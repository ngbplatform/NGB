namespace NGB.Accounting.Reports.AccountingConsistency;

public enum AccountingConsistencyIssueKind
{
    TurnoversVsRegisterMismatch = 1,
    BalanceVsTurnoverMismatch = 2,
    BalanceChainMismatch = 3,
    /// <summary>
    /// Backwards-compatible alias for <see cref="BalanceChainMismatch"/>.
    /// </summary>
    ClosedPeriodChainBroken = BalanceChainMismatch,

    MissingKey = 4
}

public sealed class AccountingConsistencyIssue
{
    public AccountingConsistencyIssueKind Kind { get; init; }

    public DateOnly Period { get; init; }
    public DateOnly? PreviousPeriod { get; init; }

    public Guid? AccountId { get; init; }
    public string? AccountCode { get; init; }

    public Guid? DimensionSetId { get; init; }

    public string Message { get; init; } = null!;
}
