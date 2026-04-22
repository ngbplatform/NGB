using NGB.Definitions.Catalogs;
using NGB.Definitions.Documents;
using NGB.Definitions.Documents.Derivations;
using NGB.Definitions.Documents.Relationships;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Documents.Hybrid;
using NGB.Tools.Exceptions;

namespace NGB.Definitions;

/// <summary>
/// Mutable builder used by modules to register and extend definitions.
/// </summary>
public sealed class DefinitionsBuilder
{
    private const int MaxRelationshipCodeLength = 128;
    private const int MaxDerivationCodeLength = 128;

    private readonly Dictionary<string, MutableDocumentTypeDefinition> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MutableCatalogTypeDefinition> _catalogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MutableDocumentRelationshipTypeDefinition> _documentRelationshipTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MutableDocumentDerivationDefinition> _documentDerivations = new(StringComparer.OrdinalIgnoreCase);

    public void AddDocument(string typeCode, Action<DocumentTypeDefinitionBuilder> configure)
    {
        if (string.IsNullOrWhiteSpace(typeCode))
            throw new NgbArgumentInvalidException(nameof(typeCode), "Type code must be non-empty.");

        if (configure is null)
            throw new NgbArgumentRequiredException(nameof(configure));

        if (_documents.ContainsKey(typeCode))
            throw new NgbConfigurationViolationException(
                $"Document type '{typeCode}' is already registered.",
                context: new Dictionary<string, object?>
                {
                    ["definitionKind"] = "document",
                    ["typeCode"] = typeCode
                });

        var mutable = new MutableDocumentTypeDefinition(typeCode);
        var builder = new DocumentTypeDefinitionBuilder(mutable);
        configure(builder);

        mutable.ValidateRequired();
        _documents.Add(typeCode, mutable);
    }

    public void ExtendDocument(string typeCode, Action<DocumentTypeDefinitionBuilder> configure)
    {
        if (string.IsNullOrWhiteSpace(typeCode))
            throw new NgbArgumentInvalidException(nameof(typeCode), "Type code must be non-empty.");

        if (configure is null)
            throw new NgbArgumentRequiredException(nameof(configure));

        if (!_documents.TryGetValue(typeCode, out var mutable))
            throw new NgbConfigurationViolationException(
                $"Cannot extend document type '{typeCode}' because it is not registered. Use AddDocument(...) first.",
                context: new Dictionary<string, object?>
                {
                    ["definitionKind"] = "document",
                    ["typeCode"] = typeCode
                });

        var builder = new DocumentTypeDefinitionBuilder(mutable);
        configure(builder);

        mutable.ValidateRequired();
    }

    public void AddCatalog(string typeCode, Action<CatalogTypeDefinitionBuilder> configure)
    {
        if (string.IsNullOrWhiteSpace(typeCode))
            throw new NgbArgumentInvalidException(nameof(typeCode), "Type code must be non-empty.");

        if (configure is null)
            throw new NgbArgumentRequiredException(nameof(configure));

        if (_catalogs.ContainsKey(typeCode))
            throw new NgbConfigurationViolationException(
                $"Catalog type '{typeCode}' is already registered.",
                context: new Dictionary<string, object?>
                {
                    ["definitionKind"] = "catalog",
                    ["typeCode"] = typeCode
                });

        var mutable = new MutableCatalogTypeDefinition(typeCode);
        var builder = new CatalogTypeDefinitionBuilder(mutable);
        configure(builder);

        mutable.ValidateRequired();
        _catalogs.Add(typeCode, mutable);
    }

    public void ExtendCatalog(string typeCode, Action<CatalogTypeDefinitionBuilder> configure)
    {
        if (string.IsNullOrWhiteSpace(typeCode))
            throw new NgbArgumentInvalidException(nameof(typeCode), "Type code must be non-empty.");

        if (configure is null)
            throw new NgbArgumentRequiredException(nameof(configure));

        if (!_catalogs.TryGetValue(typeCode, out var mutable))
            throw new NgbConfigurationViolationException(
                $"Cannot extend catalog type '{typeCode}' because it is not registered. Use AddCatalog(...) first.",
                context: new Dictionary<string, object?>
                {
                    ["definitionKind"] = "catalog",
                    ["typeCode"] = typeCode
                });

        var builder = new CatalogTypeDefinitionBuilder(mutable);
        configure(builder);

        mutable.ValidateRequired();
    }

