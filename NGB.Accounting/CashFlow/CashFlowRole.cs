using System.ComponentModel.DataAnnotations;

namespace NGB.Accounting.CashFlow;

/// <summary>
/// Account-level cash-flow classification used by indirect-method cash flow reporting.
///
/// IMPORTANT:
/// - This metadata controls report semantics, not posting semantics.
/// - "Counterparty" roles are used only when a real cash account appears on the other side of a register row.
/// </summary>
public enum CashFlowRole : short
{
    [Display(Name = "None")]
    None = 0,
    
    [Display(Name = "Cash equivalent")]
    CashEquivalent = 1,
    
    [Display(Name = "Working capital")]
    WorkingCapital = 2,
    
    [Display(Name = "Non-cash operating adjustment")]
    NonCashOperatingAdjustment = 3,
    
    [Display(Name = "Investing counterparty")]
    InvestingCounterparty = 4,
    
    [Display(Name = "Financing counterparty")]
    FinancingCounterparty = 5
}
