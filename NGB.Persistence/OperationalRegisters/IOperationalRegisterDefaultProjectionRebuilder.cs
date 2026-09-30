namespace NGB.Persistence.OperationalRegisters;

/// <summary>
/// Rebuilds the default turnover and cumulative-balance projections without transferring
/// all dimension/resource rows through managed memory.
/// </summary>
public interface IOperationalRegisterDefaultProjectionRebuilder
{
    Task RebuildMonthAsync(
        Guid registerId,
        DateOnly periodMonth,
        DateOnly? previousFinalizedPeriod,
        CancellationToken ct = default);
}
