using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<Guid, RegisterResourceContext> _registerContexts = new();
    private readonly ConcurrentDictionary<string, TableReadiness> _existingTables = new(StringComparer.Ordinal);

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

        var (tableName, resourceColumns) = await GetRegisterContextAsync(registerId, ct);

        // Fail-fast on misconfiguration (e.g. PM expects 'amount').
        if (!resourceColumns.Contains(resourceColumnCode, StringComparer.Ordinal))
            throw new NgbConfigurationViolationException(
                $"Operational register '{registerId}' does not define resource column '{resourceColumnCode}'.");

        if (!await TableExistsAsync(tableName, ct))
            return 0m;

        var balancesTable = ResolveBalancesTableName(tableName);
        var balancesTableExists = await TableExistsAsync(balancesTable, ct);

        // IMPORTANT: identifiers are validated by OperationalRegisterMovementsTableResolver.
        var sql = balancesTableExists
            ? $"""
              WITH latest_snapshot AS (
                  SELECT MAX(period_month) AS period_month FROM {balancesTable}
              ),
              snapshot AS (
                  SELECT COALESCE(SUM(balance.{resourceColumnCode}), 0) AS amount
                  FROM {balancesTable} balance
                  CROSS JOIN latest_snapshot latest
                  WHERE balance.period_month = latest.period_month
                    AND balance.dimension_set_id = @DimensionSetId
              ),
              delta AS (
                  SELECT COALESCE(SUM(CASE WHEN movement.is_storno
                      THEN -movement.{resourceColumnCode}
                      ELSE movement.{resourceColumnCode}
                  END), 0) AS amount
                  FROM {tableName} movement
                  CROSS JOIN latest_snapshot latest
                  WHERE movement.dimension_set_id = @DimensionSetId
                    AND (latest.period_month IS NULL OR movement.period_month > latest.period_month)
              )
              SELECT snapshot.amount + delta.amount FROM snapshot CROSS JOIN delta;
              """
            : $"SELECT COALESCE(SUM(CASE WHEN is_storno THEN -{resourceColumnCode} ELSE {resourceColumnCode} END), 0) FROM {tableName} WHERE dimension_set_id = @DimensionSetId;";

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

        var (tableName, resourceColumns) = await GetRegisterContextAsync(registerId, ct);

        if (!resourceColumns.Contains(resourceColumnCode, StringComparer.Ordinal))
            throw new NgbConfigurationViolationException(
                $"Operational register '{registerId}' does not define resource column '{resourceColumnCode}'.");

        if (!await TableExistsAsync(tableName, ct))
            return ids.ToDictionary(static id => id, static _ => 0m);

        var balancesTable = ResolveBalancesTableName(tableName);
        var balancesTableExists = await TableExistsAsync(balancesTable, ct);
        var sql = balancesTableExists
            ? $"""
WITH latest_snapshot AS (
    SELECT MAX(period_month) AS period_month FROM {balancesTable}
),
snapshot AS (
    SELECT requested.dimension_set_id, COALESCE(balance.{resourceColumnCode}, 0) AS amount
    FROM UNNEST(@DimensionSetIds::uuid[]) AS requested(dimension_set_id)
    CROSS JOIN latest_snapshot latest
    LEFT JOIN {balancesTable} balance
      ON balance.period_month = latest.period_month
     AND balance.dimension_set_id = requested.dimension_set_id
),
delta AS (
    SELECT
        requested.dimension_set_id,
        COALESCE(SUM(CASE WHEN movement.is_storno
            THEN -movement.{resourceColumnCode}
            ELSE movement.{resourceColumnCode}
        END), 0) AS amount
    FROM UNNEST(@DimensionSetIds::uuid[]) AS requested(dimension_set_id)
    CROSS JOIN latest_snapshot latest
    LEFT JOIN {tableName} movement
      ON movement.dimension_set_id = requested.dimension_set_id
     AND (latest.period_month IS NULL OR movement.period_month > latest.period_month)
    GROUP BY requested.dimension_set_id
)
SELECT snapshot.dimension_set_id AS DimensionSetId, snapshot.amount + delta.amount AS NetAmount
FROM snapshot
JOIN delta USING (dimension_set_id);
"""
            : $"""
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

        var (tableName, resourceColumns) = await GetRegisterContextAsync(registerId, ct);

        if (!resourceColumns.Contains(resourceColumnCode, StringComparer.Ordinal))
            throw new NgbConfigurationViolationException($"Operational register '{registerId}' does not define resource column '{resourceColumnCode}'.");

        if (!await TableExistsAsync(tableName, ct))
            return 0m;

        var (dimensionIds, valueIds, dimensionCount) = SqlDimensionFilter.Normalize(dimensions);
        var balancesTable = ResolveBalancesTableName(tableName);
        var balancesTableExists = await TableExistsAsync(balancesTable, ct);
        var sql = balancesTableExists
            ? $"""
            WITH matching_dimension_sets AS (
                SELECT item.dimension_set_id
                FROM platform_dimension_set_items item
                JOIN UNNEST(@DimensionIds::uuid[], @ValueIds::uuid[])
                  AS requested(dimension_id, value_id)
                  ON requested.dimension_id = item.dimension_id
                 AND requested.value_id = item.value_id
                GROUP BY item.dimension_set_id
                HAVING COUNT(*) = @DimensionCount
            ),
            latest_snapshot AS (
                SELECT MAX(period_month) AS period_month FROM {balancesTable}
            ),
            snapshot AS (
                SELECT COALESCE(SUM(balance.{resourceColumnCode}), 0) AS amount
                FROM matching_dimension_sets matching
                CROSS JOIN latest_snapshot latest
                JOIN {balancesTable} balance
                  ON balance.period_month = latest.period_month
                 AND balance.dimension_set_id = matching.dimension_set_id
            ),
            delta AS (
                SELECT COALESCE(SUM(CASE WHEN movement.is_storno
                    THEN -movement.{resourceColumnCode}
                    ELSE movement.{resourceColumnCode}
                END), 0) AS amount
                FROM matching_dimension_sets matching
                CROSS JOIN latest_snapshot latest
                JOIN {tableName} movement
                  ON movement.dimension_set_id = matching.dimension_set_id
                 AND (latest.period_month IS NULL OR movement.period_month > latest.period_month)
            )
            SELECT snapshot.amount + delta.amount FROM snapshot CROSS JOIN delta;
            """
            : $"""
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

        var (tableName, resourceColumns) = await GetRegisterContextAsync(registerId, ct);

        if (!resourceColumns.Contains(resourceColumnCode, StringComparer.Ordinal))
            throw new NgbConfigurationViolationException(
                $"Operational register '{registerId}' does not define resource column '{resourceColumnCode}'.");

        if (!await TableExistsAsync(tableName, ct))
            return Enumerable.Repeat(0m, dimensionGroups.Count).ToArray();

        var balancesTable = ResolveBalancesTableName(tableName);
        var balancesTableExists = await TableExistsAsync(balancesTable, ct);

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

        var sql = balancesTableExists
            ? BuildSnapshotBackedBatchNetSql(tableName, balancesTable, resourceColumnCode)
            : BuildMovementOnlyBatchNetSql(tableName, resourceColumnCode);

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
        {
            result[row.RequestIndex] = row.NetAmount;
        }

        return result;
    }

    private static string BuildMovementOnlyBatchNetSql(string tableName, string resourceColumnCode) => $"""
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

    private static string BuildSnapshotBackedBatchNetSql(
        string movementsTable,
        string balancesTable,
        string resourceColumnCode)
        => $"""
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
latest_snapshot AS (
    SELECT MAX(period_month) AS period_month
    FROM {balancesTable}
    WHERE period_month < @AsOfMonth
),
snapshot_amounts AS (
    SELECT
        matching.request_index,
        COALESCE(SUM(balance.{resourceColumnCode}), 0) AS net_amount
    FROM matching_dimension_sets matching
    CROSS JOIN latest_snapshot latest
    JOIN {balancesTable} balance
      ON balance.period_month = latest.period_month
     AND balance.dimension_set_id = matching.dimension_set_id
    GROUP BY matching.request_index
),
movement_amounts AS (
    SELECT
        matching.request_index,
        COALESCE(SUM(CASE WHEN movement.is_storno
            THEN -movement.{resourceColumnCode}
            ELSE movement.{resourceColumnCode}
        END), 0) AS net_amount
    FROM matching_dimension_sets matching
    CROSS JOIN latest_snapshot latest
    JOIN {movementsTable} movement
      ON movement.dimension_set_id = matching.dimension_set_id
     AND (latest.period_month IS NULL OR movement.period_month > latest.period_month)
     AND movement.period_month <= @AsOfMonth
     AND movement.occurred_at_utc < @OccurredToExclusiveUtc
    GROUP BY matching.request_index
),
request_numbers AS (
    SELECT generate_series(0, @RequestCount - 1) AS request_index
)
SELECT
    request.request_index AS RequestIndex,
    COALESCE(snapshot.net_amount, 0) + COALESCE(movement.net_amount, 0) AS NetAmount
