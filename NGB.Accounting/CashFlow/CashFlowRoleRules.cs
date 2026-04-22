namespace NGB.Accounting.CashFlow;

public static class CashFlowRoleRules
{
    public static bool RequiresLineCode(CashFlowRole role)
        => role is CashFlowRole.WorkingCapital
            or CashFlowRole.NonCashOperatingAdjustment
            or CashFlowRole.InvestingCounterparty
            or CashFlowRole.FinancingCounterparty;

    public static bool ForbidsLineCode(CashFlowRole role) => role is CashFlowRole.None or CashFlowRole.CashEquivalent;

    public static bool SupportsLineCode(CashFlowRole role) => !ForbidsLineCode(role);

    public static IReadOnlyList<CashFlowRole> GetAllowedRoles(CashFlowLineDefinition line)
        => line.Section switch
        {
            CashFlowSection.Operating when line.LineCode.StartsWith("op_wc_", StringComparison.Ordinal)
                => [CashFlowRole.WorkingCapital],
            CashFlowSection.Operating
                => [CashFlowRole.NonCashOperatingAdjustment],
            CashFlowSection.Investing
                => [CashFlowRole.InvestingCounterparty],
            CashFlowSection.Financing
                => [CashFlowRole.FinancingCounterparty],
            _ => []
        };
}
