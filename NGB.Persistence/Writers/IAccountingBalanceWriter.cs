using NGB.Accounting.Balances;

namespace NGB.Persistence.Writers;

public interface IAccountingBalanceWriter
{
    /// <summary>
    /// Deletes all stored balances for the given period.
    /// 
    /// IMPORTANT: This is a maintenance operation used by rebuild tools.
    /// It must be executed within an active transaction.
    /// </summary>
    Task DeleteForPeriodAsync(DateOnly period, CancellationToken ct = default);

    Task SaveAsync(IEnumerable<AccountingBalance> balances, CancellationToken ct = default);
}
