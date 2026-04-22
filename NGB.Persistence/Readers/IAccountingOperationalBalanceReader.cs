using NGB.Accounting.Balances;

namespace NGB.Persistence.Readers;

/// <summary>
/// Reader for operational balance enforcement during posting.
/// Returns, for the given month (period), a snapshot per key:
/// - PreviousClosingBalance: last closed period <= period
/// - DebitTurnover/CreditTurnover: current month turnovers (to-date)
/// </summary>
public interface IAccountingOperationalBalanceReader
{
    Task<IReadOnlyList<AccountingOperationalBalanceSnapshot>> GetForKeysAsync(
        DateOnly period,
        IReadOnlyList<AccountingBalanceKey> keys,
        CancellationToken ct = default);
}
