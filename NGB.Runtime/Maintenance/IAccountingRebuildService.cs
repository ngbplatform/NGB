using NGB.Accounting.Reports.AccountingConsistency;

namespace NGB.Runtime.Maintenance;

/// <summary>
/// Maintenance/repair operations for accounting derived data.
/// 
/// Derived data in this architecture:
/// - accounting_turnovers  (monthly aggregation of accounting_register_main)
/// - accounting_balances   (opening/closing snapshot, usually produced by closing)
/// 
/// This service is designed to be PRODUCTION-safe:
/// - operations are transactional
/// - period-level concurrency is guarded by advisory locks
/// - REBUILD is strictly forbidden for closed periods
/// </summary>
public interface IAccountingRebuildService
{
    Task<AccountingConsistencyReport> VerifyAsync(
        DateOnly period,
        DateOnly? previousPeriodForChainCheck = null,
        CancellationToken ct = default);

    Task<int> RebuildTurnoversAsync(DateOnly period, CancellationToken ct = default);

    Task<int> RebuildBalancesAsync(DateOnly period, CancellationToken ct = default);

    /// <summary>
    /// Rebuilds turnovers and balances for the given month and runs Verify.
    /// All actions are executed in a single transaction.
    /// </summary>
    Task<AccountingRebuildResult> RebuildMonthAsync(
        DateOnly period,
        DateOnly? previousPeriodForChainCheck = null,
        CancellationToken ct = default);

    /// <summary>
    /// Single entry point for maintenance: rebuild turnovers + balances for the month and run Verify.
    /// All actions are executed in a single transaction.
    /// Emits structured logs (start + summary).
    /// </summary>
    Task<AccountingRebuildResult> RebuildAndVerifyAsync(
        DateOnly period,
        DateOnly? previousPeriodForChainCheck = null,
        CancellationToken ct = default);
}
