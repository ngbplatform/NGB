namespace NGB.Persistence.Documents.Actions;

public enum DocumentActionExecutionBeginStatus
{
    Begun = 1,
    InProgress = 2,
    Completed = 3,
    Conflict = 4
}

public sealed record DocumentActionExecutionBeginResult(
    DocumentActionExecutionBeginStatus Status,
    Guid ExecutionId,
    string? ResultJson);

public interface IDocumentActionExecutionRepository
{
    Task<DocumentActionExecutionBeginResult> TryBeginAsync(
        string idempotencyKey,
        string requestFingerprint,
        Guid documentId,
        string documentType,
        string actionCode,
        DateTime startedAtUtc,
        CancellationToken ct);

    Task MarkCompletedAsync(
        Guid executionId,
        string resultJson,
        DateTime completedAtUtc,
        CancellationToken ct);
}
