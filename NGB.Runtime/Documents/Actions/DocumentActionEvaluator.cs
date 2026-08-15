using NGB.Application.Abstractions.Services;
using NGB.Contracts.Documents;
using NGB.Contracts.Services;
using NGB.Core.Documents;
using NGB.Core.Documents.Actions;
using NGB.Core.Security;
using NGB.Definitions;
using NGB.Definitions.Documents.Actions;
using NGB.Runtime.Security;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.Actions;

internal sealed class EvaluatedDocumentAction(DocumentActionDefinition definition, DocumentActionDto dto)
{
    public DocumentActionDefinition Definition { get; } = definition;
    public DocumentActionDto Dto { get; } = dto;
}

internal sealed class DocumentActionEvaluator(
    DocumentActionRegistry registry,
    DefinitionsRegistry definitions,
    IDocumentActionComponentResolver components,
    IEnumerable<IDocumentActionContextEnricher> enrichers)
{
    private readonly IReadOnlyList<IDocumentActionContextEnricher> _enrichers = enrichers.ToArray();

    public static bool RequiresEnrichedContextForExecution(DocumentActionDefinition definition)
        => definition.HandlerType is not null
           || definition.AuthorizationEvaluatorType is not null
           || definition.AvailabilityEvaluatorType is not null;

    public static bool RequiresFactsForExecution(DocumentActionDefinition definition)
        => definition.AuthorizationEvaluatorType is not null
           || definition.AvailabilityEvaluatorType is not null;

    /// <summary>
    /// Evaluates the executable action without materializing a DTO for standard lifecycle actions.
    /// Extension evaluators still receive the same rich context and preloaded facts.
    /// </summary>
    public async Task<DocumentActionAvailabilityResult> EvaluateForExecutionAsync(
        DocumentActionDefinition definition,
        DocumentRecord document,
        DocumentDto? documentDto,
        PermissionSnapshot snapshot,
        IReadOnlyDictionary<string, object?> facts,
        CancellationToken ct)
    {
        DocumentActionEvaluationContext? context = null;
        if (definition.AuthorizationEvaluatorType is not null
            || definition.AvailabilityEvaluatorType is not null)
        {
            if (documentDto is null)
                throw new NgbInvariantViolationException(
                    $"Document action '{definition.Metadata.Code}' requires an enriched document context.");

            context = new DocumentActionEvaluationContext(
                document,
                documentDto,
                ToSecurityContext(snapshot),
                facts);
        }

        var authorized = definition.AuthorizationEvaluatorType is null
            ? IsAuthorizedByMetadata(definition, snapshot)
            : await IsAuthorizedAsync(definition, context!, snapshot, ct);

        if (!authorized)
            throw new DocumentActionForbiddenException(document.TypeCode, definition.Metadata.Code.Value);

        var reasons = GetStandardDisabledReasons(definition.Metadata.Code, document);
        if (definition.AvailabilityEvaluatorType is not null)
        {
            var custom = await components
                .ResolveAvailabilityEvaluator(definition.AvailabilityEvaluatorType)
                .EvaluateAsync(context!, ct);
            reasons.AddRange(custom.DisabledReasons);
        }

        return reasons.Count == 0
            ? DocumentActionAvailabilityResult.Allowed
            : new DocumentActionAvailabilityResult(
                reasons
                    .OrderBy(static x => x.Code, StringComparer.Ordinal)
                    .ThenBy(static x => x.Message, StringComparer.Ordinal)
                    .ToArray());
    }

    public async Task<IReadOnlyDictionary<string, object?>> LoadFactsAsync(
        DocumentRecord document,
        DocumentDto documentDto,
        PermissionSnapshot snapshot,
        CancellationToken ct)
    {
        var matches = _enrichers
            .Where(x => string.Equals(x.DocumentTypeCode, document.TypeCode, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length > 1)
            throw new NgbConfigurationViolationException($"Only one document action context enricher may be registered for '{document.TypeCode}'.");

        if (matches.Length == 0)
            return new Dictionary<string, object?>();

        return await matches[0].LoadFactsAsync(
            new DocumentActionContextRequest(document, documentDto, ToSecurityContext(snapshot)),
            ct);
    }

    public async Task<IReadOnlyList<EvaluatedDocumentAction>> EvaluateAllAsync(
        DocumentRecord document,
        DocumentDto documentDto,
        PermissionSnapshot snapshot,
        IReadOnlyDictionary<string, object?> facts,
        CancellationToken ct)
    {
        var context = new DocumentActionEvaluationContext(
            document,
            documentDto,
            ToSecurityContext(snapshot),
            facts);

        var result = new List<EvaluatedDocumentAction>();
        foreach (var definition in registry.GetForDocumentType(document.TypeCode))
        {
            if (!await IsAuthorizedAsync(definition, context, snapshot, ct))
                continue;

            var availability = await GetAvailabilityAsync(definition, context, ct);

            result.Add(new EvaluatedDocumentAction(
                definition,
                ToDto(definition, availability, document, documentDto, createdDocumentId: null)));
        }

        return result;
    }

    public async Task<EvaluatedDocumentAction> EvaluateOneAsync(
        DocumentActionDefinition definition,
        DocumentRecord document,
        DocumentDto documentDto,
        PermissionSnapshot snapshot,
        IReadOnlyDictionary<string, object?> facts,
        CancellationToken ct)
    {
        var context = new DocumentActionEvaluationContext(
            document,
            documentDto,
            ToSecurityContext(snapshot),
            facts);

        if (!await IsAuthorizedAsync(definition, context, snapshot, ct))
            throw new DocumentActionForbiddenException(document.TypeCode, definition.Metadata.Code.Value);

        var availability = await GetAvailabilityAsync(definition, context, ct);

        return new EvaluatedDocumentAction(
            definition,
            ToDto(definition, availability, document, documentDto, createdDocumentId: null));
    }

    public DocumentActionDto ToDto(
        DocumentActionDefinition definition,
        DocumentActionAvailabilityResult availability,
        DocumentRecord document,
        DocumentDto documentDto,
        Guid? createdDocumentId)
    {
        var metadata = definition.Metadata;

        var target = metadata.Target is null
            ? null
            : new DocumentActionTargetDto(
                metadata.Target.Code,
                metadata.Target.Parameters.ToDictionary(
                    static x => x.Key,
                    x => ReplaceTargetToken(x.Value, document, documentDto, createdDocumentId),
                    StringComparer.Ordinal));

        return new DocumentActionDto(
            metadata.Code.Value,
            metadata.Presentation.Label,
            metadata.Presentation.LabelKey,
            metadata.Presentation.Description,
            metadata.Presentation.Icon,
            (NGB.Contracts.Documents.DocumentActionKind)metadata.Kind,
            (NGB.Contracts.Documents.DocumentActionExecutionKind)metadata.ExecutionKind,
            metadata.Order,
            availability.IsAllowed,
            availability.DisabledReasons,
            metadata.Confirmation is null
                ? null
                : new DocumentActionConfirmationDto(
                    (NGB.Contracts.Documents.DocumentActionConfirmationMode)metadata.Confirmation.Mode,
                    metadata.Confirmation.Title,
                    metadata.Confirmation.Message,
                    metadata.Confirmation.ConfirmLabel),
            target);
    }

    private async ValueTask<bool> IsAuthorizedAsync(
        DocumentActionDefinition definition,
        DocumentActionEvaluationContext context,
        PermissionSnapshot snapshot,
        CancellationToken ct)
    {
        if (definition.AuthorizationEvaluatorType is not null)
        {
            var evaluator = components.ResolveAuthorizationEvaluator(definition.AuthorizationEvaluatorType);

            return (await evaluator.EvaluateAsync(context, ct)).IsAuthorized;
        }

        return IsAuthorizedByMetadata(definition, snapshot);
    }

    private bool IsAuthorizedByMetadata(DocumentActionDefinition definition, PermissionSnapshot snapshot)
    {
        var action = definition.Metadata.Code;
        var permissionAction = GetStandardPermission(action);

        if (permissionAction is not null)
            return snapshot.Has(NgbResourceKinds.Document, definition.DocumentTypeCode, permissionAction);

        if (definition.DerivationCode is not null)
        {
            var derivation = definitions.GetDocumentDerivation(definition.DerivationCode);

            return snapshot.Has(NgbResourceKinds.Document, derivation.FromTypeCode, NgbPermissionActions.View)
                   && snapshot.Has(NgbResourceKinds.Document, derivation.ToTypeCode, NgbPermissionActions.Create)
                   && snapshot.Has(NgbResourceKinds.Document, derivation.ToTypeCode, NgbPermissionActions.View);
        }

        return snapshot.Has(NgbResourceKinds.Document, definition.DocumentTypeCode, NgbPermissionActions.View);
    }

    private async ValueTask<DocumentActionAvailabilityResult> GetAvailabilityAsync(
        DocumentActionDefinition definition,
        DocumentActionEvaluationContext context,
        CancellationToken ct)
    {
        var reasons = GetStandardDisabledReasons(definition.Metadata.Code, context.Document);

        if (definition.AvailabilityEvaluatorType is not null)
        {
            var evaluator = components.ResolveAvailabilityEvaluator(definition.AvailabilityEvaluatorType);
            var custom = await evaluator.EvaluateAsync(context, ct);
            reasons.AddRange(custom.DisabledReasons);
        }

        return reasons.Count == 0
            ? DocumentActionAvailabilityResult.Allowed
            : new DocumentActionAvailabilityResult(
                reasons
                    .OrderBy(static x => x.Code, StringComparer.Ordinal)
                    .ThenBy(static x => x.Message, StringComparer.Ordinal)
                    .ToArray());
    }

    private static List<DocumentActionDisabledReasonDto> GetStandardDisabledReasons(
        DocumentActionCode action,
        DocumentRecord document)
    {
        var reasons = new List<DocumentActionDisabledReasonDto>();
        if (action == StandardDocumentActionCodes.Post && document.Status != DocumentStatus.Draft)
            reasons.Add(Reason("document.not_draft", "Only Draft documents can be posted."));
        else if (action == StandardDocumentActionCodes.Unpost && document.Status != DocumentStatus.Posted)
            reasons.Add(Reason("document.not_posted", "Only Posted documents can be unposted."));
        else if (action == StandardDocumentActionCodes.Repost && document.Status != DocumentStatus.Posted)
            reasons.Add(Reason("document.not_posted", "Only Posted documents can be reposted."));
        else if (action == StandardDocumentActionCodes.MarkForDeletion && document.Status != DocumentStatus.Draft)
            reasons.Add(Reason("document.not_draft", "Only Draft documents can be marked for deletion."));
        else if (action == StandardDocumentActionCodes.UnmarkForDeletion && document.Status != DocumentStatus.MarkedForDeletion)
            reasons.Add(Reason("document.not_marked_for_deletion", "Only marked documents can be restored."));

        return reasons;
    }

    private static DocumentActionDisabledReasonDto Reason(string code, string message) => new(code, message);

    private static string? GetStandardPermission(DocumentActionCode action)
    {
        if (action == StandardDocumentActionCodes.Post)
            return NgbPermissionActions.Post;

        if (action == StandardDocumentActionCodes.Unpost)
            return NgbPermissionActions.Unpost;

        if (action == StandardDocumentActionCodes.Repost)
            return NgbPermissionActions.Repost;

        if (action == StandardDocumentActionCodes.MarkForDeletion)
            return NgbPermissionActions.MarkForDeletion;

        if (action == StandardDocumentActionCodes.UnmarkForDeletion)
            return NgbPermissionActions.UnmarkForDeletion;

        if (action == StandardDocumentActionCodes.ViewEffects)
            return NgbPermissionActions.ViewEffects;

        if (action == StandardDocumentActionCodes.ViewFlow)
            return NgbPermissionActions.ViewFlow;

        if (action == StandardDocumentActionCodes.ViewAudit)
            return NgbPermissionActions.ViewAudit;

        if (action == StandardDocumentActionCodes.Print)
            return NgbPermissionActions.Print;

        return null;
    }

    private static DocumentActionSecurityContext ToSecurityContext(PermissionSnapshot snapshot)
        => new(
            snapshot.UserId,
            snapshot.IsAuthenticated,
            snapshot.IsActive,
            snapshot.IsBootstrapAdmin,
            snapshot.Permissions);

    private static string? ReplaceTargetToken(
        string? value,
        DocumentRecord document,
        DocumentDto documentDto,
        Guid? createdDocumentId)
    {
        if (value is null)
            return null;

        const string fieldPrefix = "{field:";
        if (value.StartsWith(fieldPrefix, StringComparison.Ordinal) && value.EndsWith('}'))
        {
            var fieldName = value[fieldPrefix.Length..^1];
            if (documentDto.Payload.Fields?.TryGetValue(fieldName, out var field) == true)
                return ReadTargetField(field);

            return null;
        }

        return value
            .Replace("{documentId}", document.Id.ToString(), StringComparison.Ordinal)
            .Replace("{documentType}", document.TypeCode, StringComparison.Ordinal)
            .Replace(
                "{createdDocumentId}",
                createdDocumentId?.ToString() ?? "{createdDocumentId}",
                StringComparison.Ordinal);
    }

    private static string? ReadTargetField(System.Text.Json.JsonElement field)
    {
        if (field.ValueKind == System.Text.Json.JsonValueKind.String)
            return field.GetString();

        if (field.ValueKind == System.Text.Json.JsonValueKind.Object
            && field.TryGetProperty("id", out var id)
            && id.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            return id.GetString();
        }

        return field.ToString();
    }
}
