namespace NGB.Runtime.Catalogs;

public interface ICatalogDraftService
{
    Task<Guid> CreateAsync(
        string catalogCode,
        bool manageTransaction = true,
        bool suppressAudit = false,
        CancellationToken ct = default);

    // Backward-compatible overload for existing call sites that pass (catalogCode, manageTransaction, ct).
    Task<Guid> CreateAsync(string catalogCode, bool manageTransaction, CancellationToken ct);

    /// <summary>
    /// Creates the catalog header row (catalogs) and audit log entry, but DOES NOT create the typed storage row (cat_*).
    ///
    /// Rationale:
    /// - <see cref="NGB.Application.Abstractions.Services.ICatalogService.CreateAsync"/> validates payload and then upserts the head row in the same transaction.
    /// - Some catalogs may have NOT NULL / CHECK invariants that cannot be satisfied by a "draft placeholder" row.
    /// </summary>
    Task<Guid> CreateHeaderOnlyAsync(
        string catalogCode,
        bool manageTransaction = true,
        bool suppressAudit = false,
        CancellationToken ct = default);

    // Backward-compatible overload for existing call sites that pass (catalogCode, manageTransaction, ct).
    Task<Guid> CreateHeaderOnlyAsync(string catalogCode, bool manageTransaction, CancellationToken ct);

    Task MarkForDeletionAsync(Guid catalogId, bool manageTransaction = true, CancellationToken ct = default);

    Task UnmarkForDeletionAsync(Guid catalogId, bool manageTransaction = true, CancellationToken ct = default);
}
