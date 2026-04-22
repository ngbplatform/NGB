namespace NGB.Accounting.Reports.GeneralLedgerAggregated;

public sealed class GeneralLedgerAggregatedReport
{
    public Guid AccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;

    public DateOnly FromInclusive { get; init; }
    public DateOnly ToInclusive { get; init; }

    public decimal OpeningBalance { get; init; }
    public decimal TotalDebit { get; init; }
    public decimal TotalCredit { get; init; }
    public decimal ClosingBalance { get; init; }

    public IReadOnlyList<GeneralLedgerAggregatedReportLine> Lines { get; init; } = [];
}
