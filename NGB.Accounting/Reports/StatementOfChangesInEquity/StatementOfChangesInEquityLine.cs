namespace NGB.Accounting.Reports.StatementOfChangesInEquity;

public sealed class StatementOfChangesInEquityLine
{
    public required Guid AccountId { get; init; }
    public required string ComponentCode { get; init; }
    public required string ComponentName { get; init; }
    public required bool IsSynthetic { get; init; }

    public required decimal OpeningAmount { get; init; }
    public required decimal ChangeAmount { get; init; }
    public required decimal ClosingAmount { get; init; }
}
