namespace NGB.Persistence.Reporting;

/// <summary>Durable queue and immutable, owner-scoped report results. Attempts are fenced by a lease token.</summary>
public interface IReportRunStore
{
    Task CreateAsync(Guid id, string owner, string reportCode, string preparedJson, CancellationToken ct);
    Task<StoredReportRun?> FindAsync(Guid id, string owner, string reportCode, CancellationToken ct);
    Task<StoredReportRun?> ClaimAsync(string[] reportCodes, CancellationToken ct);
    Task<bool> RenewAsync(Guid id, Guid attempt, CancellationToken ct);
    Task AppendAsync(Guid id, Guid attempt, int offset, string[] rows, CancellationToken ct);
    Task WriteAsync(Guid id, Guid attempt, int[] ordinals, string[] rows, CancellationToken ct);
    Task<bool> CompleteAsync(Guid id, Guid attempt, string templateJson, int count, CancellationToken ct);
    Task FailAsync(Guid id, Guid attempt, CancellationToken ct);
    Task FailAsync(Guid id, Guid attempt, string? failureJson, CancellationToken ct);
    Task CancelAsync(Guid id, string owner, string reportCode, CancellationToken ct);
    Task<IReadOnlyList<string>> ReadRowsAsync(Guid id, Guid attempt, int offset, int limit, CancellationToken ct);
    Task CleanupAsync(CancellationToken ct);
}

public sealed record StoredReportRun(
    Guid Id,
    string Owner,
    string ReportCode,
    string PreparedJson,
    string Status,
    Guid Attempt,
    string? TemplateJson,
    int RowCount,
    DateTime ExpiresAtUtc);
