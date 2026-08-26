using Dapper;
using NGB.Core.Dimensions;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Internal;
using NGB.PostgreSql.OperationalRegisters.Internal;
using NGB.PostgreSql.Readers;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.OperationalRegisters;

/// <summary>
/// Computes net amounts for a single resource in a per-register movements table.
///
/// Storno semantics:
/// <c>net = SUM(non-storno) - SUM(storno)</c>.
/// </summary>
public sealed class PostgresOperationalRegisterResourceNetReader(
    IUnitOfWork uow,
    IOperationalRegisterRepository registers,
    IOperationalRegisterResourceRepository resources)
    : IOperationalRegisterResourceNetReader
{
    public async Task<decimal> GetNetByDimensionSetAsync(
        Guid registerId,
        Guid dimensionSetId,
        string resourceColumnCode,
        CancellationToken ct = default)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(registerId), "RegisterId must not be empty.");

        if (dimensionSetId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(dimensionSetId), "DimensionSetId must not be empty.");

        if (string.IsNullOrWhiteSpace(resourceColumnCode))
            throw new NgbArgumentRequiredException(nameof(resourceColumnCode));

        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        var (tableName, resourceColumns) =
            await OperationalRegisterMovementsTableResolver.ResolveOrThrowAsync(registers, resources, registerId, ct);

        // Fail-fast on misconfiguration (e.g. PM expects 'amount').
        if (!resourceColumns.Contains(resourceColumnCode, StringComparer.Ordinal))
            throw new NgbConfigurationViolationException(
                $"Operational register '{registerId}' does not define resource column '{resourceColumnCode}'.");

        if (!await PostgresTableExistence.ExistsAsync(uow, tableName, ct))
            return 0m;

        // IMPORTANT: identifiers are validated by OperationalRegisterMovementsTableResolver.
        var sql = $"SELECT COALESCE(SUM(CASE WHEN is_storno THEN -{resourceColumnCode} ELSE {resourceColumnCode} END), 0) FROM {tableName} WHERE dimension_set_id = @DimensionSetId;";

        var cmd = new CommandDefinition(
            sql,
            new { DimensionSetId = dimensionSetId },
            transaction: uow.Transaction,
            cancellationToken: ct);

        return await uow.Connection.ExecuteScalarAsync<decimal>(cmd);
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetNetByDimensionSetsAsync(
        Guid registerId,
        IReadOnlyCollection<Guid> dimensionSetIds,
        string resourceColumnCode,
        CancellationToken ct = default)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(registerId), "RegisterId must not be empty.");

        ArgumentNullException.ThrowIfNull(dimensionSetIds);

        if (dimensionSetIds.Any(static id => id == Guid.Empty))
            throw new NgbArgumentInvalidException(nameof(dimensionSetIds), "DimensionSetIds must not contain empty values.");

        if (string.IsNullOrWhiteSpace(resourceColumnCode))
            throw new NgbArgumentRequiredException(nameof(resourceColumnCode));

        var ids = dimensionSetIds.Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<Guid, decimal>();

        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        var (tableName, resourceColumns) =
            await OperationalRegisterMovementsTableResolver.ResolveOrThrowAsync(registers, resources, registerId, ct);

        if (!resourceColumns.Contains(resourceColumnCode, StringComparer.Ordinal))
            throw new NgbConfigurationViolationException(
                $"Operational register '{registerId}' does not define resource column '{resourceColumnCode}'.");

        if (!await PostgresTableExistence.ExistsAsync(uow, tableName, ct))
            return ids.ToDictionary(static id => id, static _ => 0m);

        var sql = $"""
SELECT
    requested.dimension_set_id AS DimensionSetId,
    COALESCE(
        SUM(CASE WHEN movement.is_storno
            THEN -movement.{resourceColumnCode}
            ELSE movement.{resourceColumnCode}
        END),
        0) AS NetAmount
FROM UNNEST(@DimensionSetIds::uuid[]) AS requested(dimension_set_id)
LEFT JOIN {tableName} movement
  ON movement.dimension_set_id = requested.dimension_set_id
GROUP BY requested.dimension_set_id;
""";

        var rows = await uow.Connection.QueryAsync<ResourceNetRow>(new CommandDefinition(
            sql,
            new { DimensionSetIds = ids },
            transaction: uow.Transaction,
            cancellationToken: ct));
        return rows.ToDictionary(static row => row.DimensionSetId, static row => row.NetAmount);
    }

    public async Task<decimal> GetNetByDimensionsAsync(
        Guid registerId,
        IReadOnlyList<DimensionValue> dimensions,
        string resourceColumnCode,
        CancellationToken ct = default)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(registerId), "RegisterId must not be empty.");

        ArgumentNullException.ThrowIfNull(dimensions);
        if (dimensions.Count == 0)
            throw new NgbArgumentInvalidException(nameof(dimensions), "At least one dimension is required.");

        if (string.IsNullOrWhiteSpace(resourceColumnCode))
            throw new NgbArgumentRequiredException(nameof(resourceColumnCode));

        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        var (tableName, resourceColumns) =
            await OperationalRegisterMovementsTableResolver.ResolveOrThrowAsync(registers, resources, registerId, ct);

        if (!resourceColumns.Contains(resourceColumnCode, StringComparer.Ordinal))
            throw new NgbConfigurationViolationException($"Operational register '{registerId}' does not define resource column '{resourceColumnCode}'.");

        if (!await PostgresTableExistence.ExistsAsync(uow, tableName, ct))
            return 0m;

        var (dimensionIds, valueIds, dimensionCount) = SqlDimensionFilter.Normalize(dimensions);
        var sql = $"""
            WITH matching_dimension_sets AS (
                SELECT item.dimension_set_id
                FROM platform_dimension_set_items item
                JOIN UNNEST(@DimensionIds::uuid[], @ValueIds::uuid[])
                  AS requested(dimension_id, value_id)
                  ON requested.dimension_id = item.dimension_id
                 AND requested.value_id = item.value_id
                GROUP BY item.dimension_set_id
                HAVING COUNT(*) = @DimensionCount
            )
            SELECT COALESCE(
                SUM(CASE WHEN movement.is_storno
                    THEN -movement.{resourceColumnCode}
                    ELSE movement.{resourceColumnCode}
                END),
                0)
            FROM {tableName} movement
            WHERE movement.dimension_set_id IN (SELECT dimension_set_id FROM matching_dimension_sets);
            """;

        return await uow.Connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            sql,
            new
            {
                DimensionIds = dimensionIds,
                ValueIds = valueIds,
                DimensionCount = dimensionCount
            },
            transaction: uow.Transaction,
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<decimal>> GetNetsByDimensionsAsync(
        Guid registerId,
        IReadOnlyList<IReadOnlyList<DimensionValue>> dimensionGroups,
        string resourceColumnCode,
        DateOnly asOfInclusive,
        CancellationToken ct = default)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(registerId), "RegisterId must not be empty.");

        ArgumentNullException.ThrowIfNull(dimensionGroups);

        if (dimensionGroups.Any(static group => group is null || group.Count == 0))
            throw new NgbArgumentInvalidException(nameof(dimensionGroups), "Dimension groups must not contain null or empty filters.");

        if (string.IsNullOrWhiteSpace(resourceColumnCode))
            throw new NgbArgumentRequiredException(nameof(resourceColumnCode));

        if (dimensionGroups.Count == 0)
            return [];

        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        var (tableName, resourceColumns) =
            await OperationalRegisterMovementsTableResolver.ResolveOrThrowAsync(registers, resources, registerId, ct);

        if (!resourceColumns.Contains(resourceColumnCode, StringComparer.Ordinal))
            throw new NgbConfigurationViolationException(
                $"Operational register '{registerId}' does not define resource column '{resourceColumnCode}'.");

        if (!await PostgresTableExistence.ExistsAsync(uow, tableName, ct))
            return Enumerable.Repeat(0m, dimensionGroups.Count).ToArray();

        var requestIndexes = new List<int>();
        var dimensionIds = new List<Guid>();
        var valueIds = new List<Guid>();

        for (var requestIndex = 0; requestIndex < dimensionGroups.Count; requestIndex++)
        {
            var normalized = dimensionGroups[requestIndex]
                .DistinctBy(static value => (value.DimensionId, value.ValueId))
                .ToArray();

            foreach (var value in normalized)
            {
                requestIndexes.Add(requestIndex);
                dimensionIds.Add(value.DimensionId);
                valueIds.Add(value.ValueId);
            }
        }

        var occurredToExclusiveUtc = asOfInclusive == DateOnly.MaxValue
            ? DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc)
            : DateTime.SpecifyKind(asOfInclusive.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var sql = $"""
WITH requested_dimensions AS (
    SELECT request_index, dimension_id, value_id
    FROM UNNEST(@RequestIndexes::integer[], @DimensionIds::uuid[], @ValueIds::uuid[])
      AS requested(request_index, dimension_id, value_id)
),
requested_counts AS (
    SELECT request_index, COUNT(*) AS dimension_count
    FROM requested_dimensions
    GROUP BY request_index
),
matching_dimension_sets AS (
    SELECT requested.request_index, item.dimension_set_id
    FROM requested_dimensions requested
    JOIN platform_dimension_set_items item
      ON item.dimension_id = requested.dimension_id
     AND item.value_id = requested.value_id
    GROUP BY requested.request_index, item.dimension_set_id
    HAVING COUNT(*) = (
        SELECT counts.dimension_count
        FROM requested_counts counts
        WHERE counts.request_index = requested.request_index
    )
),
request_numbers AS (
    SELECT generate_series(0, @RequestCount - 1) AS request_index
)
SELECT
    request.request_index AS RequestIndex,
    COALESCE(SUM(CASE WHEN movement.is_storno
        THEN -movement.{resourceColumnCode}
        ELSE movement.{resourceColumnCode}
    END), 0) AS NetAmount
FROM request_numbers request
LEFT JOIN matching_dimension_sets matching
  ON matching.request_index = request.request_index
LEFT JOIN {tableName} movement
  ON movement.dimension_set_id = matching.dimension_set_id
 AND movement.period_month <= @AsOfMonth
 AND movement.occurred_at_utc < @OccurredToExclusiveUtc
GROUP BY request.request_index
ORDER BY request.request_index;
""";

        var rows = (await uow.Connection.QueryAsync<ResourceNetRequestRow>(new CommandDefinition(
            sql,
            new
            {
                RequestIndexes = requestIndexes.ToArray(),
                DimensionIds = dimensionIds.ToArray(),
                ValueIds = valueIds.ToArray(),
                RequestCount = dimensionGroups.Count,
                AsOfMonth = new DateOnly(asOfInclusive.Year, asOfInclusive.Month, 1),
                OccurredToExclusiveUtc = occurredToExclusiveUtc
            },
            transaction: uow.Transaction,
            cancellationToken: ct))).AsList();

        var result = new decimal[dimensionGroups.Count];
        foreach (var row in rows)
            result[row.RequestIndex] = row.NetAmount;

        return result;
    }

    private sealed record ResourceNetRow(Guid DimensionSetId, decimal NetAmount);
    private sealed record ResourceNetRequestRow(int RequestIndex, decimal NetAmount);
}
