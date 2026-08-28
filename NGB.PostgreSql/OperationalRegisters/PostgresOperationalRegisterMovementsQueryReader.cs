using System.Collections.Concurrent;
using Dapper;
using NGB.Contracts.Common;
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
    private readonly ConcurrentDictionary<string, TableReadiness> _existingTables = new(StringComparer.Ordinal);

    public async Task<OperationalRegisterMovementQueryPage> GetByOccurredAtPageAsync(
        Guid registerId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<DimensionValue>? dimensions = null,
        int offset = 0,
        int? limit = 100,
        CancellationToken ct = default)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(registerId));

        if (toInclusive < fromInclusive)
            throw new NgbArgumentOutOfRangeException(nameof(toInclusive), toInclusive, "To must be on or after From.");

        if (offset < 0)
            throw new NgbArgumentOutOfRangeException(nameof(offset), offset, "Offset must be zero or greater.");

        if (limit is <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be greater than zero when specified.");

        await uow.EnsureConnectionOpenAsync(ct);

        var context = await GetRegisterQueryContextAsync(registerId, ct);
        if (context is null)
            return new OperationalRegisterMovementQueryPage([], 0);

        var (dimIds, dimValueIds, dimCount) = SqlDimensionFilter.Normalize(dimensions);
        var dimensionFilterSql = BuildDimensionFilterSql("t", dimCount);
        var resourcesSelect = context.ResourceColumns.Count == 0
            ? string.Empty
            : ", " + string.Join(", ", context.ResourceColumns.Select(c => $"{c} AS \"{c}\""));
        var occurredFromUtc = DateTime.SpecifyKind(fromInclusive.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var occurredToExclusiveUtc = toInclusive == DateOnly.MaxValue
            ? DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc)
            : DateTime.SpecifyKind(toInclusive.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var filterSql = $"""
t.period_month >= @FromMonth::date
AND t.period_month <= @ToMonth::date
AND t.occurred_at_utc >= @OccurredFromUtc
AND t.occurred_at_utc < @OccurredToExclusiveUtc
{dimensionFilterSql}
""";
        var cte = BuildDimensionFilterCte(dimCount);
        var sql = $"""
{cte}
SELECT COUNT(*)
FROM {context.TableName} t
WHERE {filterSql};

{cte}
SELECT
    movement_id AS "MovementId",
    document_id AS "DocumentId",
    occurred_at_utc AS "OccurredAtUtc",
    period_month AS "PeriodMonth",
    dimension_set_id AS "DimensionSetId",
    is_storno AS "IsStorno"{resourcesSelect}
FROM {context.TableName} t
WHERE {filterSql}
ORDER BY t.occurred_at_utc, t.movement_id
OFFSET @Offset
LIMIT @Limit;
""";

        var command = new CommandDefinition(
            sql,
            new
            {
                FromMonth = new DateOnly(fromInclusive.Year, fromInclusive.Month, 1),
                ToMonth = new DateOnly(toInclusive.Year, toInclusive.Month, 1),
                OccurredFromUtc = occurredFromUtc,
                OccurredToExclusiveUtc = occurredToExclusiveUtc,
                Offset = PagingLimits.BoundOffset(offset),
                Limit = limit,
                DimCount = dimCount,
                DimIds = dimIds,
                DimValueIds = dimValueIds
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await using var grid = await uow.Connection.QueryMultipleAsync(command);
        var total = await grid.ReadSingleAsync<long>();
        var rows = await grid.ReadAsync();
        var result = MaterializeRows(rows, context);
        await ResolveDimensionsAsync(result, ct);
        await ResolveDimensionValueDisplaysAsync(result, ct);
        return new OperationalRegisterMovementQueryPage(result, total);
    }

    public async Task<IReadOnlyList<OperationalRegisterDimensionResourceNetRow>> GetResourceNetsByDimensionAsync(
        Guid registerId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<DimensionValue>? dimensions,
        Guid groupDimensionId,
        string resourceColumnCode,
        CancellationToken ct = default)
    {
        var page = await GetResourceNetsByDimensionPageAsync(
            registerId,
            fromInclusive,
            toInclusive,
            dimensions,
            groupDimensionId,
            resourceColumnCode,
            offset: 0,
            limit: PagingLimits.MaxMaterializedRows + 1,
            ct);

        EnsureLegacyMaterializationBound(page.Total);

        return page.Rows;
    }

    public async Task<OperationalRegisterDimensionResourceNetPage> GetResourceNetsByDimensionPageAsync(
        Guid registerId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<DimensionValue>? dimensions,
        Guid groupDimensionId,
        string resourceColumnCode,
        int offset,
        int limit,
        CancellationToken ct = default)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(registerId));

        if (groupDimensionId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(groupDimensionId));

        if (string.IsNullOrWhiteSpace(resourceColumnCode))
            throw new NgbArgumentRequiredException(nameof(resourceColumnCode));

        if (toInclusive < fromInclusive)
            throw new NgbArgumentOutOfRangeException(nameof(toInclusive), toInclusive, "To must be on or after From.");

        if (offset < 0)
            throw new NgbArgumentOutOfRangeException(nameof(offset), offset, "Offset must be zero or greater.");

        if (limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be greater than zero.");

        fromInclusive.EnsureMonthStart(nameof(fromInclusive));
        toInclusive.EnsureMonthStart(nameof(toInclusive));

        await uow.EnsureConnectionOpenAsync(ct);

        var context = await GetRegisterQueryContextAsync(registerId, ct);
        if (context is null)
            return new OperationalRegisterDimensionResourceNetPage([], 0, 0m, 0m);

        if (!context.ResourceColumns.Contains(resourceColumnCode, StringComparer.Ordinal))
        {
            throw new NgbConfigurationViolationException(
                $"Operational register '{registerId}' does not define resource column '{resourceColumnCode}'.");
        }

        var (dimIds, dimValueIds, dimCount) = SqlDimensionFilter.Normalize(dimensions);
        var dimensionFilterSql = BuildDimensionFilterSql("movement", dimCount);
        var dimensionCte = BuildDimensionFilterCte(dimCount);
        var withClause = string.IsNullOrWhiteSpace(dimensionCte)
            ? "WITH"
            : $"{dimensionCte.TrimEnd()},";
        var sql = $"""
{withClause}
nets AS (
SELECT
    grouped.value_id AS ValueId,
    SUM(CASE WHEN movement.is_storno
        THEN -movement.{resourceColumnCode}
        ELSE movement.{resourceColumnCode}
    END) AS NetAmount
FROM {context.TableName} movement
JOIN platform_dimension_set_items grouped
  ON grouped.dimension_set_id = movement.dimension_set_id
 AND grouped.dimension_id = @GroupDimensionId
WHERE movement.period_month >= @FromMonth::date
  AND movement.period_month <= @ToMonth::date
  {dimensionFilterSql}
GROUP BY grouped.value_id
HAVING SUM(CASE WHEN movement.is_storno
    THEN -movement.{resourceColumnCode}
    ELSE movement.{resourceColumnCode}
END) <> 0
)
SELECT
    ValueId,
    NetAmount,
    COUNT(*) OVER()::integer AS TotalCount,
    COALESCE(SUM(CASE WHEN NetAmount > 0 THEN NetAmount ELSE 0 END) OVER(), 0) AS TotalPositive,
    COALESCE(SUM(CASE WHEN NetAmount < 0 THEN -NetAmount ELSE 0 END) OVER(), 0) AS TotalNegativeAbsolute
FROM nets
ORDER BY CASE WHEN NetAmount > 0 THEN 0 ELSE 1 END, ValueId
OFFSET @Offset
LIMIT @Limit;
""";

        var rows = (await uow.Connection.QueryAsync<GroupNetSqlRow>(new CommandDefinition(
            sql,
            new
            {
                FromMonth = fromInclusive,
                ToMonth = toInclusive,
                GroupDimensionId = groupDimensionId,
                DimCount = dimCount,
                DimIds = dimIds,
                DimValueIds = dimValueIds,
                Offset = PagingLimits.BoundOffset(offset),
                Limit = limit
            },
            transaction: uow.Transaction,
            cancellationToken: ct))).AsList();

        if (rows.Count == 0)
            return new OperationalRegisterDimensionResourceNetPage([], 0, 0m, 0m);

        var keys = rows.Select(row => new DimensionValueKey(groupDimensionId, row.ValueId)).ToArray();
        var displays = await dimensionValueEnrichmentReader.ResolveAsync(keys, ct);

        return new OperationalRegisterDimensionResourceNetPage(
            rows.Select(row => new OperationalRegisterDimensionResourceNetRow(
                row.ValueId,
                row.NetAmount,
                displays.GetValueOrDefault(new DimensionValueKey(groupDimensionId, row.ValueId)))).ToArray(),
            rows[0].TotalCount,
            rows[0].TotalPositive,
            rows[0].TotalNegativeAbsolute);
    }

    public async Task<IReadOnlyList<OperationalRegisterDimensionResourceNetRow>> GetResourceBalancesByDimensionAsync(
        Guid registerId,
        DateOnly asOfMonthInclusive,
        IReadOnlyList<DimensionValue>? dimensions,
        Guid groupDimensionId,
        string resourceColumnCode,
        CancellationToken ct = default)
    {
        var page = await GetResourceBalancesByDimensionPageAsync(
            registerId,
            asOfMonthInclusive,
            dimensions,
            groupDimensionId,
            resourceColumnCode,
            offset: 0,
            limit: PagingLimits.MaxMaterializedRows + 1,
            ct);

        EnsureLegacyMaterializationBound(page.Total);

        return page.Rows;
    }

    private static void EnsureLegacyMaterializationBound(int total)
    {
        if (total <= PagingLimits.MaxMaterializedRows)
            return;

        throw new NgbArgumentOutOfRangeException(
            "resultCount",
            total,
            $"The unpaged operational-register result exceeds {PagingLimits.MaxMaterializedRows:N0} rows. Use the paged API.");
    }

    public async Task<OperationalRegisterDimensionResourceNetPage> GetResourceBalancesByDimensionPageAsync(
        Guid registerId,
        DateOnly asOfMonthInclusive,
        IReadOnlyList<DimensionValue>? dimensions,
        Guid groupDimensionId,
        string resourceColumnCode,
        int offset,
        int limit,
        CancellationToken ct = default)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(registerId));

        if (groupDimensionId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(groupDimensionId));

        if (string.IsNullOrWhiteSpace(resourceColumnCode))
            throw new NgbArgumentRequiredException(nameof(resourceColumnCode));

        if (offset < 0)
            throw new NgbArgumentOutOfRangeException(nameof(offset), offset, "Offset must be zero or greater.");

        if (limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be greater than zero.");

        asOfMonthInclusive.EnsureMonthStart(nameof(asOfMonthInclusive));
        await uow.EnsureConnectionOpenAsync(ct);

        var context = await GetRegisterQueryContextAsync(registerId, ct);
        if (context is null)
            return new OperationalRegisterDimensionResourceNetPage([], 0, 0m, 0m);

        if (!context.ResourceColumns.Contains(resourceColumnCode, StringComparer.Ordinal))
            throw new NgbConfigurationViolationException($"Operational register '{registerId}' does not define resource column '{resourceColumnCode}'.");

        var balancesTable = ResolveBalancesTableName(context.TableName);
        var balancesExist = await TableExistsAsync(balancesTable, ct);
        var (dimIds, dimValueIds, dimCount) = SqlDimensionFilter.Normalize(dimensions);
        var dimensionCte = BuildDimensionFilterCte(dimCount);
        var withClause = string.IsNullOrWhiteSpace(dimensionCte)
            ? "WITH"
            : $"{dimensionCte.TrimEnd()},";
        var sourceSql = balancesExist
            ? BuildSnapshotBackedBalanceSourceSql(context.TableName, balancesTable, resourceColumnCode, dimCount)
            : BuildMovementOnlyBalanceSourceSql(context.TableName, resourceColumnCode, dimCount);
        var sql = $"""
{withClause}
{sourceSql}
SELECT
    ValueId,
    NetAmount,
    COUNT(*) OVER()::integer AS TotalCount,
    COALESCE(SUM(CASE WHEN NetAmount > 0 THEN NetAmount ELSE 0 END) OVER(), 0) AS TotalPositive,
    COALESCE(SUM(CASE WHEN NetAmount < 0 THEN -NetAmount ELSE 0 END) OVER(), 0) AS TotalNegativeAbsolute
FROM nets
WHERE NetAmount <> 0
ORDER BY CASE WHEN NetAmount > 0 THEN 0 ELSE 1 END, ValueId
OFFSET @Offset
LIMIT @Limit;
""";

        var rows = (await uow.Connection.QueryAsync<GroupNetSqlRow>(new CommandDefinition(
            sql,
            new
            {
                AsOfMonth = asOfMonthInclusive,
                GroupDimensionId = groupDimensionId,
                DimCount = dimCount,
                DimIds = dimIds,
                DimValueIds = dimValueIds,
                Offset = PagingLimits.BoundOffset(offset),
                Limit = limit
            },
            transaction: uow.Transaction,
            cancellationToken: ct))).AsList();

        if (rows.Count == 0)
            return new OperationalRegisterDimensionResourceNetPage([], 0, 0m, 0m);

        var keys = rows.Select(row => new DimensionValueKey(groupDimensionId, row.ValueId)).ToArray();
        var displays = await dimensionValueEnrichmentReader.ResolveAsync(keys, ct);

        return new OperationalRegisterDimensionResourceNetPage(
            rows.Select(row => new OperationalRegisterDimensionResourceNetRow(
                row.ValueId,
                row.NetAmount,
                displays.GetValueOrDefault(new DimensionValueKey(groupDimensionId, row.ValueId)))).ToArray(),
            rows[0].TotalCount,
            rows[0].TotalPositive,
            rows[0].TotalNegativeAbsolute);
    }

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

        return ConvertMaxPeriodMonthScalar(scalar);
    }

    internal static DateOnly? ConvertMaxPeriodMonthScalar(object? scalar)
    {
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

    private static List<OperationalRegisterMovementQueryReadRow> MaterializeRows(
        IEnumerable<dynamic> rows,
        RegisterQueryContext context)
    {
        var result = new List<OperationalRegisterMovementQueryReadRow>();
        foreach (var row in rows)
        {
            var valuesByColumn = (IDictionary<string, object?>)row;
            var values = new Dictionary<string, decimal>(StringComparer.Ordinal);

            foreach (var column in context.ResourceColumns)
            {
                var value = valuesByColumn.TryGetValue(column, out var raw) ? raw : null;
                values[column] = value is null or DBNull ? 0m : Convert.ToDecimal(value);
            }

            result.Add(new OperationalRegisterMovementQueryReadRow
            {
                MovementId = Convert.ToInt64(valuesByColumn["MovementId"]!),
                DocumentId = (Guid)valuesByColumn["DocumentId"]!,
                OccurredAtUtc = (DateTime)valuesByColumn["OccurredAtUtc"]!,
                PeriodMonth = (DateOnly)valuesByColumn["PeriodMonth"]!,
                DimensionSetId = (Guid)valuesByColumn["DimensionSetId"]!,
                IsStorno = (bool)valuesByColumn["IsStorno"]!,
                Values = values
            });
        }

        return result;
    }

    private async Task<RegisterQueryContext?> GetRegisterQueryContextAsync(Guid registerId, CancellationToken ct)
    {
        if (_registerContexts.TryGetValue(registerId, out var cached))
            return await TableExistsAsync(cached.TableName, ct) ? cached : null;

        var (tableName, resourceColumns) = await OperationalRegisterMovementsTableResolver.ResolveOrThrowAsync(
            registers,
            resources,
            registerId,
            ct);

        if (!await TableExistsAsync(tableName, ct))
            return null;

        var context = new RegisterQueryContext(tableName, resourceColumns);
        _registerContexts[registerId] = context;
        return context;
    }

    private async Task<bool> TableExistsAsync(string tableName, CancellationToken ct)
    {
        if (_existingTables.TryGetValue(tableName, out var readiness) && ReferenceEquals(readiness.Transaction, uow.Transaction))
            return true;

        if (!await PostgresTableExistence.ExistsAsync(uow, tableName, ct))
            return false;

        _existingTables[tableName] = new TableReadiness(uow.Transaction);

        return true;
    }

    internal static string BuildDimensionFilterCte(int dimCount)
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

    internal static string BuildDimensionFilterSql(string tableAlias, int dimCount)
        => dimCount == 0
            ? string.Empty
            : $"AND {tableAlias}.dimension_set_id IN (SELECT dimension_set_id FROM matching_dimension_sets)";

    private static string BuildMovementOnlyBalanceSourceSql(
        string movementsTable,
        string resourceColumnCode,
        int dimCount)
    {
        var dimensionFilter = BuildDimensionFilterSql("movement", dimCount);
        return $"""
nets AS (
    SELECT
        grouped.value_id AS ValueId,
        SUM(CASE WHEN movement.is_storno
            THEN -movement.{resourceColumnCode}
            ELSE movement.{resourceColumnCode}
        END) AS NetAmount
    FROM {movementsTable} movement
    JOIN platform_dimension_set_items grouped
      ON grouped.dimension_set_id = movement.dimension_set_id
     AND grouped.dimension_id = @GroupDimensionId
    WHERE movement.period_month <= @AsOfMonth::date
      {dimensionFilter}
    GROUP BY grouped.value_id
)
""";
    }

    private static string BuildSnapshotBackedBalanceSourceSql(
        string movementsTable,
        string balancesTable,
        string resourceColumnCode,
        int dimCount)
    {
        var balanceDimensionFilter = BuildDimensionFilterSql("balance", dimCount);
        var movementDimensionFilter = BuildDimensionFilterSql("movement", dimCount);
        return $"""
latest_snapshot AS (
    SELECT MAX(period_month) AS period_month
    FROM {balancesTable}
    WHERE period_month <= @AsOfMonth::date
),
snapshot_values AS (
    SELECT balance.dimension_set_id, balance.{resourceColumnCode} AS net_amount
    FROM {balancesTable} balance
    CROSS JOIN latest_snapshot latest
    WHERE balance.period_month = latest.period_month
      {balanceDimensionFilter}
),
movement_values AS (
    SELECT
        movement.dimension_set_id,
        SUM(CASE WHEN movement.is_storno
            THEN -movement.{resourceColumnCode}
            ELSE movement.{resourceColumnCode}
        END) AS net_amount
    FROM {movementsTable} movement
    CROSS JOIN latest_snapshot latest
    WHERE (latest.period_month IS NULL OR movement.period_month > latest.period_month)
      AND movement.period_month <= @AsOfMonth::date
      {movementDimensionFilter}
    GROUP BY movement.dimension_set_id
),
dimension_values AS (
    SELECT
        keys.dimension_set_id,
        COALESCE(snapshot.net_amount, 0) + COALESCE(delta.net_amount, 0) AS net_amount
    FROM (
        SELECT dimension_set_id FROM snapshot_values
        UNION
        SELECT dimension_set_id FROM movement_values
    ) keys
    LEFT JOIN snapshot_values snapshot ON snapshot.dimension_set_id = keys.dimension_set_id
    LEFT JOIN movement_values delta ON delta.dimension_set_id = keys.dimension_set_id
),
nets AS (
    SELECT grouped.value_id AS ValueId, SUM(value.net_amount) AS NetAmount
    FROM dimension_values value
    JOIN platform_dimension_set_items grouped
      ON grouped.dimension_set_id = value.dimension_set_id
     AND grouped.dimension_id = @GroupDimensionId
    GROUP BY grouped.value_id
)
""";
    }

    private static string ResolveBalancesTableName(string movementsTable)
    {
        const string movementsSuffix = "__movements";
        if (!movementsTable.EndsWith(movementsSuffix, StringComparison.Ordinal))
            throw new NgbConfigurationViolationException($"Unexpected operational-register movements table name '{movementsTable}'.");

        return $"{movementsTable[..^movementsSuffix.Length]}__balances";
    }

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
    private sealed record TableReadiness(object? Transaction);

    private sealed record GroupNetSqlRow(
        Guid ValueId,
        decimal NetAmount,
        int TotalCount,
        decimal TotalPositive,
        decimal TotalNegativeAbsolute);
}