    public void AddDocumentRelationshipType(
        string relationshipCode,
        Action<DocumentRelationshipTypeDefinitionBuilder> configure)
    {
        var code = NormalizeRelationshipCode(relationshipCode);

        if (configure is null)
            throw new NgbArgumentRequiredException(nameof(configure));

        if (_documentRelationshipTypes.ContainsKey(code))
            throw new NgbConfigurationViolationException(
                $"Document relationship type '{code}' is already registered.",
                context: new Dictionary<string, object?>
                {
                    ["definitionKind"] = "document_relationship",
                    ["code"] = code
                });

        var mutable = new MutableDocumentRelationshipTypeDefinition(code);
        var builder = new DocumentRelationshipTypeDefinitionBuilder(mutable);
        configure(builder);

        mutable.ValidateRequired();
        _documentRelationshipTypes.Add(code, mutable);
    }

    public void ExtendDocumentRelationshipType(
        string relationshipCode,
        Action<DocumentRelationshipTypeDefinitionBuilder> configure)
    {
        var code = NormalizeRelationshipCode(relationshipCode);

        if (configure is null)
            throw new NgbArgumentRequiredException(nameof(configure));

        if (!_documentRelationshipTypes.TryGetValue(code, out var mutable))
            throw new NgbConfigurationViolationException(
                $"Cannot extend document relationship type '{code}' because it is not registered. Use AddDocumentRelationshipType(...) first.",
                context: new Dictionary<string, object?>
                {
                    ["definitionKind"] = "document_relationship",
                    ["code"] = code
                });

        var builder = new DocumentRelationshipTypeDefinitionBuilder(mutable);
        configure(builder);

        mutable.ValidateRequired();
    }

    public void AddDocumentDerivation(string derivationCode, Action<DocumentDerivationDefinitionBuilder> configure)
    {
        var code = NormalizeDerivationCode(derivationCode);

        if (configure is null)
            throw new NgbArgumentRequiredException(nameof(configure));

        if (_documentDerivations.ContainsKey(code))
            throw new NgbConfigurationViolationException(
                $"Document derivation '{code}' is already registered.",
                context: new Dictionary<string, object?>
                {
                    ["definitionKind"] = "document_derivation",
                    ["code"] = code
                });

        var mutable = new MutableDocumentDerivationDefinition(code);
        var builder = new DocumentDerivationDefinitionBuilder(mutable);
        configure(builder);

        mutable.ValidateRequired();
        _documentDerivations.Add(code, mutable);
    }

    public void ExtendDocumentDerivation(string derivationCode, Action<DocumentDerivationDefinitionBuilder> configure)
    {
        var code = NormalizeDerivationCode(derivationCode);

        if (configure is null)
            throw new NgbArgumentRequiredException(nameof(configure));

        if (!_documentDerivations.TryGetValue(code, out var mutable))
            throw new NgbConfigurationViolationException(
                $"Cannot extend document derivation '{code}' because it is not registered. Use AddDocumentDerivation(...) first.",
                context: new Dictionary<string, object?>
                {
                    ["definitionKind"] = "document_derivation",
                    ["code"] = code
                });

        var builder = new DocumentDerivationDefinitionBuilder(mutable);
        configure(builder);

        mutable.ValidateRequired();
    }

    public DefinitionsRegistry Build()
    {
        var documents = _documents.Values
            .Select(m => m.Build())
            .ToDictionary(x => x.TypeCode, StringComparer.OrdinalIgnoreCase);

        var catalogs = _catalogs.Values
            .Select(m => m.Build())
            .ToDictionary(x => x.TypeCode, StringComparer.OrdinalIgnoreCase);

        var relationshipTypes = _documentRelationshipTypes.Values
            .Select(m => m.Build())
            .ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

        var derivations = _documentDerivations.Values
            .Select(m => m.Build())
            .ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

        return new DefinitionsRegistry(documents, catalogs, relationshipTypes, derivations);
    }

