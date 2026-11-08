using NGB.Accounting.Balances;

namespace NGB.Persistence.Readers;

/// <summary>
/// Reader for operational balance enforcement during posting.
/// Returns, for the given month (period), a snapshot per key:
/// - PreviousClosingBalance: last closed period is less then or equal period
/// - PriorOpenPeriodDelta: signed change after that closed period and before the requested month
/// - DebitTurnover/CreditTurnover: current month turnovers (to-date)
/// </summary>
public interface IAccountingOperationalBalanceReader
{
    Task<IReadOnlyList<AccountingOperationalBalanceSnapshot>> GetForKeysAsync(
        DateOnly period,
        IReadOnlyList<AccountingBalanceKey> keys,
        CancellationToken ct = default);
}
