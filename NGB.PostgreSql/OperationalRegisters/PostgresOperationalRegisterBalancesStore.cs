using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.OperationalRegisters;

public sealed class PostgresOperationalRegisterBalancesStore(
    IUnitOfWork uow,
    IOperationalRegisterRepository registers,
    IOperationalRegisterResourceRepository resourcesRepo)
    : IOperationalRegisterBalancesStore
{
    private readonly PostgresOperationalRegisterMonthlyProjectionStoreCore _core = new(
        uow,
        registers,
        resourcesRepo,
        tableCode => OperationalRegisterNaming.BalancesTable(tableCode),
        tableNameDescription: "opreg balances table name",
        indexPrefix: "ix_opreg_b_",
        aliasResourceColumns: false);

    public Task EnsureSchemaAsync(Guid registerId, CancellationToken ct = default)
        => _core.EnsureSchemaAsync(registerId, ct);

    public Task ReplaceForMonthAsync(
        Guid registerId,
        DateOnly periodMonth,
        IReadOnlyList<OperationalRegisterMonthlyProjectionRow> rows,
        CancellationToken ct = default)
        => _core.ReplaceForMonthAsync(registerId, periodMonth, rows, ct);

    public Task<IReadOnlyList<OperationalRegisterMonthlyProjectionRow>> GetByMonthAsync(
        Guid registerId,
        DateOnly periodMonth,
        Guid? dimensionSetId = null,
        CancellationToken ct = default)
        => _core.GetByMonthAsync(registerId, periodMonth, dimensionSetId, ct);
}
