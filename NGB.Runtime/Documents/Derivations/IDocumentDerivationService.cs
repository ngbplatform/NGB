using NGB.Definitions;

namespace NGB.Runtime.Documents.Derivations;

/// <summary>
/// Platform service implementing the "Create based on" feature.
///
/// The service uses <see cref="NGB.Definitions.Documents.Derivations.DocumentDerivationDefinition"/>
/// registered in <see cref="DefinitionsRegistry"/>.
/// </summary>
public interface IDocumentDerivationService
{
    IReadOnlyList<DocumentDerivationAction> ListActionsForSourceType(string sourceTypeCode);

    Task<IReadOnlyList<DocumentDerivationAction>> ListActionsForDocumentAsync(
        Guid sourceDocumentId,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a new draft document according to the derivation definition.
    ///
    /// Relationship creation rules:
    /// - "created_from" is written once to <paramref name="createdFromDocumentId"/>.
    /// - "based_on" is written to <paramref name="createdFromDocumentId"/> and to every id
    ///   in <paramref name="basedOnDocumentIds"/>.
    /// - Any other relationship code is written once to <paramref name="createdFromDocumentId"/>.
    ///
    /// IMPORTANT: the method creates a Draft only and does not post it.
    /// </summary>
    Task<Guid> CreateDraftAsync(
        string derivationCode,
        Guid createdFromDocumentId,
        IReadOnlyList<Guid>? basedOnDocumentIds = null,
        DateTime? dateUtc = null,
        string? number = null,
        bool manageTransaction = true,
        CancellationToken ct = default);
}
