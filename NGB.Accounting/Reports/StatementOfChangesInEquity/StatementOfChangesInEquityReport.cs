namespace NGB.Accounting.Reports.StatementOfChangesInEquity;

public sealed class StatementOfChangesInEquityReport
{
    public required DateOnly FromInclusive { get; init; }
    public required DateOnly ToInclusive { get; init; }

    public required IReadOnlyList<StatementOfChangesInEquityLine> Lines { get; init; }

    public required decimal TotalOpening { get; init; }
    public required decimal TotalChange { get; init; }
    public required decimal TotalClosing { get; init; }
}
