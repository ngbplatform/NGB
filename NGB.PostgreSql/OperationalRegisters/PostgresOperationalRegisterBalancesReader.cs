using NGB.Core.Dimensions;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Schema;

namespace NGB.PostgreSql.OperationalRegisters;

/// <summary>
/// Reader for per-register balances tables (opreg_*__balances).
/// Works both inside and outside a transaction; if the table has not been created yet, returns empty results.
///
/// Notes:
/// - The physical schema is defined by register resources (operational_register_resources).
/// - Returned rows can be enriched with DimensionBag and display values for UI/report rendering.
/// </summary>
public sealed class PostgresOperationalRegisterBalancesReader(
    IUnitOfWork uow,
    IOperationalRegisterRepository registers,
    IOperationalRegisterResourceRepository resources,
    IDimensionSetReader dimensionSetReader,
    IDimensionValueEnrichmentReader dimensionValueEnrichmentReader,
    OperationalRegisterMetadataCache? metadataCache = null,
    PostgresRelationPresenceCache? relationPresenceCache = null)
    : IOperationalRegisterBalancesReader
{
    private readonly OperationalRegisterMetadataCache _metadataCache = metadataCache
        ?? new OperationalRegisterMetadataCache(TimeProvider.System);
    private readonly PostgresRelationPresenceCache _relationPresenceCache = relationPresenceCache
        ?? new PostgresRelationPresenceCache(TimeProvider.System);
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
            ResolveBalancesTableAndResourcesOrThrowAsync,
            _relationPresenceCache,
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
            ResolveBalancesTableAndResourcesOrThrowAsync,
            _relationPresenceCache,
            dimensionSetReader,
            dimensionValueEnrichmentReader,
            ct);

    private async Task<(string TableName, IReadOnlyList<string> ResourceColumns)> ResolveBalancesTableAndResourcesOrThrowAsync(
        Guid registerId,
        CancellationToken ct)
    {
        var context = await _metadataCache.GetOrCreateAsync(
            registerId,
            loadCt => LoadMetadataAsync(registerId, loadCt),
            ct);
        var reg = context.Register;

        var tableName = OperationalRegisterNaming.BalancesTable(reg.TableCode);
        OperationalRegisterSqlIdentifiers.EnsureOrThrow(tableName, "opreg balances table name");

        var cols = context.Resources
            .Select(static resource => resource.ColumnCode)
            .ToArray();

        foreach (var c in cols)
            OperationalRegisterSqlIdentifiers.EnsureOrThrow(c, "opreg resource column_code");

        return (tableName, cols);
    }

    private async Task<OperationalRegisterMetadataContext> LoadMetadataAsync(Guid registerId, CancellationToken ct)
    {
        var register = await registers.GetByIdAsync(registerId, ct)
            ?? throw new OperationalRegisterNotFoundException(registerId);
        var movementsTable = OperationalRegisterNaming.MovementsTable(register.TableCode);

        OperationalRegisterSqlIdentifiers.EnsureOrThrow(movementsTable, "opreg movements table name");

        var definitions = (await resources.GetByRegisterIdAsync(registerId, ct))
            .OrderBy(static resource => resource.Ordinal)
            .ToArray();

        foreach (var resource in definitions)
        {
            OperationalRegisterSqlIdentifiers.EnsureOrThrow(resource.ColumnCode, "opreg resource column_code");
        }

        return new OperationalRegisterMetadataContext(register, definitions, movementsTable);
    }
}
