namespace NGB.Persistence.Writers;

/// <summary>
/// Narrow maintenance mutations for accounting register entries.
///
/// IMPORTANT:
/// - methods must be executed inside an active transaction;
/// - callers are responsible for period-level locking and higher-level invariants.
/// </summary>
public interface IAccountingEntryMaintenanceWriter
{
    /// <summary>
    /// Deletes all accounting register rows for the given document and returns distinct affected periods.
    /// </summary>
    Task<IReadOnlyList<DateOnly>> DeleteByDocumentAsync(Guid documentId, CancellationToken ct = default);
}
