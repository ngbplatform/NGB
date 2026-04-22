using NGB.Core.Documents;

namespace NGB.Runtime.Documents.Numbering;

/// <summary>
/// Convenience service for workflows that ensure a document number outside of
/// <see cref="NGB.Runtime.Documents.DocumentPostingService"/>.
///
/// If a number is assigned, the service also synchronizes typed draft storage
/// (via <see cref="NGB.Runtime.Documents.DocumentWriteEngine"/>) so type-specific tables
/// that denormalize header fields (number/date) stay consistent.
/// </summary>
public interface IDocumentNumberingAndTypedSyncService
{
    /// <summary>
    /// Ensures a document has a number. If it is newly assigned (i.e. the document had no number
    /// before the call), typed draft storage is synchronized.
    ///
    /// Requires an active transaction, and assumes the document row is locked (FOR UPDATE) by the caller.
    /// </summary>
    Task<string> EnsureNumberAndSyncTypedAsync(
        DocumentRecord documentForUpdate,
        DateTime nowUtc,
        CancellationToken ct = default);
}
