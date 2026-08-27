using Dapper;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Internal;
using NGB.PostgreSql.Readers;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.OperationalRegisters;

/// <summary>
/// Shared core for reading monthly projection tables (opreg_*__balances / opreg_*__turnovers).
/// Keeps the readers thin and consistent.
/// </summary>
internal static class PostgresOperationalRegisterMonthlyProjectionReaderCore
{
    public static Task<IReadOnlyList<OperationalRegisterMonthlyProjectionReadRow>> GetByMonthsAsync(
        IUnitOfWork uow,
        Guid registerId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<DimensionValue>? effectiveDimensions,
        Guid? dimensionSetId,
        Func<Guid, CancellationToken, Task<(string TableName, IReadOnlyList<string> ResourceColumns)>> resolveTableAndResourcesOrThrowAsync,
        IDimensionSetReader dimensionSetReader,
        IDimensionValueEnrichmentReader dimensionValueEnrichmentReader,
        CancellationToken ct)
        => ReadByMonthsAsync(
            uow,
            registerId,
            fromInclusive,
            toInclusive,
            effectiveDimensions,
            dimensionSetId,
            afterPeriodMonth: null,
            afterDimensionSetId: null,
            limit: null,
            resolveTableAndResourcesOrThrowAsync,
            dimensionSetReader,
            dimensionValueEnrichmentReader,
            ct);

    public static Task<IReadOnlyList<OperationalRegisterMonthlyProjectionReadRow>> GetPageByMonthsAsync(
        IUnitOfWork uow,
        Guid registerId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<DimensionValue>? effectiveDimensions,
        Guid? dimensionSetId,
        DateOnly? afterPeriodMonth,
        Guid? afterDimensionSetId,
        int limit,
        Func<Guid, CancellationToken, Task<(string TableName, IReadOnlyList<string> ResourceColumns)>> resolveTableAndResourcesOrThrowAsync,
        IDimensionSetReader dimensionSetReader,
        IDimensionValueEnrichmentReader dimensionValueEnrichmentReader,
        CancellationToken ct)
        => ReadByMonthsAsync(
            uow,
            registerId,
            fromInclusive,
            toInclusive,
            effectiveDimensions,
            dimensionSetId,
            afterPeriodMonth,
            afterDimensionSetId,
            limit,
            resolveTableAndResourcesOrThrowAsync,
            dimensionSetReader,
            dimensionValueEnrichmentReader,
            ct);

