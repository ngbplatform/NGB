using NGB.Core.Documents;

namespace NGB.Persistence.Documents.Storage;

/// <summary>
/// Optional hook for document types that mirror the full platform document header (<c>documents</c> table)
/// into typed tables for query convenience.
///
/// The platform updates the common document registry first and then calls this hook
/// in the SAME transaction. Implementations must assume an active transaction.
///
/// This hook is used for any update of common document fields (e.g. draft header updates,
/// number assignment, mark/unmark for deletion state changes).
/// </summary>
public interface IDocumentTypeDraftFullUpdater
{
    /// <summary>
    /// Called after a Draft document was updated in the common document registry.
    /// </summary>
    Task UpdateDraftAsync(DocumentRecord updatedDraft, CancellationToken ct = default);
}
