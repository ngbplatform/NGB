using NGB.Core.Documents;

namespace NGB.Runtime.Documents.Derivations;

/// <summary>
/// Runtime context passed to a derivation handler.
///
/// IMPORTANT: the handler runs inside the same UnitOfWork transaction as draft creation
/// and relationship writes.
/// </summary>
public sealed record DocumentDerivationContext(
    string DerivationCode,
    DocumentRecord SourceDocument,
    DocumentRecord TargetDraft,
    IReadOnlyCollection<Guid> BasedOnDocumentIds);
