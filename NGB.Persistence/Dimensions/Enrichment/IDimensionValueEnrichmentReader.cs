using NGB.Core.Dimensions.Enrichment;

namespace NGB.Persistence.Dimensions.Enrichment;

/// <summary>
/// Resolves dimension value ids to display strings for reports/enrichment.
/// Implementations should be optimized for batch requests.
/// </summary>
public interface IDimensionValueEnrichmentReader
{
    Task<IReadOnlyDictionary<DimensionValueKey, string>> ResolveAsync(
        IReadOnlyCollection<DimensionValueKey> keys,
        CancellationToken ct = default);
}
