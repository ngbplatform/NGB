using Dapper;
using NGB.Core.Dimensions;
using NGB.Persistence.Dimensions;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Dimensions;

public sealed class PostgresDimensionSetReader(IUnitOfWork uow) : IDimensionSetReader
{
    private sealed class Row
    {
        public Guid DimensionSetId { get; init; }
        public Guid DimensionId { get; init; }
        public Guid ValueId { get; init; }
    }

    public async Task<IReadOnlyDictionary<Guid, DimensionBag>> GetBagsByIdsAsync(
        IReadOnlyCollection<Guid> dimensionSetIds,
        CancellationToken ct = default)
    {
        if (dimensionSetIds is null)
            throw new NgbArgumentRequiredException(nameof(dimensionSetIds));

        if (dimensionSetIds.Count == 0)
            return new Dictionary<Guid, DimensionBag>();

        // Always treat Empty as a special canonical bag.
        var includeEmpty = dimensionSetIds.Contains(Guid.Empty);

        var ids = dimensionSetIds
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToArray();

        var result = new Dictionary<Guid, DimensionBag>(capacity: ids.Length + (includeEmpty ? 1 : 0));

        if (includeEmpty)
            result[Guid.Empty] = DimensionBag.Empty;

        if (ids.Length == 0)
            return result;

        const string sql = """
                           SELECT
                               dimension_set_id AS DimensionSetId,
                               dimension_id AS DimensionId,
                               value_id AS ValueId
                           FROM platform_dimension_set_items
                           WHERE dimension_set_id = ANY(@Ids)
                           ORDER BY dimension_set_id, dimension_id;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { Ids = ids },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.EnsureConnectionOpenAsync(ct);

        var rows = (await uow.Connection.QueryAsync<Row>(cmd)).AsList();

        // Group and build canonical bags.
        foreach (var g in rows.GroupBy(x => x.DimensionSetId))
        {
            var items = g.Select(x => new DimensionValue(x.DimensionId, x.ValueId));
            result[g.Key] = new DimensionBag(items);
        }

        // Be defensive: if a requested set has no items, still return an empty bag.
        foreach (var id in ids)
        {
            if (!result.ContainsKey(id))
                result[id] = DimensionBag.Empty;
        }

        return result;
    }
}
