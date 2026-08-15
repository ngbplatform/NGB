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
}
