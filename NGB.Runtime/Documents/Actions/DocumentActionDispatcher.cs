using System.Security.Cryptography;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Documents;
using NGB.Contracts.Services;
using NGB.Core.AuditLog;
using NGB.Core.Documents;
using NGB.Core.Documents.Actions;
using NGB.Core.Documents.Exceptions;
using NGB.Core.Events;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.Actions;
using NGB.Persistence.Outbox;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents.Derivations;
using NGB.Runtime.Observability;
using NGB.Runtime.Security;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.Documents.Actions;

internal sealed class DocumentActionDispatcher(
    IUnitOfWork uow,
    IDocumentRepository documentRepository,
    IDocumentActionExecutionRepository executions,
    IOutboxEventRepository outbox,
    DocumentActionRegistry registry,
    DocumentActionEvaluator evaluator,
    DocumentService documentService,
    IDocumentPostingService posting,
    IDocumentDerivationService derivations,
    IPermissionSnapshotProvider permissions,
    IAuditLogService audit,
    IServiceProvider services,
    TimeProvider timeProvider)
    : IDocumentActionDispatcher
{
    private const string WorkCenterConsumer = "work-center";
    private const string DocumentTypeKey = "document.type";
    private const string ActionCodeKey = "action.code";
    private const string FailureKindKey = "failure.kind";
    private const string NgbSource = "ngb";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<ExecuteDocumentActionResultDto> ExecuteAsync(
        string documentType,
        Guid documentId,
        DocumentActionCode actionCode,
        string idempotencyKey,
        ExecuteDocumentActionRequestDto request,
        CancellationToken ct)
    {
        using var activity = NgbFeatureTelemetry.Activities.StartActivity("document.action.execute");
        activity?.SetTag("ngb.document.type", documentType);
        activity?.SetTag("ngb.document.action", actionCode.Value);
        var started = Stopwatch.GetTimestamp();

        try
        {
            var result = await ExecuteCoreAsync(
                documentType,
                documentId,
                actionCode,
                idempotencyKey,
                request,
                ct);

            NgbFeatureTelemetry.DocumentActionExecutions.Add(
                1,
                new KeyValuePair<string, object?>(DocumentTypeKey, documentType),
                new KeyValuePair<string, object?>(ActionCodeKey, actionCode.Value));

            activity?.SetStatus(ActivityStatusCode.Ok);

            return result;
        }
        catch (DocumentVersionConflictException)
        {
            NgbFeatureTelemetry.DocumentActionConcurrencyConflicts.Add(
                1,
                new KeyValuePair<string, object?>(DocumentTypeKey, documentType),
                new KeyValuePair<string, object?>(ActionCodeKey, actionCode.Value));
            NgbFeatureTelemetry.DocumentActionFailures.Add(
                1,
                new KeyValuePair<string, object?>(FailureKindKey, "concurrency"));
            activity?.SetStatus(ActivityStatusCode.Error, "document version conflict");
            throw;
        }
        catch (Exception ex)
        {
            NgbFeatureTelemetry.DocumentActionFailures.Add(
                1,
                new KeyValuePair<string, object?>(FailureKindKey, ex.GetType().Name));
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            throw;
        }
        finally
        {
            NgbFeatureTelemetry.DocumentActionDuration.Record(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>(DocumentTypeKey, documentType),
                new KeyValuePair<string, object?>(ActionCodeKey, actionCode.Value));
        }
    }

    private async Task<ExecuteDocumentActionResultDto> ExecuteCoreAsync(
        string documentType,
        Guid documentId,
        DocumentActionCode actionCode,
        string idempotencyKey,
        ExecuteDocumentActionRequestDto request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(documentType))
            throw new NgbArgumentRequiredException(nameof(documentType));

        documentId.EnsureRequired(nameof(documentId));

        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new NgbArgumentRequiredException(nameof(idempotencyKey));

        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        if (request.ExpectedVersion < 1)
            throw new NgbArgumentInvalidException(nameof(request.ExpectedVersion), "Expected version must be positive.");

        var normalizedType = documentType.Trim();
        var normalizedKey = idempotencyKey.Trim();
        var definition = registry.Get(normalizedType, actionCode);

        if (definition.Metadata.ExecutionKind is DocumentActionExecutionKind.Navigation or DocumentActionExecutionKind.View)
        {
            throw new DocumentActionUnavailableException(
                normalizedType,
                actionCode.Value,
                ["document_action.client_side_target"]);
        }

        // Cheap initial authorization/availability pass. Everything is repeated under the row lock.
        var initialSnapshot = await permissions.GetCurrentAsync(ct);
        var initialDocument = await documentRepository.GetAsync(documentId, ct)
            ?? throw new DocumentNotFoundException(documentId);

        EnsureType(documentId, normalizedType, initialDocument.TypeCode);

        var initialDto = await documentService.GetByIdAsync(normalizedType, documentId, ct);

        _ = await evaluator.EvaluateOneAsync(
            definition,
            initialDocument,
            initialDto,
            initialSnapshot,
            new Dictionary<string, object?>(),
            ct);

        var fingerprint = ComputeFingerprint(normalizedType, documentId, actionCode, request);

        return await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            var locked = await documentRepository.GetForUpdateAsync(documentId, innerCt)
                ?? throw new DocumentNotFoundException(documentId);

            EnsureType(documentId, normalizedType, locked.TypeCode);

            var begin = await executions.TryBeginAsync(
                normalizedKey,
                fingerprint,
                documentId,
                normalizedType,
                actionCode.Value,
                timeProvider.GetUtcNowDateTime(),
                innerCt);

            if (begin.Status == DocumentActionExecutionBeginStatus.Completed)
            {
                return JsonSerializer.Deserialize<ExecuteDocumentActionResultDto>(begin.ResultJson!, Json)
                    ?? throw new NgbInvariantViolationException("Stored document action result could not be deserialized.");
            }

            if (begin.Status == DocumentActionExecutionBeginStatus.Conflict)
                throw new DocumentActionIdempotencyConflictException(normalizedKey);

            if (begin.Status == DocumentActionExecutionBeginStatus.InProgress)
                throw new DocumentActionInProgressException(normalizedKey);

            if (locked.Version != request.ExpectedVersion)
                throw new DocumentVersionConflictException(documentId, request.ExpectedVersion, locked.Version);

            // A fresh snapshot closes the authorization TOCTOU window between the
            // initial fast check and the transaction protected by the document row lock.
            var snapshot = await permissions.RefreshCurrentAsync(innerCt);
            var beforeDto = await documentService.GetByIdAsync(normalizedType, documentId, innerCt);
            var facts = await evaluator.LoadFactsAsync(locked, beforeDto, snapshot, innerCt);
            var evaluated = await evaluator.EvaluateOneAsync(
                definition,
                locked,
                beforeDto,
                snapshot,
                facts,
                innerCt);

            if (!evaluated.Dto.IsAllowed)
            {
                throw new DocumentActionUnavailableException(
                    normalizedType,
                    actionCode.Value,
                    evaluated.Dto.DisabledReasons.Select(static x => x.Code).ToArray());
            }

            if (definition.Metadata.Confirmation?.Mode == DocumentActionConfirmationMode.RequireReason
                && string.IsNullOrWhiteSpace(request.Reason))
            {
                throw new NgbArgumentInvalidException(nameof(request.Reason), "A reason is required for this action.");
            }

            var handlerResult = await ExecuteDefinitionAsync(
                definition,
                begin.ExecutionId,
                locked,
                beforeDto,
                request,
                snapshot.UserId,
                innerCt);

            var now = timeProvider.GetUtcNowDateTime();
            var documentVersion = await documentRepository.IncrementVersionAsync(documentId, now, innerCt);
            var refreshedDocument = await documentRepository.GetForUpdateAsync(documentId, innerCt)
                ?? throw new DocumentNotFoundException(documentId);

            var refreshedDto = await documentService.GetByIdAsync(normalizedType, documentId, innerCt);

            await audit.WriteAsync(
                AuditEntityKind.Document,
                documentId,
                $"document.action.{actionCode.Value}",
                metadata: new
                {
                    executionId = begin.ExecutionId,
                    documentType = normalizedType,
                    actionCode = actionCode.Value,
                    reason = request.Reason,
                    previousStatus = locked.Status,
                    currentStatus = refreshedDocument.Status,
                    documentVersion
                },
                correlationId: begin.ExecutionId,
                ct: innerCt);

            await AppendActionCompletedEventAsync(
                begin.ExecutionId,
                snapshot.UserId,
                locked,
                refreshedDocument,
                actionCode,
                documentVersion,
                now,
                innerCt);

            var refreshedFacts = await evaluator.LoadFactsAsync(
                refreshedDocument,
                refreshedDto,
                snapshot,
                innerCt);

            var refreshedActions = await evaluator.EvaluateAllAsync(
                refreshedDocument,
                refreshedDto,
                snapshot,
                refreshedFacts,
                innerCt);

            DocumentDto? createdDocument = null;
            if (handlerResult.CreatedDocumentId is { } createdId)
            {
                var createdRecord = await documentRepository.GetAsync(createdId, innerCt)
                    ?? throw new DocumentNotFoundException(createdId);

                createdDocument = await documentService.GetByIdAsync(createdRecord.TypeCode, createdId, innerCt);
            }

            var result = new ExecuteDocumentActionResultDto(
                begin.ExecutionId,
                actionCode.Value,
                refreshedDto,
                documentVersion,
                refreshedActions.Select(static x => x.Dto).ToArray(),
                WorkCenterMayChange: true,
                createdDocument);

            var resultJson = JsonSerializer.Serialize(result, Json);

            await executions.MarkCompletedAsync(begin.ExecutionId, resultJson, now, innerCt);

            return result;
        }, ct);
    }

    private async Task<DocumentActionHandlerResult> ExecuteDefinitionAsync(
        NGB.Definitions.Documents.Actions.DocumentActionDefinition definition,
        Guid executionId,
        DocumentRecord document,
        DocumentDto documentDto,
        ExecuteDocumentActionRequestDto request,
        Guid? actorUserId,
        CancellationToken ct)
    {
        var code = definition.Metadata.Code;
        if (code == StandardDocumentActionCodes.Post)
        {
            await posting.PostAsync(document.Id, manageTransaction: false, ct);
        }
        else if (code == StandardDocumentActionCodes.Unpost)
        {
            await posting.UnpostAsync(document.Id, manageTransaction: false, ct);
        }
        else if (code == StandardDocumentActionCodes.Repost)
        {
            await posting.RepostAsync(document.Id, manageTransaction: false, ct);
        }
        else if (code == StandardDocumentActionCodes.MarkForDeletion)
        {
            await posting.MarkForDeletionAsync(document.Id, manageTransaction: false, ct);
        }
        else if (code == StandardDocumentActionCodes.UnmarkForDeletion)
        {
            await posting.UnmarkForDeletionAsync(document.Id, manageTransaction: false, ct);
        }
        else if (definition.DerivationCode is not null)
        {
            var createdId = await derivations.CreateDraftAsync(
                definition.DerivationCode,
                document.Id,
                manageTransaction: false,
                ct: ct);

            return new DocumentActionHandlerResult(createdId);
        }
        else
        {
            var handlerType = RequireHandlerType(definition);
            var handler = (IDocumentActionHandler)services.GetRequiredService(handlerType);

            return await handler.ExecuteAsync(
                new DocumentActionHandlerContext(
                    executionId,
                    code,
                    document,
                    documentDto,
                    request.Payload,
                    request.Reason,
                    actorUserId),
                ct);
        }

        return new DocumentActionHandlerResult();
    }

    [ExcludeFromCodeCoverage(Justification = "The action registry validates every executable non-standard action has a handler before the dispatcher is constructed.")]
    private static Type RequireHandlerType(
        NGB.Definitions.Documents.Actions.DocumentActionDefinition definition)
        => definition.HandlerType
            ?? throw new NgbInvariantViolationException($"Document action '{definition.Metadata.Code}' has no runtime handler.");

    private async Task AppendActionCompletedEventAsync(
        Guid executionId,
        Guid? actorUserId,
        DocumentRecord before,
        DocumentRecord after,
        DocumentActionCode actionCode,
        long documentVersion,
        DateTime occurredAtUtc,
        CancellationToken ct)
    {
        var eventId = Guid.CreateVersion7();
        var payload = JsonSerializer.Serialize(
            new
            {
                eventId,
                type = StandardDocumentActionCodes.DocumentActionCompletedType,
                schemaVersion = 1,
                occurredAtUtc,
                source = NgbSource,
                subject = $"document/{after.TypeCode}/{after.Id}",
                actorUserId,
                correlationId = executionId,
                causationId = (Guid?)null,
                data = new
                {
                    documentId = after.Id,
                    documentType = after.TypeCode,
                    actionCode = actionCode.Value,
                    previousStatus = before.Status,
                    currentStatus = after.Status,
                    documentVersion
                }
            },
            Json);

        await outbox.AppendAsync(
            new PlatformOutboxEvent(
                eventId,
                StandardDocumentActionCodes.DocumentActionCompletedType,
                1,
                occurredAtUtc,
                NgbSource,
                $"document/{after.TypeCode}/{after.Id}",
                actorUserId,
                executionId,
                CausationId: null,
                payload,
                occurredAtUtc),
            [WorkCenterConsumer],
            ct);
    }

    private static void EnsureType(Guid documentId, string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new DocumentTypeMismatchException(documentId, expected, actual);
    }

    private static string ComputeFingerprint(
        string documentType,
        Guid documentId,
        DocumentActionCode actionCode,
        ExecuteDocumentActionRequestDto request)
    {
        var canonical = string.Join(
            '\n',
            documentType.ToLowerInvariant(),
            documentId.ToString("D"),
            actionCode.Value,
            request.ExpectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.Payload?.GetRawText() ?? "null",
            request.Reason?.Trim() ?? string.Empty);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
