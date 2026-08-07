using System.Text.Json;
using NGB.Contracts.Documents;
using NGB.Contracts.Services;
using NGB.Core.Documents;
using NGB.Core.Documents.Actions;

namespace NGB.Application.Abstractions.Services;

public sealed record DocumentActionEvaluationContext(
    DocumentRecord Document,
    DocumentDto DocumentDto,
    DocumentActionSecurityContext Security,
    IReadOnlyDictionary<string, object?> Facts);

public sealed record DocumentActionContextRequest(
    DocumentRecord Document,
    DocumentDto DocumentDto,
    DocumentActionSecurityContext Security);

public sealed record DocumentActionAuthorizationResult(bool IsAuthorized)
{
    public static DocumentActionAuthorizationResult Authorized { get; } = new(true);
    public static DocumentActionAuthorizationResult Denied { get; } = new(false);
}

public sealed record DocumentActionAvailabilityResult(IReadOnlyList<DocumentActionDisabledReasonDto> DisabledReasons)
{
    public bool IsAllowed => DisabledReasons.Count == 0;

    public static DocumentActionAvailabilityResult Allowed { get; } = new([]);
}

public sealed record DocumentActionHandlerContext(
    Guid ExecutionId,
    DocumentActionCode ActionCode,
    DocumentRecord Document,
    DocumentDto DocumentDto,
    JsonElement? Payload,
    string? Reason,
    Guid? ActorUserId);

public sealed record DocumentActionHandlerResult(Guid? CreatedDocumentId = null);

public interface IDocumentActionHandler
{
    Task<DocumentActionHandlerResult> ExecuteAsync(DocumentActionHandlerContext context, CancellationToken ct);
}

public interface IDocumentActionAvailabilityEvaluator
{
    ValueTask<DocumentActionAvailabilityResult> EvaluateAsync(
        DocumentActionEvaluationContext context,
        CancellationToken ct);
}

public interface IDocumentActionAuthorizationEvaluator
{
    ValueTask<DocumentActionAuthorizationResult> EvaluateAsync(
        DocumentActionEvaluationContext context,
        CancellationToken ct);
}

public interface IDocumentActionContextEnricher
{
    string DocumentTypeCode { get; }

    Task<IReadOnlyDictionary<string, object?>> LoadFactsAsync(
        DocumentActionContextRequest request,
        CancellationToken ct);
}

public interface IDocumentActionQueryService
{
    Task<DocumentEditorStateDto> GetEditorStateAsync(
        string documentType,
        Guid documentId,
        CancellationToken ct);
}

public interface IDocumentActionDispatcher
{
    Task<ExecuteDocumentActionResultDto> ExecuteAsync(
        string documentType,
        Guid documentId,
        DocumentActionCode actionCode,
        string idempotencyKey,
        ExecuteDocumentActionRequestDto request,
        CancellationToken ct);
}
