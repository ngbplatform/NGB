namespace NGB.Accounting.Reports.CashFlowIndirect;

public sealed class CashFlowIndirectReport
{
    public DateOnly FromInclusive { get; init; }
    public DateOnly ToInclusive { get; init; }
    public IReadOnlyList<CashFlowIndirectSectionModel> Sections { get; init; } = [];
    public decimal BeginningCash { get; init; }
    public decimal NetIncreaseDecreaseInCash { get; init; }
    public decimal EndingCash { get; init; }
}
