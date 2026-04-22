using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.OperationalRegisters;

public sealed class PostgresOperationalRegisterTurnoversStore(
    IUnitOfWork uow,
    IOperationalRegisterRepository registers,
    IOperationalRegisterResourceRepository resourcesRepo)
    : IOperationalRegisterTurnoversStore
{
    private readonly PostgresOperationalRegisterMonthlyProjectionStoreCore _core = new(
        uow,
        registers,
        resourcesRepo,
        tableCode => OperationalRegisterNaming.TurnoversTable(tableCode),
        tableNameDescription: "opreg turnovers table name",
        indexPrefix: "ix_opreg_t_",
        aliasResourceColumns: true);

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
