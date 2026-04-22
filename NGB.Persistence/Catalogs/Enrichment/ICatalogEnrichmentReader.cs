namespace NGB.Persistence.Catalogs.Enrichment;

/// <summary>
/// Resolves catalog ids to display strings for reports/enrichment.
/// Implementations should be optimized for batch requests.
/// </summary>
public interface ICatalogEnrichmentReader
{
    Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(
        string catalogCode,
        IReadOnlyList<Guid> ids,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, string>>> ResolveManyAsync(
        IReadOnlyDictionary<string, IReadOnlyCollection<Guid>> idsByCatalogCode,
        CancellationToken ct = default);
}
