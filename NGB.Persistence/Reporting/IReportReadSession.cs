namespace NGB.Persistence.Reporting;

/// <summary>One consistent source view across the queries in a report request.</summary>
public interface IReportReadSession
{
    /// <summary>Unique for the active consistent read; empty outside a session.</summary>
    Guid SnapshotId => Guid.Empty;

    Task BeginAsync(CancellationToken ct);
    Task EndAsync(CancellationToken ct);
}
