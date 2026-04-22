using Dapper;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Internal;
using NGB.PostgreSql.OperationalRegisters.Internal;
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
}
