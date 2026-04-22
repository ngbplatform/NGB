using NGB.Tools.Exceptions;

namespace NGB.Metadata.Base;

public abstract record LookupSourceMetadata;

public sealed record CatalogLookupSourceMetadata : LookupSourceMetadata
{
    public CatalogLookupSourceMetadata(string catalogType)
    {
        if (string.IsNullOrWhiteSpace(catalogType))
            throw new NgbArgumentRequiredException(nameof(catalogType));

        CatalogType = catalogType;
    }

    public string CatalogType { get; }
}

public sealed record DocumentLookupSourceMetadata : LookupSourceMetadata
{
    public DocumentLookupSourceMetadata(IReadOnlyList<string> documentTypes)
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

public sealed record ChartOfAccountsLookupSourceMetadata : LookupSourceMetadata;
