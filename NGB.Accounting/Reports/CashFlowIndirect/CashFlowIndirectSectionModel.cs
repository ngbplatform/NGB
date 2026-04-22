using NGB.Accounting.CashFlow;

namespace NGB.Accounting.Reports.CashFlowIndirect;

public sealed class CashFlowIndirectSectionModel
{
    public CashFlowSection Section { get; init; }
    public string Label { get; init; } = string.Empty;
    public IReadOnlyList<CashFlowIndirectLine> Lines { get; init; } = [];
    public decimal Total { get; init; }
}
