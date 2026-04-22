namespace NGB.Runtime.Documents.Derivations;

/// <summary>
/// Custom per-derivation logic for prefilling the derived draft.
///
/// Typical usage:
/// - copy header fields
/// - create draft lines/allocations in typed tables
/// - update draft header via IDocumentRepository.UpdateDraftHeaderAsync
///
/// NOTE: The handler is optional. If no handler is configured, only the draft header is created
/// (via DocumentDraftService) and relationships are written.
/// </summary>
public interface IDocumentDerivationHandler
{
    Task ApplyAsync(DocumentDerivationContext ctx, CancellationToken ct = default);
}
