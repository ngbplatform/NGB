using NGB.Core.Dimensions;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.OperationalRegisters;

/// <summary>
/// Reader for per-register turnovers tables (opreg_*__turnovers).
/// Works both inside and outside a transaction; if the table has not been created yet, returns empty results.
///
/// Notes:
/// - The physical schema is defined by register resources (operational_register_resources).
/// - Returned rows can be enriched with DimensionBag and display values for UI/report rendering.
/// </summary>
public sealed class PostgresOperationalRegisterTurnoversReader(
    IUnitOfWork uow,
    IOperationalRegisterRepository registers,
    IOperationalRegisterResourceRepository resources,
    IDimensionSetReader dimensionSetReader,
    IDimensionValueEnrichmentReader dimensionValueEnrichmentReader)
    : IOperationalRegisterTurnoversReader
{
    public Task<IReadOnlyList<OperationalRegisterMonthlyProjectionReadRow>> GetByMonthsAsync(
        Guid registerId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<DimensionValue>? dimensions = null,
        Guid? dimensionSetId = null,
        CancellationToken ct = default)
        => PostgresOperationalRegisterMonthlyProjectionReaderCore.GetByMonthsAsync(
            uow,
            registerId,
            fromInclusive,
            toInclusive,
            dimensions,
            dimensionSetId,
            ResolveTurnoversTableAndResourcesOrThrowAsync,
            dimensionSetReader,
            dimensionValueEnrichmentReader,
            ct);

    public Task<IReadOnlyList<OperationalRegisterMonthlyProjectionReadRow>> GetPageByMonthsAsync(
        Guid registerId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<DimensionValue>? dimensions = null,
        Guid? dimensionSetId = null,
        DateOnly? afterPeriodMonth = null,
        Guid? afterDimensionSetId = null,
        int limit = 1000,
        CancellationToken ct = default)
        => PostgresOperationalRegisterMonthlyProjectionReaderCore.GetPageByMonthsAsync(
            uow,
            registerId,
            fromInclusive,
            toInclusive,
            dimensions,
            dimensionSetId,
            afterPeriodMonth,
            afterDimensionSetId,
            limit,
            ResolveTurnoversTableAndResourcesOrThrowAsync,
            dimensionSetReader,
            dimensionValueEnrichmentReader,
            ct);

    private async Task<(string TableName, IReadOnlyList<string> ResourceColumns)> ResolveTurnoversTableAndResourcesOrThrowAsync(
        Guid registerId,
        CancellationToken ct)
    {
        var reg = await registers.GetByIdAsync(registerId, ct);
        if (reg is null)
            throw new OperationalRegisterNotFoundException(registerId);

        var tableName = OperationalRegisterNaming.TurnoversTable(reg.TableCode);
        OperationalRegisterSqlIdentifiers.EnsureOrThrow(tableName, "opreg turnovers table name");

        var cols = (await resources.GetByRegisterIdAsync(registerId, ct))
            .OrderBy(r => r.Ordinal)
            .Select(r => r.ColumnCode)
            .ToArray();

        foreach (var c in cols)
        {
            OperationalRegisterSqlIdentifiers.EnsureOrThrow(c, "opreg resource column_code");
        }

        return (tableName, cols);
    }
}