    private static string NormalizeRelationshipCode(string relationshipCode)
    {
        if (string.IsNullOrWhiteSpace(relationshipCode))
            throw new NgbArgumentInvalidException(nameof(relationshipCode), "Relationship code must be non-empty.");

        var code = relationshipCode.Trim();
        if (code.Length > MaxRelationshipCodeLength)
            throw new NgbArgumentInvalidException(nameof(relationshipCode), $"Relationship code exceeds max length {MaxRelationshipCodeLength}.");

        return code;
    }

    private static string NormalizeDerivationCode(string derivationCode)
    {
        if (string.IsNullOrWhiteSpace(derivationCode))
            throw new NgbArgumentInvalidException(nameof(derivationCode), "Derivation code must be non-empty.");

        var code = derivationCode.Trim();
        if (code.Length > MaxDerivationCodeLength)
            throw new NgbArgumentInvalidException(nameof(derivationCode), $"Derivation code exceeds max length {MaxDerivationCodeLength}.");

        return code;
    }

    internal sealed class MutableDocumentTypeDefinition(string typeCode)
    {
        public string TypeCode { get; } = typeCode;
        public DocumentTypeMetadata? Metadata { get; set; }

        public Type? TypedStorageType { get; set; }
        public Type? PostingHandlerType { get; set; }
        public Type? OperationalRegisterPostingHandlerType { get; set; }
        public Type? ReferenceRegisterPostingHandlerType { get; set; }
        public Type? NumberingPolicyType { get; set; }
        public Type? ApprovalPolicyType { get; set; }

        public List<Type> DraftValidatorTypes { get; } = new();
        public List<Type> PostValidatorTypes { get; } = new();
        public HashSet<Type> DraftValidatorSet { get; } = new();
        public HashSet<Type> PostValidatorSet { get; } = new();

        public void ValidateRequired()
        {
            if (Metadata is null)
                throw new NgbConfigurationViolationException(
                    $"Document type '{TypeCode}' must define metadata.",
                    context: new Dictionary<string, object?>
                    {
                        ["definitionKind"] = "document",
                        ["typeCode"] = TypeCode,
                        ["missing"] = "metadata"
                    });
        }

        public DocumentTypeDefinition Build() => new(
            TypeCode,
            Metadata!,
            typedStorageType: TypedStorageType,
            postingHandlerType: PostingHandlerType,
            operationalRegisterPostingHandlerType: OperationalRegisterPostingHandlerType,
            referenceRegisterPostingHandlerType: ReferenceRegisterPostingHandlerType,
            numberingPolicyType: NumberingPolicyType,
            approvalPolicyType: ApprovalPolicyType,
            draftValidatorTypes: DraftValidatorTypes.ToArray(),
            postValidatorTypes: PostValidatorTypes.ToArray());
    }

    internal sealed class MutableCatalogTypeDefinition(string typeCode)
    {
        public string TypeCode { get; } = typeCode;
        public CatalogTypeMetadata? Metadata { get; set; }

        public Type? TypedStorageType { get; set; }
        public List<Type> ValidatorTypes { get; } = [];
        public HashSet<Type> ValidatorSet { get; } = [];

        public void ValidateRequired()
        {
            if (Metadata is null)
                throw new NgbConfigurationViolationException(
                    $"Catalog type '{TypeCode}' must define metadata.",
                    context: new Dictionary<string, object?>
                    {
                        ["definitionKind"] = "catalog",
                        ["typeCode"] = TypeCode,
                        ["missing"] = "metadata"
                    });
        }

        public CatalogTypeDefinition Build() => new(
            TypeCode,
            Metadata!,
            typedStorageType: TypedStorageType,
            validatorTypes: ValidatorTypes.ToArray());
    }

    internal sealed class MutableDocumentRelationshipTypeDefinition(string relationshipCode)
    {
        public string Code { get; } = relationshipCode;
        public string? Name { get; set; }
        public bool IsBidirectional { get; set; }
        public DocumentRelationshipCardinality? Cardinality { get; set; }

