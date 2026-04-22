namespace NGB.Accounting.Reports.AccountCard;

public sealed class AccountCardReportPage
{
    public Guid AccountId { get; init; }
    public string AccountCode { get; init; } = null!;
    
    public DateOnly FromInclusive { get; init; }
    public DateOnly ToInclusive { get; init; }
    public decimal OpeningBalance { get; init; }
    public decimal TotalDebit { get; init; }
    public decimal TotalCredit { get; init; }
    public decimal ClosingBalance { get; init; }

    public IReadOnlyList<AccountCardReportLine> Lines { get; init; } = [];

    public bool HasMore { get; init; }
    public AccountCardReportCursor? NextCursor { get; init; }
}
