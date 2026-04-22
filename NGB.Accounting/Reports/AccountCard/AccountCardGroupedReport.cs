using NGB.Core.Dimensions;

namespace NGB.Accounting.Reports.AccountCard;

public sealed class AccountCardGroupedReport
{
    public Guid AccountId { get; init; }
    public string AccountCode { get; init; } = null!;
    public DimensionScopeBag? DimensionScopes { get; init; }

    public DateOnly FromInclusive { get; init; }
    public DateOnly ToInclusive { get; init; }

    public AccountCardGrouping Grouping { get; init; }

    public decimal OpeningBalance { get; init; }
    public decimal TotalDebit { get; init; }
    public decimal TotalCredit { get; init; }
    public decimal ClosingBalance { get; init; }

    public IReadOnlyList<AccountCardReportSection> Sections { get; init; } = [];
}
