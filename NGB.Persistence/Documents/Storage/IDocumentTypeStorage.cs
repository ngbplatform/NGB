namespace NGB.Persistence.Documents.Storage;

/// <summary>
/// Per-document-type storage operations for typed tables (doc_{type_code}, doc_{type_code}__parts).
/// Implementations live in vertical solutions.
/// IMPORTANT: Implementations MUST assume they are called within an active transaction.
/// </summary>
public interface IDocumentTypeStorage
{
    /// <summary>
    /// Document type code (matches documents.type_code).
    /// </summary>
    string TypeCode { get; }

    /// <summary>
    /// Creates the per-type draft data for the given document id (header + parts).
    /// Called when the platform creates a Draft document in the registry.
    /// </summary>
    Task CreateDraftAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>
    /// Deletes/cleans up per-type draft data for the given document id.
    /// Typically used if a draft is removed (if the solution supports hard-delete).
    /// </summary>
    Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default);
}
