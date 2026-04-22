using Microsoft.Extensions.Logging;
using NGB.Accounting.Accounts;
using NGB.Accounting.Balances;
using NGB.Accounting.Periods;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Core.Dimensions;
using NGB.Persistence.Checkers;
using NGB.Persistence.Locks;
using NGB.Persistence.Periods;
using NGB.Persistence.PostingState;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Periods;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.Writers;
using NGB.Runtime.Accounting;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Diagnostics;
using NGB.Runtime.Posting;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Extensions;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Periods;

public sealed class PeriodClosingService(
    IUnitOfWork uow,
    IAuditLogService audit,
    IAdvisoryLockManager advisoryLocks,
    IAccountingTurnoverReader turnoverReader,
    IAccountingTurnoverAggregationReader turnoverAggregationReader,
    IAccountingTurnoverWriter turnoverWriter,
    IAccountingBalanceReader balanceReader,
    IAccountingBalanceWriter balanceWriter,
    IAccountingEntryMaintenanceWriter entryMaintenanceWriter,
    IClosedPeriodRepository closedPeriodRepository,
    IClosedPeriodReader closedPeriodReader,
    IAccountingPeriodActivityReader activityReader,
    IChartOfAccountsProvider chartOfAccountsProvider,
    ITrialBalanceReader trialBalance,
    PostingEngine postingEngine,
    AccountingBalanceCalculator calculator,
    IAccountingIntegrityChecker integrityChecker,
    IPostingStateRepository postingStateRepository,
    IPostingStateReader postingStateReader,
    IAuditEventReader auditEventReader,
    AccountingNegativeBalanceChecker negativeBalanceChecker,
    IAccountByIdResolver accountByIdResolver,
    ILogger<PeriodClosingService> logger,
    TimeProvider? timeProvider = null)
    : IPeriodClosingService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    public async Task CloseMonthAsync(DateOnly period, string closedBy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(closedBy))
            throw new NgbArgumentRequiredException(nameof(closedBy));

        // Normalize to month start (1st day) before logging/audit scope to keep telemetry consistent.
        period = AccountingPeriod.FromDateOnly(period);

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["Period"] = period.ToString("yyyy-MM-dd"),
            ["ClosedBy"] = closedBy
        });
        RuntimeLog.PeriodClosingStarted(logger);

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            try
            {
                // CRITICAL CONCURRENCY GUARD:
                // Lock the period for the duration of this transaction.
                // Prevents races:
                //  - Close vs Close (double closing)
                //  - Close vs Posting (posting into the period being closed)
                await advisoryLocks.LockPeriodAsync(period, innerCt);

                // Check: the period is not already closed
                if (await closedPeriodRepository.IsClosedAsync(period, innerCt))
                    throw new PeriodAlreadyClosedException(period);

                var chain = await LoadChainSnapshotAsync(innerCt);
                if (chain.HasBrokenChain && PeriodClosingChainEvaluator.HasLaterClosedPeriods(chain, period))
                    throw new MonthClosingBlockedByLaterClosedPeriodException(period, chain.LatestClosedPeriod!.Value);

                if (!PeriodClosingChainEvaluator.CanCloseMonth(chain, period))
                {
                    var nextClosablePeriod = chain.NextClosablePeriod ?? period;
                    throw new MonthClosingPrerequisiteNotMetException(nextClosablePeriod);
                }

                // Integrity check (turnovers must match register aggregation for this period)
                await integrityChecker.AssertPeriodIsBalancedAsync(period, innerCt);

                // Get turnover for the period
                var turnovers = await turnoverReader.GetForPeriodAsync(period, innerCt);

                // Get balances from the previous period (month)
                var previousPeriod = period.AddMonths(-1);
                var previousBalances = await balanceReader.GetForPeriodAsync(previousPeriod, innerCt);

                // Calculate the new balances (with carry-forward)
                var balances = calculator.Calculate(turnovers, previousBalances, period).ToList();

                await CheckNegativeBalanceAsync(balances, innerCt);

                // Save balances
                await balanceWriter.SaveAsync(balances, innerCt);

                var closedAtUtc = _timeProvider.GetUtcNowDateTime();
                closedAtUtc.EnsureUtc(nameof(closedAtUtc));

                // Record closing
                await closedPeriodRepository.MarkClosedAsync(period, closedBy, closedAtUtc, innerCt);

                // Business AuditLog (atomic with closing)
                var periodEntityId = DeterministicGuid.Create($"CloseMonth|{period:yyyy-MM-dd}");
                await audit.WriteAsync(
                    AuditEntityKind.Period,
                    periodEntityId,
                    AuditActionCodes.PeriodCloseMonth,
                    changes:
                    [
                        AuditLogService.Change("is_closed", false, true),
                        AuditLogService.Change("closed_by", null, closedBy),
                        AuditLogService.Change("closed_at_utc", null, closedAtUtc)
                    ],
                    metadata: new { period = period.ToString("yyyy-MM-dd") },
                    ct: innerCt);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Period closing failed.");
                throw;
            }
        }, ct);

        RuntimeLog.PeriodClosingCompleted(logger);
    }

    public async Task ReopenMonthAsync(DateOnly period, string reopenedBy, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reopenedBy))
            throw new NgbArgumentRequiredException(nameof(reopenedBy));

        if (string.IsNullOrWhiteSpace(reason))
            throw new NgbArgumentRequiredException(nameof(reason));

        period = AccountingPeriod.FromDateOnly(period);

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["Period"] = period.ToString("yyyy-MM-dd"),
            ["ReopenedBy"] = reopenedBy
        });

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            try
            {
                await advisoryLocks.LockPeriodAsync(period, innerCt);

                if (!await closedPeriodRepository.IsClosedAsync(period, innerCt))
                    throw new PeriodNotClosedException(period);

                var chain = await LoadChainSnapshotAsync(innerCt);
                if (chain.LatestClosedPeriod is null)
                    throw new PeriodNotClosedException(period);

                if (chain.LatestClosedPeriod.Value != period)
                    throw new MonthReopenLatestClosedRequiredException(period, chain.LatestClosedPeriod.Value);

                var fiscalYearDocumentId = DeterministicGuid.Create($"CloseFiscalYear|{period:yyyy-MM-dd}");
                var fiscalYearRow = (await postingStateReader.GetPageAsync(
                    new PostingStatePageRequest
                    {
                        DocumentId = fiscalYearDocumentId,
                        Operation = PostingOperation.CloseFiscalYear,
                        PageSize = 1
                    },
                    innerCt)).Records.SingleOrDefault();

                if (fiscalYearRow is not null)
                    throw new MonthReopenBlockedByFiscalYearCloseException(period, fiscalYearDocumentId);

                await closedPeriodRepository.ReopenAsync(period, innerCt);

                var reopenedAtUtc = _timeProvider.GetUtcNowDateTime();
                reopenedAtUtc.EnsureUtc(nameof(reopenedAtUtc));

                var periodEntityId = DeterministicGuid.Create($"CloseMonth|{period:yyyy-MM-dd}");
                await audit.WriteAsync(
                    AuditEntityKind.Period,
                    periodEntityId,
                    AuditActionCodes.PeriodReopenMonth,
                    changes:
                    [
                        AuditLogService.Change("is_closed", true, false),
                        AuditLogService.Change("reopened_by", null, reopenedBy),
                        AuditLogService.Change("reopen_reason", null, reason.Trim()),
                        AuditLogService.Change("reopened_at_utc", null, reopenedAtUtc)
                    ],
                    metadata: new { period = period.ToString("yyyy-MM-dd") },
                    ct: innerCt);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Period reopen failed.");
                throw;
            }
        }, ct);
    }

    public async Task ReopenFiscalYearAsync(
        DateOnly fiscalYearEndPeriod,
        string reopenedBy,
        string reason,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reopenedBy))
            throw new NgbArgumentRequiredException(nameof(reopenedBy));

        if (string.IsNullOrWhiteSpace(reason))
            throw new NgbArgumentRequiredException(nameof(reason));

        fiscalYearEndPeriod.EnsureMonthStart(nameof(fiscalYearEndPeriod));
        var documentId = DeterministicGuid.Create($"CloseFiscalYear|{fiscalYearEndPeriod:yyyy-MM-dd}");

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["FiscalYearEndPeriod"] = fiscalYearEndPeriod.ToString("yyyy-MM-dd"),
            ["DocumentId"] = documentId,
            ["ReopenedBy"] = reopenedBy
        });

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            try
            {
                await advisoryLocks.LockPeriodAsync(fiscalYearEndPeriod, innerCt);

                var postingRow = await TryGetFiscalYearPostingRowAsync(documentId, innerCt);
                if (postingRow is null)
                    throw new FiscalYearNotClosedException(fiscalYearEndPeriod, documentId);

                if (postingRow.Status is PostingStateStatus.InProgress or PostingStateStatus.StaleInProgress)
                    throw new FiscalYearReopenBlockedByInProgressException(fiscalYearEndPeriod, documentId);

                if (postingRow.Status != PostingStateStatus.Completed)
                    throw new FiscalYearNotClosedException(fiscalYearEndPeriod, documentId);

                var endPeriodClosed = await closedPeriodRepository.IsClosedAsync(fiscalYearEndPeriod, innerCt);
                if (endPeriodClosed)
                {
                    // If the end month is currently the edge of the chain, the next month becomes closable.
                    // Lock it too so a concurrent close cannot race with this reopen and create a broken chain.
                    await advisoryLocks.LockPeriodAsync(fiscalYearEndPeriod.AddMonths(1), innerCt);
                }

                var chain = await LoadChainSnapshotAsync(innerCt);
                if (chain.LatestClosedPeriod is not null && chain.LatestClosedPeriod.Value > fiscalYearEndPeriod)
                    throw new FiscalYearReopenBlockedByLaterClosedPeriodException(fiscalYearEndPeriod, chain.LatestClosedPeriod.Value);

                if (endPeriodClosed)
                    await closedPeriodRepository.ReopenAsync(fiscalYearEndPeriod, innerCt);

                var deletedPeriods = await entryMaintenanceWriter.DeleteByDocumentAsync(documentId, innerCt);
                if (deletedPeriods.Any(x => x != fiscalYearEndPeriod))
                {
                    throw new NgbInvariantViolationException(
                        "Fiscal year close entries were found outside the expected end period.",
                        new Dictionary<string, object?>
                        {
                            ["documentId"] = documentId,
                            ["fiscalYearEndPeriod"] = fiscalYearEndPeriod.ToString("yyyy-MM-dd"),
                            ["affectedPeriods"] = deletedPeriods.Select(x => x.ToString("yyyy-MM-dd")).ToArray()
                        });
                }

                await turnoverWriter.DeleteForPeriodAsync(fiscalYearEndPeriod, innerCt);
                var rebuiltTurnovers = await turnoverAggregationReader.GetAggregatedFromRegisterAsync(fiscalYearEndPeriod, innerCt);
                await turnoverWriter.WriteAsync(rebuiltTurnovers, innerCt);

                // Reopened periods must not keep a frozen monthly balance snapshot.
                await balanceWriter.DeleteForPeriodAsync(fiscalYearEndPeriod, innerCt);

                await postingStateRepository.ClearCompletedStateAsync(documentId, PostingOperation.CloseFiscalYear, innerCt);

                var reopenedAtUtc = _timeProvider.GetUtcNowDateTime();
                reopenedAtUtc.EnsureUtc(nameof(reopenedAtUtc));
                var trimmedReason = reason.Trim();

                await audit.WriteAsync(
                    AuditEntityKind.Period,
                    documentId,
                    AuditActionCodes.PeriodReopenFiscalYear,
                    changes:
                    [
                        AuditLogService.Change("is_fiscal_year_closed", true, false),
                        AuditLogService.Change("reopened_by", null, reopenedBy),
                        AuditLogService.Change("reopen_reason", null, trimmedReason),
                        AuditLogService.Change("reopened_at_utc", null, reopenedAtUtc),
                        AuditLogService.Change("closing_entries_removed", null, deletedPeriods.Count > 0),
                        AuditLogService.Change("end_period_reopened", null, endPeriodClosed)
                    ],
                    metadata: new
                    {
                        fiscal_year_end_period = fiscalYearEndPeriod.ToString("yyyy-MM-dd"),
                        deleted_entry_periods = deletedPeriods.Select(x => x.ToString("yyyy-MM-dd")).ToArray(),
                        rebuilt_turnover_rows = rebuiltTurnovers.Count
                    },
                    ct: innerCt);

                if (endPeriodClosed)
                {
                    var periodEntityId = DeterministicGuid.Create($"CloseMonth|{fiscalYearEndPeriod:yyyy-MM-dd}");
                    await audit.WriteAsync(
                        AuditEntityKind.Period,
                        periodEntityId,
                        AuditActionCodes.PeriodReopenMonth,
                        changes:
                        [
                            AuditLogService.Change("is_closed", true, false),
                            AuditLogService.Change("reopened_by", null, reopenedBy),
                            AuditLogService.Change("reopen_reason", null, trimmedReason),
                            AuditLogService.Change("reopened_at_utc", null, reopenedAtUtc)
                        ],
                        metadata: new
                        {
                            period = fiscalYearEndPeriod.ToString("yyyy-MM-dd"),
                            initiated_by = AuditActionCodes.PeriodReopenFiscalYear,
                            fiscal_year_document_id = documentId
                        },
                        ct: innerCt);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fiscal year reopen failed.");
                throw;
            }
        }, ct);
    }

    public async Task CloseFiscalYearAsync(
        DateOnly fiscalYearEndPeriod,
        Guid retainedEarningsAccountId,
        string closedBy,
        CancellationToken ct = default)
    {
        if (retainedEarningsAccountId == Guid.Empty)
            throw new NgbArgumentOutOfRangeException(nameof(retainedEarningsAccountId), retainedEarningsAccountId, "Argument is out of range.");

        if (string.IsNullOrWhiteSpace(closedBy))
            throw new NgbArgumentRequiredException(nameof(closedBy));

        fiscalYearEndPeriod.EnsureMonthStart(nameof(fiscalYearEndPeriod));

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["FiscalYearEndPeriod"] = fiscalYearEndPeriod.ToString("yyyy-MM-dd"),
            ["RetainedEarningsAccountId"] = retainedEarningsAccountId,
            ["ClosedBy"] = closedBy,
        });

        RuntimeLog.FiscalYearClosingStarted(logger);
        var yearStart = new DateOnly(fiscalYearEndPeriod.Year, 1, 1);

        var documentId = DeterministicGuid.Create($"CloseFiscalYear|{fiscalYearEndPeriod:yyyy-MM-dd}");
        var chart = await chartOfAccountsProvider.GetAsync(ct);

        if (!chart.TryGet(retainedEarningsAccountId, out var retainedEarnings) || retainedEarnings is null)
            throw new NgbArgumentInvalidException(nameof(retainedEarningsAccountId), $"Retained earnings account not found: {retainedEarningsAccountId}");

        if (retainedEarnings.StatementSection != StatementSection.Equity)
            throw new NgbArgumentInvalidException(nameof(retainedEarningsAccountId), $"Retained earnings account must belong to Equity. account={retainedEarnings.Code} section={retainedEarnings.StatementSection}");

        if (retainedEarnings.NormalBalance != NormalBalance.Credit)
            throw new NgbArgumentInvalidException(nameof(retainedEarningsAccountId), $"Retained earnings account must be Credit-normal. account={retainedEarnings.Code} normal={retainedEarnings.NormalBalance}");

        if (retainedEarnings.DimensionRules.Any(x => x.IsRequired))
            throw FiscalYearRetainedEarningsValidationException.DimensionsNotAllowed(retainedEarnings.Id, retainedEarnings.Code);

        // Trial Balance is month-based: [from..to] must be month starts.
        yearStart.EnsureMonthStart(nameof(yearStart));

        // Create one accounting document for the whole closing operation.
        // IMPORTANT (variant A semantics): fiscal year closing must be idempotent.
        // We MUST prevent double-close for the same end period.
        //
        // We achieve it by using a deterministic documentId, so PostingEngine's accounting posting state/history idempotency
        // (key: document_id + operation) makes the operation retry-safe and double-close safe.
        //
        // NOTE: The document id is based ONLY on the end period, not on retained earnings account id.
        // The current mutable posting-state row determines whether the fiscal year is presently closed.
        // Immutable audit history still preserves previous retained earnings choices for mismatch diagnostics.
        // Close on the last day of the fiscal year-end month (UTC midnight).
        // All entries share the same UTC day => validator invariant.
        var closingDay = new DateTime(
            fiscalYearEndPeriod.Year,
            fiscalYearEndPeriod.Month,
            DateTime.DaysInMonth(fiscalYearEndPeriod.Year, fiscalYearEndPeriod.Month),
            0, 0, 0,
            DateTimeKind.Utc);

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            try
            {
                // CRITICAL CONCURRENCY GUARD:
                // Lock the full fiscal-year window [Jan..endPeriod] in ascending order.
                // This keeps prerequisite reads stable while we evaluate them and serializes:
                //  - CloseFiscalYear vs Posting into the end period
                //  - CloseFiscalYear vs Close/ReopenMonth on prerequisite months
                //  - concurrent fiscal-year close attempts for the same end period
                await LockFiscalYearWindowAsync(yearStart, fiscalYearEndPeriod, innerCt);
                await EnsureFiscalYearClosePrerequisitesAsync(yearStart, fiscalYearEndPeriod, innerCt);

                var currentPostingRow = await TryGetFiscalYearPostingRowAsync(documentId, innerCt);
                if (currentPostingRow?.Status == PostingStateStatus.Completed)
                {
                    var existingRetainedEarningsAccountId = await FiscalYearCloseAuditReader.TryGetRetainedEarningsAccountIdAsync(
                        auditEventReader,
                        documentId,
                        innerCt);

                    if (existingRetainedEarningsAccountId.HasValue
                        && existingRetainedEarningsAccountId.Value != retainedEarningsAccountId)
                    {
                        var actualDisplay = await ResolveRetainedEarningsAccountDisplayAsync(existingRetainedEarningsAccountId.Value, innerCt);
                        throw new FiscalYearAlreadyClosedWithDifferentRetainedEarningsException(
                            fiscalYearEndPeriod,
                            documentId,
                            retainedEarningsAccountId,
                            existingRetainedEarningsAccountId.Value,
                            actualDisplay);
                    }

                    throw new FiscalYearAlreadyClosedException(fiscalYearEndPeriod, documentId);
                }

                if (currentPostingRow?.Status == PostingStateStatus.InProgress)
                    throw new FiscalYearClosingAlreadyInProgressException(fiscalYearEndPeriod, documentId);

                // Read Trial Balance INSIDE the transaction, after we acquired the fiscal-year window locks.
                // This makes closing entries consistent with the state we are closing.
                var tb = await trialBalance.GetAsync(yearStart, fiscalYearEndPeriod, innerCt);

                // Pre-resolve inactive accounts referenced in trial balance in a single round-trip.
                // This avoids N+1 DB lookups during fiscal-year closing (potentially thousands of P&L accounts).
                var missingIds = new HashSet<Guid>();
                foreach (var row in tb)
                {
                    if (row.AccountId == Guid.Empty)
                        continue;

                    if (chart.TryGet(row.AccountId, out var active) && active is not null)
                        continue;

                    missingIds.Add(row.AccountId);
                }

                var missingResolved = missingIds.Count == 0
                    ? new Dictionary<Guid, Account>()
                    : await accountByIdResolver.GetByIdsAsync(missingIds, innerCt);

                Account? ResolveAccountForFiscalYearClosing(Guid accountId)
                {
                    if (chart.TryGet(accountId, out var active) && active is not null)
                        return active;

                    return missingResolved.GetValueOrDefault(accountId);
                }

                // Build a cheap "do we have anything to close?" flag up-front.
                // It is valid to close a fiscal year with zero P&L activity (net income = 0).
                // In this case we still want the operation to be idempotent and recorded in accounting posting state/history,
                // but there are no register writes to perform.
                var hasClosingMovements = false;
                foreach (var row in tb)
                {
                    if (row.AccountId == Guid.Empty)
                        continue;

                    var acc = ResolveAccountForFiscalYearClosing(row.AccountId);
                    if (acc is null)
                        throw new AccountNotFoundException(row.AccountId);

                    if (!IsProfitAndLoss(acc.StatementSection))
                        continue;

                    var presented = acc.NormalBalance == NormalBalance.Debit
                        ? row.ClosingBalance
                        : -row.ClosingBalance;

                    if (presented != 0m)
                    {
                        hasClosingMovements = true;
                        break;
                    }
                }

                if (!hasClosingMovements)
                {
                    // No closing entries required. Still record CloseFiscalYear in accounting posting state/history (idempotency + audit).
                    var startedAtUtc = _timeProvider.GetUtcNowDateTime();
                    var begin = await postingStateRepository.TryBeginAsync(documentId, PostingOperation.CloseFiscalYear, startedAtUtc, innerCt);

                    if (begin == PostingStateBeginResult.AlreadyCompleted)
                        throw new FiscalYearAlreadyClosedException(fiscalYearEndPeriod, documentId);

                    if (begin == PostingStateBeginResult.InProgress)
                        throw new FiscalYearClosingAlreadyInProgressException(fiscalYearEndPeriod, documentId);

                    var closedAtUtc = _timeProvider.GetUtcNowDateTime();
                    closedAtUtc.EnsureUtc(nameof(closedAtUtc));

                    await postingStateRepository.MarkCompletedAsync(documentId, PostingOperation.CloseFiscalYear, closedAtUtc, innerCt);

                    // Business AuditLog (atomic with posting-state completion)
                    await audit.WriteAsync(
                        AuditEntityKind.Period,
                        documentId,
                        AuditActionCodes.PeriodCloseFiscalYear,
                        changes:
                        [
                            AuditLogService.Change("is_fiscal_year_closed", false, true),
                            AuditLogService.Change("fiscal_year_end_period", null, fiscalYearEndPeriod),
                            AuditLogService.Change("retained_earnings_account_id", null, retainedEarningsAccountId),
                            AuditLogService.Change("closing_entries_posted", null, false),
                            AuditLogService.Change("closed_by", null, closedBy),
                            AuditLogService.Change("closed_at_utc", null, closedAtUtc)
                        ],
                        metadata: new { closing_day_utc = closingDay },
                        ct: innerCt);

                    logger.LogInformation(
                        "Fiscal year close produced no closing entries (net income = 0). Recorded CloseFiscalYear in posting state/history. endPeriod={EndPeriod}",
                        fiscalYearEndPeriod);

                    return;
                }

                PostingResult postingResult;
                try
                {
                    postingResult = await postingEngine.PostAsync(
                    PostingOperation.CloseFiscalYear,
                    async (ctx, _) =>
                    {
                        // Trial Balance is per (account + dimension set), so each row is closed to zero.
                        // Fixed-slot projections (first 3 dimensions) are not used; rows carry canonical Dimensions via DimensionSetId.
                        foreach (var row in tb)
                        {
                            if (row.AccountId == Guid.Empty)
                                continue;

                            var acc = ResolveAccountForFiscalYearClosing(row.AccountId);
                            if (acc is null)
                                throw new AccountNotFoundException(row.AccountId);

                            if (!IsProfitAndLoss(acc.StatementSection))
                                continue;

                            // TrialBalanceRow.ClosingBalance = (debits - credits):
                            // - positive => the account has a net DEBIT balance
                            // - negative => the account has a net CREDIT balance
                            //
                            // IMPORTANT: we must close accounts to zero regardless of Contra/NormalBalance.
                            // Contra P&L accounts can have the opposite normal balance, so the closing direction
                            // MUST be derived from the actual signed ClosingBalance.
                            var net = row.ClosingBalance;

                            if (net == 0m)
                                continue;

                            var amount = Math.Abs(net);

                            if (net < 0m)
                            {
                                // Net credit balance => debit the account; credit retained earnings.
                                ctx.Post(
                                    documentId,
                                    closingDay,
                                    debit: acc,
                                    credit: retainedEarnings,
                                    amount: amount,
                                    debitDimensions: row.Dimensions,
                                    creditDimensions: DimensionBag.Empty);
                            }
                            else
                            {
                                // Net debit balance => credit the account; debit retained earnings.
                                ctx.Post(
                                    documentId,
                                    closingDay,
                                    debit: retainedEarnings,
                                    credit: acc,
                                    amount: amount,
                                    debitDimensions: DimensionBag.Empty,
                                    creditDimensions: row.Dimensions);
                            }
                        }

                        await Task.CompletedTask;
                    },
                    manageTransaction: false,
                    ct: innerCt);

                }
                catch (PostingAlreadyInProgressException)
                {
                    throw new FiscalYearClosingAlreadyInProgressException(fiscalYearEndPeriod, documentId);
                }

                if (postingResult == PostingResult.AlreadyCompleted)
                    throw new FiscalYearAlreadyClosedException(fiscalYearEndPeriod, documentId);

                var finalClosedAtUtc = _timeProvider.GetUtcNowDateTime();
                finalClosedAtUtc.EnsureUtc(nameof(finalClosedAtUtc));

                // Business AuditLog (atomic with fiscal-year closing posting)
                await audit.WriteAsync(
                    AuditEntityKind.Period,
                    documentId,
                    AuditActionCodes.PeriodCloseFiscalYear,
                    changes:
                    [
                        AuditLogService.Change("is_fiscal_year_closed", false, true),
                        AuditLogService.Change("fiscal_year_end_period", null, fiscalYearEndPeriod),
                        AuditLogService.Change("retained_earnings_account_id", null, retainedEarningsAccountId),
                        AuditLogService.Change("closing_entries_posted", null, true),
                        AuditLogService.Change("closed_by", null, closedBy),
                        AuditLogService.Change("closed_at_utc", null, finalClosedAtUtc)
                    ],
                    metadata: new { closing_day_utc = closingDay },
                    ct: innerCt);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fiscal year closing failed.");
                throw;
            }
        }, ct);

        RuntimeLog.FiscalYearClosingCompleted(logger);
    }

    private async Task CheckNegativeBalanceAsync(IEnumerable<AccountingBalance> balances, CancellationToken ct)
    {
        // Negative handling
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

        // Logging Warn Policy
        var warns = violations
            .Where(v => v.Policy == NegativeBalancePolicy.Warn)
            .ToList();

        foreach (var w in warns)
        {
            logger.LogWarning(
                "WARN: Negative balance: {WAccountCode} {WAccountName} ({WAccountType}) = {WClosingBalance} period={DateOnly:yyyy-MM-dd}",
                w.AccountCode, w.AccountName, w.AccountType, w.ClosingBalance, w.Period);
        }
    }

    private static bool IsProfitAndLoss(StatementSection section)
        => section is StatementSection.Income
            or StatementSection.Expenses
            or StatementSection.OtherIncome
            or StatementSection.OtherExpense
            or StatementSection.CostOfGoodsSold;

    private async Task LockFiscalYearWindowAsync(
        DateOnly yearStart,
        DateOnly fiscalYearEndPeriod,
        CancellationToken ct)
    {
        for (var p = yearStart; p <= fiscalYearEndPeriod; p = p.AddMonths(1))
        {
            await advisoryLocks.LockPeriodAsync(p, ct);
        }
    }

    private async Task EnsureFiscalYearClosePrerequisitesAsync(
        DateOnly yearStart,
        DateOnly fiscalYearEndPeriod,
        CancellationToken ct)
    {
        // Guard: end period must be open (posting is forbidden into closed periods).
        if (await closedPeriodRepository.IsClosedAsync(fiscalYearEndPeriod, ct))
            throw new PeriodAlreadyClosedException(fiscalYearEndPeriod);

        var chain = await LoadChainSnapshotAsync(ct);
        if (chain is { HasBrokenChain: true, NextClosablePeriod: not null } && chain.NextClosablePeriod.Value <= fiscalYearEndPeriod)
            throw new FiscalYearClosingBlockedByLaterClosedPeriodException(fiscalYearEndPeriod, chain.LatestClosedPeriod!.Value);

        if (chain.LatestClosedPeriod is not null
            && chain.LatestClosedPeriod.Value > fiscalYearEndPeriod
            && chain.ChainStartPeriod is not null
            && fiscalYearEndPeriod >= chain.ChainStartPeriod.Value)
        {
            throw new FiscalYearClosingBlockedByLaterClosedPeriodException(fiscalYearEndPeriod, chain.LatestClosedPeriod.Value);
        }

        // Strict rule: all months BEFORE the fiscal year-end month must already be closed.
        // Closing entries are posted into the open end month; afterward the caller may close that month via IPeriodClosingService.
        for (var p = yearStart; p < fiscalYearEndPeriod; p = p.AddMonths(1))
        {
            if (!await closedPeriodRepository.IsClosedAsync(p, ct))
                throw new FiscalYearClosingPrerequisiteNotMetException(p);
        }
    }

    private async Task<PeriodClosingChainSnapshot> LoadChainSnapshotAsync(CancellationToken ct)
    {
        var earliestActivityPeriod = await activityReader.GetEarliestActivityPeriodAsync(ct);
        var latestClosedPeriod = await closedPeriodReader.GetLatestClosedPeriodAsync(ct);

        if (latestClosedPeriod is null)
            return PeriodClosingChainEvaluator.Build(earliestActivityPeriod, latestClosedPeriod, []);

        var chainStartPeriod = earliestActivityPeriod ?? latestClosedPeriod.Value;
        if (chainStartPeriod > latestClosedPeriod.Value)
            return PeriodClosingChainEvaluator.Build(earliestActivityPeriod, latestClosedPeriod, []);

        var closedRows = await closedPeriodReader.GetClosedAsync(chainStartPeriod, latestClosedPeriod.Value, ct);
        return PeriodClosingChainEvaluator.Build(
            earliestActivityPeriod,
            latestClosedPeriod,
            closedRows.Select(x => x.Period).ToArray());
    }

    private async Task<PostingStateRecord?> TryGetFiscalYearPostingRowAsync(Guid documentId, CancellationToken ct)
    {
        var page = await postingStateReader.GetPageAsync(
            new PostingStatePageRequest
            {
                DocumentId = documentId,
                Operation = PostingOperation.CloseFiscalYear,
                PageSize = 1
            },
            ct);

        return page.Records.SingleOrDefault();
    }

    private async Task<string?> ResolveRetainedEarningsAccountDisplayAsync(Guid accountId, CancellationToken ct)
    {
        var chart = await chartOfAccountsProvider.GetAsync(ct);
        if (chart.TryGet(accountId, out var active) && active is not null)
            return $"{active.Code} — {active.Name}";

        var resolved = await accountByIdResolver.GetByIdAsync(accountId, ct);
        return resolved is null ? null : $"{resolved.Code} — {resolved.Name}";
    }
}
