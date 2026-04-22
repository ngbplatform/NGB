using System.Collections.Concurrent;
using Dapper;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Internal;
using NGB.PostgreSql.OperationalRegisters.Internal;
using NGB.PostgreSql.Readers;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.OperationalRegisters;

/// <summary>
/// UI/report oriented reader for per-register movements tables (opreg_*__movements).
/// Works both inside and outside a transaction; if the table has not been created yet, returns empty results.
///
/// Notes:
/// - The physical schema is defined by register resources (operational_register_resources).
/// - Returned rows can be enriched with DimensionBag and display values for UI/report rendering.
/// - Uses AND semantics for dimension filters (all requested DimensionValue pairs must exist in the movement's DimensionSetId).
/// </summary>
public sealed class PostgresOperationalRegisterMovementsQueryReader(
    IUnitOfWork uow,
    IOperationalRegisterRepository registers,
    IOperationalRegisterResourceRepository resources,
    IDimensionSetReader dimensionSetReader,
    IDimensionValueEnrichmentReader dimensionValueEnrichmentReader)
    : IOperationalRegisterMovementsQueryReader
{
    // IMPORTANT: identifiers are used unquoted in dynamic SQL; Postgres requires unquoted identifiers
    // to start with a letter or underscore.
    private readonly ConcurrentDictionary<Guid, RegisterQueryContext> _registerContexts = new();

    public Task<IReadOnlyList<OperationalRegisterMovementQueryReadRow>> GetByMonthsAsync(
        Guid registerId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<DimensionValue>? dimensions = null,
        Guid? dimensionSetId = null,
        Guid? documentId = null,
        bool? isStorno = null,
        long? afterMovementId = null,
        int limit = 1000,
        CancellationToken ct = default)
        => GetInternalAsync(registerId, fromInclusive, toInclusive, dimensions, dimensionSetId, documentId, isStorno, afterMovementId, limit, ct);

    public Task<DateOnly?> GetMaxPeriodMonthAsync(
        Guid registerId,
        IReadOnlyList<DimensionValue>? dimensions = null,
        Guid? dimensionSetId = null,
        Guid? documentId = null,
        bool? isStorno = null,
        CancellationToken ct = default)
        => GetMaxPeriodMonthInternalAsync(registerId, dimensions, dimensionSetId, documentId, isStorno, ct);

    private async Task<DateOnly?> GetMaxPeriodMonthInternalAsync(
        Guid registerId,
        IReadOnlyList<DimensionValue>? effectiveDimensions,
        Guid? dimensionSetId,
        Guid? documentId,
        bool? isStorno,
        CancellationToken ct)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(registerId));

        await uow.EnsureConnectionOpenAsync(ct);

        var context = await GetRegisterQueryContextAsync(registerId, ct);
        if (context is null)
            return null;

        var (dimIds, dimValueIds, dimCount) = SqlDimensionFilter.Normalize(effectiveDimensions);
        var dimensionFilterSql = BuildDimensionFilterSql("t", dimCount);

        var sql = $"""
                  {BuildDimensionFilterCte(dimCount)}
                  SELECT MAX(t.period_month) AS max_period_month
                  FROM {context.TableName} t
                  WHERE
                      (@DimensionSetId IS NULL OR t.dimension_set_id = @DimensionSetId)
                      AND (@DocumentId IS NULL OR t.document_id = @DocumentId)
                      AND (@IsStorno IS NULL OR t.is_storno = @IsStorno)
                      {dimensionFilterSql};
                  """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                DimensionSetId = dimensionSetId,
                DocumentId = documentId,
                IsStorno = isStorno,
                DimCount = dimCount,
                DimIds = dimIds,
                DimValueIds = dimValueIds
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var scalar = await uow.Connection.ExecuteScalarAsync(cmd);

        if (scalar is null or DBNull)
            return null;

        return scalar switch
        {
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
			_ => throw new NgbUnexpectedException(
				operation: "opreg.movements.get_max_period_month",
				innerException: new InvalidOperationException($"Unexpected scalar type for MAX(period_month): {scalar.GetType().FullName}."),
				additionalContext: new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["scalarType"] = scalar.GetType().FullName
				})
        };
    }

    private async Task<IReadOnlyList<OperationalRegisterMovementQueryReadRow>> GetInternalAsync(
        Guid registerId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<DimensionValue>? effectiveDimensions,
        Guid? dimensionSetId,
        Guid? documentId,
        bool? isStorno,
        long? afterMovementId,
        int limit,
        CancellationToken ct)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(registerId));

        if (limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be greater than 0.");

        if (toInclusive < fromInclusive)
            throw new NgbArgumentOutOfRangeException(nameof(toInclusive), toInclusive, "To must be on or after From.");

        fromInclusive.EnsureMonthStart(nameof(fromInclusive));
        toInclusive.EnsureMonthStart(nameof(toInclusive));

        await uow.EnsureConnectionOpenAsync(ct);

        var context = await GetRegisterQueryContextAsync(registerId, ct);
        if (context is null)
            return [];

        var (dimIds, dimValueIds, dimCount) = SqlDimensionFilter.Normalize(effectiveDimensions);
        var dimensionFilterSql = BuildDimensionFilterSql("t", dimCount);

        var resourcesSelect = context.ResourceColumns.Count == 0
            ? string.Empty
            : ", " + string.Join(", ", context.ResourceColumns.Select(c => $"{c} AS \"{c}\""));

        var sql = $"""
                  {BuildDimensionFilterCte(dimCount)}
                  SELECT
                      movement_id       AS "MovementId",
                      document_id       AS "DocumentId",
                      occurred_at_utc   AS "OccurredAtUtc",
                      period_month      AS "PeriodMonth",
                      dimension_set_id  AS "DimensionSetId",
                      is_storno         AS "IsStorno"{resourcesSelect}
                  FROM {context.TableName} t
                  WHERE
                      t.period_month >= @FromMonth::date
                      AND t.period_month <= @ToMonth::date
                      AND (@DimensionSetId IS NULL OR t.dimension_set_id = @DimensionSetId)
                      AND (@DocumentId IS NULL OR t.document_id = @DocumentId)
                      AND (@IsStorno IS NULL OR t.is_storno = @IsStorno)
                      AND (@AfterMovementId IS NULL OR t.movement_id > @AfterMovementId)
                      {dimensionFilterSql}
                  ORDER BY t.movement_id
                  LIMIT @Limit;
                  """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                FromMonth = fromInclusive,
                ToMonth = toInclusive,
                DimensionSetId = dimensionSetId,
                DocumentId = documentId,
                IsStorno = isStorno,
                AfterMovementId = afterMovementId,
                Limit = limit,
                DimCount = dimCount,
                DimIds = dimIds,
                DimValueIds = dimValueIds
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.QueryAsync(cmd);

        var result = new List<OperationalRegisterMovementQueryReadRow>();

        foreach (var row in rows)
        {
            var d = (IDictionary<string, object?>)row;

            var movementId = Convert.ToInt64(d["MovementId"]!);
            var docId = (Guid)d["DocumentId"]!;
            var occurredAtUtc = (DateTime)d["OccurredAtUtc"]!;
            var periodMonth = (DateOnly)d["PeriodMonth"]!;
            var dimSetId = (Guid)d["DimensionSetId"]!;
            var storno = (bool)d["IsStorno"]!;

            var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var col in context.ResourceColumns)
            {
                var v = d.TryGetValue(col, out var obj) ? obj : null;
                values[col] = (v is null || v is DBNull) ? 0m : Convert.ToDecimal(v);
            }

            result.Add(new OperationalRegisterMovementQueryReadRow
            {
                MovementId = movementId,
                DocumentId = docId,
                OccurredAtUtc = occurredAtUtc,
                PeriodMonth = periodMonth,
                DimensionSetId = dimSetId,
                IsStorno = storno,
                Values = values
            });
        }

        await ResolveDimensionsAsync(result, ct);
        await ResolveDimensionValueDisplaysAsync(result, ct);

        return result;
    }

    private async Task<RegisterQueryContext?> GetRegisterQueryContextAsync(Guid registerId, CancellationToken ct)
    {
        if (_registerContexts.TryGetValue(registerId, out var cached))
            return cached;

        var (tableName, resourceColumns) = await OperationalRegisterMovementsTableResolver.ResolveOrThrowAsync(
            registers,
            resources,
            registerId,
            ct);

        if (!await PostgresTableExistence.ExistsAsync(uow, tableName, ct))
            return null;

        var context = new RegisterQueryContext(tableName, resourceColumns);
        _registerContexts[registerId] = context;
        return context;
    }

    private static string BuildDimensionFilterCte(int dimCount)
        => dimCount == 0
            ? string.Empty
            : """
              WITH matching_dimension_sets AS (
                  SELECT di.dimension_set_id
                  FROM platform_dimension_set_items di
                  JOIN (
                      SELECT req.dimension_id, req.value_id
                      FROM UNNEST(@DimIds::uuid[], @DimValueIds::uuid[]) AS req(dimension_id, value_id)
                  ) req ON req.dimension_id = di.dimension_id AND req.value_id = di.value_id
                  GROUP BY di.dimension_set_id
                  HAVING COUNT(*) = @DimCount::int
              )
              """;

    private static string BuildDimensionFilterSql(string tableAlias, int dimCount)
        => dimCount == 0
            ? string.Empty
            : $"AND {tableAlias}.dimension_set_id IN (SELECT dimension_set_id FROM matching_dimension_sets)";

    private async Task ResolveDimensionsAsync(
        IReadOnlyList<OperationalRegisterMovementQueryReadRow> rows,
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

    private async Task ResolveDimensionValueDisplaysAsync(
        IReadOnlyList<OperationalRegisterMovementQueryReadRow> rows,
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

    private sealed record RegisterQueryContext(string TableName, IReadOnlyList<string> ResourceColumns);
}
