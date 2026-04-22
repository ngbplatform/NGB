using Microsoft.Extensions.Logging;
using NGB.Accounting.Accounts;
using NGB.Accounting.Balances;
using NGB.Accounting.Periods;
using NGB.Accounting.Reports.AccountingConsistency;
using NGB.Persistence.Locks;
using NGB.Persistence.Periods;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.Writers;
using NGB.Runtime.Accounting;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Extensions;

namespace NGB.Runtime.Maintenance;

/// <summary>
/// Rebuilds derived accounting data (turnovers, balances) from the ground-truth register.
/// STRICT RULE: rebuild is forbidden for closed periods.
/// </summary>
public sealed class AccountingRebuildService(
    IUnitOfWork uow,
    IAdvisoryLockManager advisoryLocks,
    IClosedPeriodRepository closedPeriodRepository,
    IAccountingTurnoverAggregationReader turnoverAggregationReader,
    IAccountingTurnoverReader turnoverReader,
    IAccountingBalanceReader balanceReader,
    IAccountingTurnoverWriter turnoverWriter,
    IAccountingBalanceWriter balanceWriter,
    AccountingBalanceCalculator balanceCalculator,
    AccountingNegativeBalanceChecker negativeBalanceChecker,
    IAccountingConsistencyReportReader consistencyReportReader,
    ILogger<AccountingRebuildService> logger)
    : IAccountingRebuildService
{
    public async Task<AccountingConsistencyReport> VerifyAsync(
        DateOnly period,
        DateOnly? previousPeriodForChainCheck = null,
        CancellationToken ct = default)
    {
        period.EnsureMonthStart(nameof(period));
        period = AccountingPeriod.FromDateOnly(period);
        previousPeriodForChainCheck = previousPeriodForChainCheck.HasValue 
            ? NormalizeToMonthStart(previousPeriodForChainCheck.Value)
            : null;

        return await consistencyReportReader.RunForPeriodAsync(period, previousPeriodForChainCheck, ct);
    }

    public async Task<int> RebuildTurnoversAsync(DateOnly period, CancellationToken ct = default)
    {
        period = NormalizeToMonthStart(period);
        period = AccountingPeriod.FromDateOnly(period);
        await GuardNotClosedAsync(period, ct);

        return await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            try
            {
                await advisoryLocks.LockPeriodAsync(period, innerCt);

                await turnoverWriter.DeleteForPeriodAsync(period, innerCt);
                var computed = await turnoverAggregationReader.GetAggregatedFromRegisterAsync(period, innerCt);
                await turnoverWriter.WriteAsync(computed, innerCt);

                return computed.Count;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RebuildTurnovers failed. period={Period}", period);
                throw;
            }
        }, ct);
    }

    public async Task<int> RebuildBalancesAsync(DateOnly period, CancellationToken ct = default)
    {
        period = NormalizeToMonthStart(period);
        period = AccountingPeriod.FromDateOnly(period);
        await GuardNotClosedAsync(period, ct);

        return await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            try
            {
                await advisoryLocks.LockPeriodAsync(period, innerCt);

                var previousPeriod = period.AddMonths(-1);
                var previousBalances = await balanceReader.GetForPeriodAsync(previousPeriod, innerCt);
                var turnovers = await turnoverReader.GetForPeriodAsync(period, innerCt);

                var balances = balanceCalculator.Calculate(turnovers, previousBalances, period).ToList();
                await CheckNegativeBalancesAsync(balances, innerCt);

                await balanceWriter.DeleteForPeriodAsync(period, innerCt);
                await balanceWriter.SaveAsync(balances, innerCt);

                return balances.Count;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RebuildBalances failed. period={Period}", period);
                throw;
            }
        }, ct);
    }

    public async Task<AccountingRebuildResult> RebuildAndVerifyAsync(
        DateOnly period,
        DateOnly? previousPeriodForChainCheck = null,
        CancellationToken ct = default)
    {
        period = NormalizeToMonthStart(period);
        period = AccountingPeriod.FromDateOnly(period);
        previousPeriodForChainCheck = previousPeriodForChainCheck.HasValue
            ? NormalizeToMonthStart(previousPeriodForChainCheck.Value)
            : null;

        logger.LogInformation(
            "Accounting maintenance started: RebuildAndVerify. period={Period} previousPeriodForChainCheck={PreviousPeriod}",
            period,
            previousPeriodForChainCheck);

        var result = await RebuildMonthAsync(period, previousPeriodForChainCheck, ct);

        var r = result.VerifyReport;
        logger.LogInformation(
            "Accounting maintenance completed: RebuildAndVerify. period={Period} turnoverRows={TurnoverRows} balanceRows={BalanceRows} ok={Ok} " +
            "turnoversVsRegisterDiff={TurnoversVsRegisterDiffCount} balanceVsTurnoverMismatch={BalanceVsTurnoverMismatchCount} balanceChainMismatch={BalanceChainMismatchCount} missingKeys={MissingKeyCount} issues={IssuesCount}",
            result.Period,
            result.TurnoverRowsWritten,
            result.BalanceRowsWritten,
            r.IsOk,
            r.TurnoversVsRegisterDiffCount,
            r.BalanceVsTurnoverMismatchCount,
            r.BalanceChainMismatchCount,
            r.MissingKeyCount,
            r.Issues.Count);

        return result;
    }

    public async Task<AccountingRebuildResult> RebuildMonthAsync(
        DateOnly period,
        DateOnly? previousPeriodForChainCheck = null,
        CancellationToken ct = default)
    {
        period = NormalizeToMonthStart(period);
        period = AccountingPeriod.FromDateOnly(period);
        previousPeriodForChainCheck = previousPeriodForChainCheck.HasValue
            ? NormalizeToMonthStart(previousPeriodForChainCheck.Value)
            : null;

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["Period"] = period.ToString("yyyy-MM-dd")
        });

        await GuardNotClosedAsync(period, ct);

        return await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            try
            {
                await advisoryLocks.LockPeriodAsync(period, innerCt);

                // 1) Turnovers: delete -> compute from register -> write
                await turnoverWriter.DeleteForPeriodAsync(period, innerCt);
                var computedTurnovers = await turnoverAggregationReader.GetAggregatedFromRegisterAsync(period, innerCt);
                await turnoverWriter.WriteAsync(computedTurnovers, innerCt);

                // 2) Balances: delete -> compute (prev + turnovers) -> save
                var previousPeriod = period.AddMonths(-1);
                var previousBalances = await balanceReader.GetForPeriodAsync(previousPeriod, innerCt);
                var balances = balanceCalculator.Calculate(computedTurnovers, previousBalances, period).ToList();
                await CheckNegativeBalancesAsync(balances, innerCt);

                await balanceWriter.DeleteForPeriodAsync(period, innerCt);
                await balanceWriter.SaveAsync(balances, innerCt);

                // 3) Verify (still inside txn for a consistent view)
                var report = await consistencyReportReader.RunForPeriodAsync(period, previousPeriodForChainCheck, innerCt);

                return new AccountingRebuildResult
                {
                    Period = period,
                    TurnoverRowsWritten = computedTurnovers.Count,
                    BalanceRowsWritten = balances.Count,
                    VerifyReport = report
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RebuildMonth failed. period={Period}", period);
                throw;
            }
        }, ct);
    }

    private static DateOnly NormalizeToMonthStart(DateOnly period)
    {
        // Accounting maintenance APIs accept any day within the month but operate on the month bucket.
        return new DateOnly(period.Year, period.Month, 1);
    }

    private async Task GuardNotClosedAsync(DateOnly period, CancellationToken ct)
    {
        if (await closedPeriodRepository.IsClosedAsync(period, ct))
            throw new AccountingRebuildPeriodClosedException(period);
    }

    private async Task CheckNegativeBalancesAsync(IEnumerable<AccountingBalance> balances, CancellationToken ct)
    {
        var violations = await negativeBalanceChecker.CheckAsync(balances, ct);

        var forbids = violations
            .Where(v => v.Policy == NegativeBalancePolicy.Forbid)
            .ToList();

        if (forbids.Count > 0)
        {
            var msg = string.Join(Environment.NewLine, forbids.Select(v =>
                $"Negative balance forbidden: {v.AccountCode} {v.AccountName} ({v.AccountType}) = {v.ClosingBalance} period={v.Period:yyyy-MM-dd}"));
            throw new AccountingNegativeBalanceForbiddenException(msg);
        }

        foreach (var w in violations.Where(v => v.Policy == NegativeBalancePolicy.Warn))
        {
            logger.LogWarning(
                "WARN: Negative balance: {AccountCode} {AccountName} ({AccountType}) = {ClosingBalance} period={Period}",
                w.AccountCode,
                w.AccountName,
                w.AccountType,
                w.ClosingBalance,
                w.Period);
        }
    }
}
