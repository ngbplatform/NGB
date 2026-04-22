using NGB.Definitions.Catalogs;
using NGB.Definitions.Documents;
using NGB.Definitions.Documents.Derivations;
using NGB.Definitions.Documents.Relationships;
using NGB.Tools.Exceptions;

namespace NGB.Definitions;

/// <summary>
/// Immutable snapshot of all registered definitions.
/// </summary>
public sealed class DefinitionsRegistry(
    IReadOnlyDictionary<string, DocumentTypeDefinition> documents,
    IReadOnlyDictionary<string, CatalogTypeDefinition> catalogs,
    IReadOnlyDictionary<string, DocumentRelationshipTypeDefinition> documentRelationshipTypes,
    IReadOnlyDictionary<string, DocumentDerivationDefinition> documentDerivations)
{
    private readonly IReadOnlyDictionary<string, DocumentTypeDefinition> _documents = documents
        ?? throw new NgbArgumentRequiredException(nameof(documents));

    private readonly IReadOnlyDictionary<string, CatalogTypeDefinition> _catalogs = catalogs
        ?? throw new NgbArgumentRequiredException(nameof(catalogs));

    private readonly IReadOnlyDictionary<string, DocumentRelationshipTypeDefinition> _documentRelationshipTypes = documentRelationshipTypes
        ?? throw new NgbArgumentRequiredException(nameof(documentRelationshipTypes));

    private readonly IReadOnlyDictionary<string, DocumentDerivationDefinition> _documentDerivations = documentDerivations
        ?? throw new NgbArgumentRequiredException(nameof(documentDerivations));

    public IEnumerable<DocumentTypeDefinition> Documents => _documents.Values;
    public IEnumerable<CatalogTypeDefinition> Catalogs => _catalogs.Values;
    public IEnumerable<DocumentRelationshipTypeDefinition> DocumentRelationshipTypes => _documentRelationshipTypes.Values;
    public IEnumerable<DocumentDerivationDefinition> DocumentDerivations => _documentDerivations.Values;

    public bool TryGetDocument(string typeCode, out DocumentTypeDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(typeCode))
            throw new NgbArgumentInvalidException(nameof(typeCode), "Type code must be non-empty.");

        return _documents.TryGetValue(typeCode, out definition!);
    }

    public DocumentTypeDefinition GetDocument(string typeCode)
    {
        if (!TryGetDocument(typeCode, out var def))
            throw new NgbConfigurationViolationException(
                $"Document type '{typeCode}' is not registered.",
                context: new Dictionary<string, object?>
                {
                    ["definitionKind"] = "document",
                    ["typeCode"] = typeCode
                });

        return def;
    }

    public bool TryGetCatalog(string typeCode, out CatalogTypeDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(typeCode))
            throw new NgbArgumentInvalidException(nameof(typeCode), "Type code must be non-empty.");

        return _catalogs.TryGetValue(typeCode, out definition!);
    }

    public CatalogTypeDefinition GetCatalog(string typeCode)
    {
        if (!TryGetCatalog(typeCode, out var def))
            throw new NgbConfigurationViolationException(
                $"Catalog type '{typeCode}' is not registered.",
                context: new Dictionary<string, object?>
                {
                    ["definitionKind"] = "catalog",
                    ["typeCode"] = typeCode
                });

        return def;
    }

    public bool TryGetDocumentRelationshipType(string relationshipCode, out DocumentRelationshipTypeDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(relationshipCode))
            throw new NgbArgumentInvalidException(nameof(relationshipCode), "Relationship code must be non-empty.");

        return _documentRelationshipTypes.TryGetValue(relationshipCode, out definition!);
    }

    public DocumentRelationshipTypeDefinition GetDocumentRelationshipType(string relationshipCode)
    {
        if (!TryGetDocumentRelationshipType(relationshipCode, out var def))
            throw new NgbConfigurationViolationException(
                $"Document relationship type '{relationshipCode}' is not registered.",
                context: new Dictionary<string, object?>
                {
                    ["definitionKind"] = "document_relationship",
                    ["code"] = relationshipCode
                });

        return def;
    }

    public bool TryGetDocumentDerivation(string derivationCode, out DocumentDerivationDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(derivationCode))
            throw new NgbArgumentInvalidException(nameof(derivationCode), "Derivation code must be non-empty.");

        return _documentDerivations.TryGetValue(derivationCode, out definition!);
    }

    public DocumentDerivationDefinition GetDocumentDerivation(string derivationCode)
    {
        if (!TryGetDocumentDerivation(derivationCode, out var def))
            throw new NgbConfigurationViolationException(
                $"Document derivation '{derivationCode}' is not registered.",
                context: new Dictionary<string, object?>
                {
                    ["definitionKind"] = "document_derivation",
                    ["code"] = derivationCode
                });

        return def;
    }
}
