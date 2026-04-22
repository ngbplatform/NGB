namespace NGB.Accounting.CashFlow;

public sealed record CashFlowLineDefinition(
    string LineCode,
    CashFlowMethod Method,
    CashFlowSection Section,
    string Label,
    int SortOrder,
    bool IsSystem);
