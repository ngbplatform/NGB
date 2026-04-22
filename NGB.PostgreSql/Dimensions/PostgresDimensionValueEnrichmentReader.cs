using Dapper;
using NGB.Core.Dimensions.Enrichment;
using NGB.Metadata.Catalogs.Storage;
using NGB.Persistence.Catalogs.Enrichment;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Dimensions;

/// <summary>
/// PostgreSQL implementation of dimension value enrichment.
///
/// Best-effort strategy:
/// 1) Resolve DimensionId -> dimension code via platform_dimensions.
/// 2) If that code is a registered catalog, resolve ValueId via ICatalogEnrichmentReader.
/// 3) Fallback unresolved ValueId through IDocumentDisplayReader.
/// 4) Final fallback: short GUID.
/// </summary>
public sealed class PostgresDimensionValueEnrichmentReader(
    IUnitOfWork uow,
    ICatalogTypeRegistry catalogTypeRegistry,
    ICatalogEnrichmentReader catalogEnrichmentReader,
    IDocumentDisplayReader documentDisplayReader)
    : IDimensionValueEnrichmentReader
{
    private sealed class DimensionRow
    {
        public Guid DimensionId { get; init; }
        public string CodeNorm { get; init; } = string.Empty;
    }

    public async Task<IReadOnlyDictionary<DimensionValueKey, string>> ResolveAsync(
        IReadOnlyCollection<DimensionValueKey> keys,
        CancellationToken ct = default)
    {
        if (keys is null)
            throw new NgbArgumentRequiredException(nameof(keys));

        if (keys.Count == 0)
            return new Dictionary<DimensionValueKey, string>();

        await uow.EnsureConnectionOpenAsync(ct);

        var dimIds = keys.Select(k => k.DimensionId).Distinct().ToArray();

        const string dimSql = """
                             SELECT
                                 dimension_id AS DimensionId,
                                 code_norm AS CodeNorm
                             FROM platform_dimensions
                             WHERE dimension_id = ANY(@Ids)
                               AND is_deleted = false;
                             """;

        var dimCmd = new CommandDefinition(dimSql, new { Ids = dimIds }, transaction: uow.Transaction, cancellationToken: ct);
        var dimRows = (await uow.Connection.QueryAsync<DimensionRow>(dimCmd)).AsList();

        var dimCodeById = dimRows
            .GroupBy(r => r.DimensionId)
            .ToDictionary(g => g.Key, g => g.First().CodeNorm);

        var result = new Dictionary<DimensionValueKey, string>(capacity: keys.Count);
        var unresolvedValueIds = new HashSet<Guid>();

        var groupedByCode = keys
            .Select(key => new { Key = key, Code = dimCodeById.GetValueOrDefault(key.DimensionId) })
            .Where(x => !string.IsNullOrWhiteSpace(x.Code))
            .GroupBy(x => x.Code!, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var catalogIdsByCode = groupedByCode
            .Where(group => catalogTypeRegistry.TryGet(group.Key, out _))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyCollection<Guid>)group.Select(x => x.Key.ValueId).Distinct().ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var catalogDisplaysByCode = await catalogEnrichmentReader.ResolveManyAsync(catalogIdsByCode, ct);

        foreach (var group in groupedByCode)
        {
            if (!catalogDisplaysByCode.TryGetValue(group.Key, out var displays))
            {
                foreach (var item in group)
                {
                    unresolvedValueIds.Add(item.Key.ValueId);
                }

                continue;
            }

            foreach (var item in group)
            {
                if (displays.TryGetValue(item.Key.ValueId, out var display))
                    result[item.Key] = display;
                else
                    unresolvedValueIds.Add(item.Key.ValueId);
            }
        }

        foreach (var key in keys)
        {
            if (!result.ContainsKey(key))
                unresolvedValueIds.Add(key.ValueId);
        }

        if (unresolvedValueIds.Count > 0)
        {
            var documentDisplays = await documentDisplayReader.ResolveAsync(unresolvedValueIds, ct);

            foreach (var key in keys)
            {
                if (result.ContainsKey(key))
                    continue;

                if (documentDisplays.TryGetValue(key.ValueId, out var display))
                    result[key] = display;
            }
        }

        foreach (var key in keys)
        {
            if (result.ContainsKey(key))
                continue;

            var s = key.ValueId.ToString("N");
            result[key] = s.Length > 8 ? s[..8] : s;
        }

        return result;
    }
}
