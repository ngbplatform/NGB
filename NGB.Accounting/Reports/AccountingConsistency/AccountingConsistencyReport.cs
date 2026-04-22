namespace NGB.Accounting.Reports.AccountingConsistency;

public sealed class AccountingConsistencyReport
{
    public DateOnly Period { get; init; }

    /// <summary>
    /// Optional previous period used for chain validation (month-start DateOnly).
    /// </summary>
    public DateOnly? PreviousPeriodForChainCheck { get; init; }

    /// <summary>
    /// Number of rows where stored turnovers differ from register aggregation for the period.
    /// Returned by DB-level diagnostic.
    /// </summary>
    public long TurnoversVsRegisterDiffCount { get; init; }

    /// <summary>
    /// Number of balance keys where expected closing != stored closing for the period.
    /// </summary>
    public long BalanceVsTurnoverMismatchCount { get; init; }

    /// <summary>
    /// Number of keys where opening(current) != closing(previous) for the chain check.
    /// </summary>
    public long BalanceChainMismatchCount { get; init; }

    /// <summary>
    /// Number of turnover keys that exist without a corresponding balance row for the period.
    /// Useful signal when balances are expected to be materialized for the period.
    /// </summary>
    public long MissingKeyCount { get; init; }

    public IReadOnlyList<AccountingConsistencyIssue> Issues { get; init; } = [];

    public bool IsOk =>
        TurnoversVsRegisterDiffCount == 0
        && BalanceVsTurnoverMismatchCount == 0
        && BalanceChainMismatchCount == 0
        && MissingKeyCount == 0
        && Issues.Count == 0;
}
