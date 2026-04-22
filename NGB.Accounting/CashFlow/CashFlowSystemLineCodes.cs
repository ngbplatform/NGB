namespace NGB.Accounting.CashFlow;

public static class CashFlowSystemLineCodes
{
    public const string WorkingCapitalAccountsReceivable = "op_wc_accounts_receivable";
    public const string WorkingCapitalAccountsPayable = "op_wc_accounts_payable";
    public const string WorkingCapitalInventory = "op_wc_inventory";
    public const string WorkingCapitalPrepaids = "op_wc_prepaids";
    public const string WorkingCapitalOtherCurrentAssets = "op_wc_other_current_assets";
    public const string WorkingCapitalAccruedLiabilities = "op_wc_accrued_liabilities";
    public const string WorkingCapitalOtherCurrentLiabilities = "op_wc_other_current_liabilities";

    public const string OperatingAdjustmentDepreciationAmortization = "op_adjust_depreciation_amortization";
    public const string OperatingAdjustmentNonCashGainsLosses = "op_adjust_noncash_gains_losses";
    public const string OperatingAdjustmentOtherNonCash = "op_adjust_other_noncash";

    public const string InvestingPropertyEquipmentNet = "inv_property_equipment_net";
    public const string InvestingIntangiblesNet = "inv_intangibles_net";
    public const string InvestingInvestmentsNet = "inv_investments_net";
    public const string InvestingLoansReceivableNet = "inv_loans_receivable_net";
    public const string InvestingOtherNet = "inv_other_net";

    public const string FinancingOwnerEquityNet = "fin_owner_equity_net";
    public const string FinancingDistributionsNet = "fin_distributions_net";
    public const string FinancingDebtNet = "fin_debt_net";
    public const string FinancingOtherNet = "fin_other_net";
}