    private static async Task<IReadOnlyList<OperationalRegisterMonthlyProjectionReadRow>> ReadByMonthsAsync(
        IUnitOfWork uow,
        Guid registerId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<DimensionValue>? effectiveDimensions,
        Guid? dimensionSetId,
        DateOnly? afterPeriodMonth,
        Guid? afterDimensionSetId,
        int? limit,
        Func<Guid, CancellationToken, Task<(string TableName, IReadOnlyList<string> ResourceColumns)>> resolveTableAndResourcesOrThrowAsync,
        IDimensionSetReader dimensionSetReader,
        IDimensionValueEnrichmentReader dimensionValueEnrichmentReader,
        CancellationToken ct)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(registerId));

        if (toInclusive < fromInclusive)
            throw new NgbArgumentOutOfRangeException(nameof(toInclusive), toInclusive, "To must be on or after From.");

        if (limit is <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");

        fromInclusive.EnsureMonthStart(nameof(fromInclusive));
        toInclusive.EnsureMonthStart(nameof(toInclusive));
        if (afterPeriodMonth is { } cursorMonth)
            cursorMonth.EnsureMonthStart(nameof(afterPeriodMonth));

        await uow.EnsureConnectionOpenAsync(ct);

        var (tableName, resourceColumns) = await resolveTableAndResourcesOrThrowAsync(registerId, ct);
        if (!await PostgresTableExistence.ExistsAsync(uow, tableName, ct))
            return [];

        var (dimIds, dimValueIds, dimCount) = SqlDimensionFilter.Normalize(effectiveDimensions);

        var resourcesSelect = resourceColumns.Count == 0
            ? string.Empty
            : ", " + string.Join(", ", resourceColumns.Select(c => $"{c} AS \"{c}\""));

        var cursorSql = afterPeriodMonth is null
            ? string.Empty
            : """
              AND (
                  t.period_month > @AfterPeriodMonth::date
                  OR (t.period_month = @AfterPeriodMonth::date AND t.dimension_set_id > @AfterDimensionSetId)
              )
              """;

        var limitSql = limit is null
            ? string.Empty
            : "LIMIT @Limit";

        var dimensionFilterCte = dimCount == 0
            ? string.Empty
            : """
              WITH requested_dimensions AS (
                  SELECT *
                  FROM unnest(@DimIds::uuid[], @DimValueIds::uuid[])
                      AS requested(dimension_id, value_id)
              ),
              matching_dimension_sets AS (
                  SELECT item.dimension_set_id
                  FROM platform_dimension_set_items item
                  JOIN requested_dimensions requested
                    ON requested.dimension_id = item.dimension_id
                   AND requested.value_id = item.value_id
                  GROUP BY item.dimension_set_id
                  HAVING COUNT(*) = @DimCount::int
              )
              """;

        var dimensionFilterJoin = dimCount == 0
            ? string.Empty
            : "JOIN matching_dimension_sets matched ON matched.dimension_set_id = t.dimension_set_id";

        var sql = $"""
                  {dimensionFilterCte}
                  SELECT
                      t.period_month      AS "PeriodMonth",
                      t.dimension_set_id  AS "DimensionSetId"{resourcesSelect}
                  FROM {tableName} t
                  {dimensionFilterJoin}
                  WHERE
                      t.period_month >= @FromMonth::date
                      AND t.period_month <= @ToMonth::date
                      AND (@DimensionSetId IS NULL OR t.dimension_set_id = @DimensionSetId)
                      {cursorSql}
                  ORDER BY t.period_month, t.dimension_set_id
                  {limitSql};
                  """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                FromMonth = fromInclusive,
                ToMonth = toInclusive,
                DimensionSetId = dimensionSetId,
                DimCount = dimCount,
                DimIds = dimIds,
                DimValueIds = dimValueIds,
                AfterPeriodMonth = afterPeriodMonth,
                AfterDimensionSetId = afterDimensionSetId,
                Limit = limit
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.QueryAsync(cmd);

        var result = new List<OperationalRegisterMonthlyProjectionReadRow>();

        foreach (var row in rows)
        {
            var d = (IDictionary<string, object?>)row;

            var periodMonth = (DateOnly)d["PeriodMonth"]!;
            var dimSetId = (Guid)d["DimensionSetId"]!;

            var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var col in resourceColumns)
            {
                var v = d.TryGetValue(col, out var obj) ? obj : null;
                values[col] = v is null or DBNull ? 0m : Convert.ToDecimal(v);
            }

            result.Add(new OperationalRegisterMonthlyProjectionReadRow
            {
                PeriodMonth = periodMonth,
                DimensionSetId = dimSetId,
                Values = values
            });
        }

        await ResolveDimensionsAsync(dimensionSetReader, result, ct);
        await ResolveDimensionValueDisplaysAsync(dimensionValueEnrichmentReader, result, ct);

        return result;
    }

    private static async Task ResolveDimensionsAsync(
        IDimensionSetReader dimensionSetReader,
        IReadOnlyList<OperationalRegisterMonthlyProjectionReadRow> rows,
        CancellationToken ct)
    {
        if (rows.Count == 0)
            return;

        var ids = rows
            .Select(x => x.DimensionSetId)
            .Distinct()
            .ToArray();

        var bags = await dimensionSetReader.GetBagsByIdsAsync(ids, ct);

        foreach (var r in rows)
        {
            r.Dimensions = bags.TryGetValue(r.DimensionSetId, out var bag) ? bag : DimensionBag.Empty;
        }
    }

    private static async Task ResolveDimensionValueDisplaysAsync(
        IDimensionValueEnrichmentReader dimensionValueEnrichmentReader,
        IReadOnlyList<OperationalRegisterMonthlyProjectionReadRow> rows,
        CancellationToken ct)
    {
        if (rows.Count == 0)
            return;

        var keys = rows.Select(x => x.Dimensions).CollectValueKeys();
        if (keys.Count == 0)
            return;

        var resolved = await dimensionValueEnrichmentReader.ResolveAsync(keys, ct);

        foreach (var r in rows)
        {
            r.DimensionValueDisplays = r.Dimensions.ToValueDisplayMap(resolved);
        }
    }
}