        public HashSet<string> AllowedFromTypeCodes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> AllowedToTypeCodes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void ValidateRequired()
        {
            if (string.IsNullOrWhiteSpace(Name))
                throw new NgbConfigurationViolationException(
                    $"Document relationship type '{Code}' must define a non-empty Name.",
                    context: new Dictionary<string, object?>
                    {
                        ["definitionKind"] = "document_relationship",
                        ["code"] = Code,
                        ["missing"] = "name"
                    });

            if (Cardinality is null)
                throw new NgbConfigurationViolationException(
                    $"Document relationship type '{Code}' must define Cardinality.",
                    context: new Dictionary<string, object?>
                    {
                        ["definitionKind"] = "document_relationship",
                        ["code"] = Code,
                        ["missing"] = "cardinality"
                    });
        }

        public DocumentRelationshipTypeDefinition Build() => new(
            Code: Code,
            Name: Name!,
            IsBidirectional: IsBidirectional,
            Cardinality: Cardinality!.Value,
            AllowedFromTypeCodes: AllowedFromTypeCodes.Count == 0 ? null : AllowedFromTypeCodes.ToArray(),
            AllowedToTypeCodes: AllowedToTypeCodes.Count == 0 ? null : AllowedToTypeCodes.ToArray());
    }

    internal sealed class MutableDocumentDerivationDefinition(string derivationCode)
    {
        public string Code { get; } = derivationCode;
        public string? Name { get; set; }
        public string? FromTypeCode { get; set; }
        public string? ToTypeCode { get; set; }
        public Type? HandlerType { get; set; }

        public List<string> RelationshipCodes { get; } = [];
        public HashSet<string> RelationshipCodeSet { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void AddRelationship(string relationshipCode)
        {
            if (string.IsNullOrWhiteSpace(relationshipCode))
                throw new NgbArgumentInvalidException(nameof(relationshipCode), "Relationship code must be non-empty.");

            var code = relationshipCode.Trim();
            if (code.Length > MaxRelationshipCodeLength)
                throw new NgbArgumentInvalidException(nameof(relationshipCode), $"Relationship code exceeds max length {MaxRelationshipCodeLength}.");

            if (!RelationshipCodeSet.Add(code))
                return;

            RelationshipCodes.Add(code);
        }

        public void ValidateRequired()
        {
            if (string.IsNullOrWhiteSpace(Name))
                throw new NgbConfigurationViolationException(
                    $"Document derivation '{Code}' must define a non-empty Name.",
                    context: new Dictionary<string, object?>
                    {
                        ["definitionKind"] = "document_derivation",
                        ["code"] = Code,
                        ["missing"] = "name"
                    });

            if (string.IsNullOrWhiteSpace(FromTypeCode))
                throw new NgbConfigurationViolationException(
                    $"Document derivation '{Code}' must define a non-empty FromTypeCode.",
                    context: new Dictionary<string, object?>
                    {
                        ["definitionKind"] = "document_derivation",
                        ["code"] = Code,
                        ["missing"] = "fromTypeCode"
                    });

            if (string.IsNullOrWhiteSpace(ToTypeCode))
                throw new NgbConfigurationViolationException(
                    $"Document derivation '{Code}' must define a non-empty ToTypeCode.",
                    context: new Dictionary<string, object?>
                    {
                        ["definitionKind"] = "document_derivation",
                        ["code"] = Code,
                        ["missing"] = "toTypeCode"
                    });

            if (RelationshipCodes.Count == 0)
                throw new NgbConfigurationViolationException(
                    $"Document derivation '{Code}' must define at least one relationship code.",
                    context: new Dictionary<string, object?>
                    {
                        ["definitionKind"] = "document_derivation",
                        ["code"] = Code,
                        ["missing"] = "relationshipCodes"
                    });
        }

        public DocumentDerivationDefinition Build() => new(
            Code: Code,
            Name: Name!,
            FromTypeCode: FromTypeCode!.Trim(),
            ToTypeCode: ToTypeCode!.Trim(),
            RelationshipCodes: RelationshipCodes.ToArray(),
            HandlerType: HandlerType);
    }
}
