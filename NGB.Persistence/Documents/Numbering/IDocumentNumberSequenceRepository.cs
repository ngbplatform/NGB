namespace NGB.Persistence.Documents.Numbering;

/// <summary>
/// Allocates sequential numbers per (document type, fiscal year).
///
/// IMPORTANT:
/// - Implementations must be concurrency-safe.
/// - Allocation must require an active transaction to avoid gaps on rollback.
/// </summary>
public interface IDocumentNumberSequenceRepository
{
    /// <summary>
    /// Gets the next sequence number for the given (typeCode, fiscalYear).
    /// Requires an active transaction.
    /// </summary>
    Task<long> NextAsync(string typeCode, int fiscalYear, CancellationToken ct = default);
}
