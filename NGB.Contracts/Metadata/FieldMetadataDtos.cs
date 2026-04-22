using System.Text.Json.Serialization;
using NGB.Tools.Exceptions;

namespace NGB.Contracts.Metadata;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(CatalogLookupSourceDto), typeDiscriminator: "catalog")]
[JsonDerivedType(typeof(DocumentLookupSourceDto), typeDiscriminator: "document")]
[JsonDerivedType(typeof(ChartOfAccountsLookupSourceDto), typeDiscriminator: "coa")]
public abstract record LookupSourceDto;

public sealed record CatalogLookupSourceDto : LookupSourceDto
{
    public CatalogLookupSourceDto(string catalogType, string? displayTemplate = null)
    {
        if (string.IsNullOrWhiteSpace(catalogType))
            throw new NgbArgumentRequiredException(nameof(catalogType));

        CatalogType = catalogType;
        DisplayTemplate = displayTemplate;
    }

    public string CatalogType { get; }
    public string? DisplayTemplate { get; }
}

public sealed record DocumentLookupSourceDto : LookupSourceDto
{
    public DocumentLookupSourceDto(IReadOnlyList<string> documentTypes)
    {
        if (documentTypes is null)
            throw new NgbArgumentRequiredException(nameof(documentTypes));

        var normalized = documentTypes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
            throw new NgbArgumentInvalidException(nameof(documentTypes), "At least one document type is required.");

        DocumentTypes = normalized;
    }

    public IReadOnlyList<string> DocumentTypes { get; }
}

public sealed record ChartOfAccountsLookupSourceDto : LookupSourceDto;

public sealed record FieldValidationDto(
    int? MaxLength = null,
    decimal? Min = null,
    decimal? Max = null,
    string? Regex = null);

public sealed record MirroredDocumentRelationshipDto(string RelationshipCode);

public sealed record FieldMetadataDto(
    string Key,
    string Label,
    DataType DataType,
    UiControl UiControl,
    bool IsRequired = false,
    bool IsReadOnly = false,
    IReadOnlyList<DocumentStatus>? ReadOnlyWhenStatusIn = null,
    LookupSourceDto? Lookup = null,
    FieldValidationDto? Validation = null,
    IReadOnlyList<MetadataOptionDto>? Options = null,
    string? HelpText = null,
    MirroredDocumentRelationshipDto? MirroredRelationship = null);