FROM request_numbers request
LEFT JOIN snapshot_amounts snapshot
  ON snapshot.request_index = request.request_index
LEFT JOIN movement_amounts movement
  ON movement.request_index = request.request_index
ORDER BY request.request_index;
""";

    private static string ResolveBalancesTableName(string movementsTable)
    {
        const string movementsSuffix = "__movements";
        if (!movementsTable.EndsWith(movementsSuffix, StringComparison.Ordinal))
            throw new NgbConfigurationViolationException($"Unexpected operational-register movements table name '{movementsTable}'.");

        return $"{movementsTable[..^movementsSuffix.Length]}__balances";
    }

    private async Task<RegisterResourceContext> GetRegisterContextAsync(Guid registerId, CancellationToken ct)
    {
        if (_registerContexts.TryGetValue(registerId, out var cached))
            return cached;

        var resolved = await OperationalRegisterMovementsTableResolver.ResolveOrThrowAsync(
            registers,
            resources,
            registerId,
            ct);
        var context = new RegisterResourceContext(resolved.TableName, resolved.ResourceColumns);

        return _registerContexts.GetOrAdd(registerId, context);
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

    private sealed record RegisterResourceContext(string TableName, IReadOnlyList<string> ResourceColumns);
    private sealed record TableReadiness(object? Transaction);
    private sealed record ResourceNetRow(Guid DimensionSetId, decimal NetAmount);
    private sealed record ResourceNetRequestRow(int RequestIndex, decimal NetAmount);
}
