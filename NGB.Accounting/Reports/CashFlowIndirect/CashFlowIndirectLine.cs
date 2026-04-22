namespace NGB.Accounting.Reports.CashFlowIndirect;

public sealed class CashFlowIndirectLine
{
    public string LineCode { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public bool IsSynthetic { get; init; }
}
