using NGB.Accounting.Balances;

namespace NGB.Persistence.Readers;

public interface IAccountingBalanceReader
{
    Task<IReadOnlyList<AccountingBalance>> GetForPeriodAsync(DateOnly period, CancellationToken ct = default);

    /// <summary>
    /// Returns balances of the last closed period less than or equal to the requested period.
    /// Used for reporting snapshots and for rolling balances forward into later periods.
    /// </summary>
    Task<IReadOnlyList<AccountingBalance>> GetLatestClosedAsync(DateOnly period, CancellationToken ct = default);
}
