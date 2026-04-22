namespace NGB.Runtime.Periods;

public interface IPeriodClosingService
{
    /// <summary>
    /// Close an accounting month.
    ///
    /// Contract notes:
    /// - <paramref name="period"/> is normalized to month start (YYYY-MM-01).
    /// - The month is marked as closed (and further writes into it are forbidden by runtime/DB guards).
    /// - The operation is atomic (balances + closed marker + audit log) and protected by a period advisory lock.
    ///
    /// See <c>Periods/PeriodClosing.Contract.md</c> for the full platform contract.
    /// </summary>
    Task CloseMonthAsync(DateOnly period, string closedBy, CancellationToken ct = default);

    /// <summary>
    /// Reopen the latest closed accounting month.
    ///
    /// Contract notes:
    /// - <paramref name="period"/> is normalized to month start (YYYY-MM-01).
    /// - Only the latest closed month can be reopened.
    /// - Reopen is forbidden when a fiscal-year close is already recorded for the same month.
    /// - The operation is atomic (closed marker delete + audit log) and protected by a period advisory lock.
    /// </summary>
    Task ReopenMonthAsync(DateOnly period, string reopenedBy, string reason, CancellationToken ct = default);

    /// <summary>
    /// Close a fiscal year by posting closing entries for all P&amp;L accounts into the open end period month.
    ///
    /// Contract notes:
    /// - <paramref name="fiscalYearEndPeriod"/> is a month start (YYYY-MM-01) and MUST be OPEN.
    /// - All prior months of the same year (Jan..month-1) must already be closed.
    /// - The operation is idempotent (deterministic documentId + posting_log) and protected by an end-period lock.
    /// - Dimension policy: closing is performed per Trial Balance row (account + dimension set); retained earnings uses empty dimensions.
    ///
    /// See <c>Periods/PeriodClosing.Contract.md</c> for the full platform contract.
    /// </summary>
    Task CloseFiscalYearAsync(
        DateOnly fiscalYearEndPeriod,
        Guid retainedEarningsAccountId,
        string closedBy,
        CancellationToken ct = default);

    /// <summary>
    /// Reopens a previously completed fiscal-year close so it may be executed again.
    ///
    /// Contract notes:
    /// - <paramref name="fiscalYearEndPeriod"/> is normalized to month start (YYYY-MM-01).
    /// - Reopen is explicit, audited, and reason-based.
    /// - Reopen is blocked if any later month is already closed because those balances would depend on the closing entries.
    /// - If the end period month is closed, it is reopened atomically as part of the operation.
    /// - The operation clears only mutable current state; immutable posting/audit history is preserved.
    /// </summary>
    Task ReopenFiscalYearAsync(
        DateOnly fiscalYearEndPeriod,
        string reopenedBy,
        string reason,
        CancellationToken ct = default);
}
