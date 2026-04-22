namespace NGB.AgencyBilling;

/// <summary>
/// Stable type codes for the Agency Billing vertical.
/// </summary>
public static class AgencyBillingCodes
{
    public const string Watchdog = "ab.health";
    public const string BackgroundJobs = "ab.background_jobs";

    public const string Client = "ab.client";
    public const string TeamMember = "ab.team_member";
    public const string Project = "ab.project";
    public const string RateCard = "ab.rate_card";
    public const string ServiceItem = "ab.service_item";
    public const string PaymentTerms = "ab.payment_terms";
    public const string AccountingPolicy = "ab.accounting_policy";

    public const string ClientContract = "ab.client_contract";
    public const string Timesheet = "ab.timesheet";
    public const string SalesInvoice = "ab.sales_invoice";
    public const string CustomerPayment = "ab.customer_payment";
    public const string GenerateInvoiceDraftDerivation = "ab.generate_invoice_draft";

    public const string ProjectTimeLedgerRegisterCode = "ab.project_time_ledger";
    public const string UnbilledTimeRegisterCode = "ab.unbilled_time";
    public const string ProjectBillingStatusRegisterCode = "ab.project_billing_status";
    public const string ArOpenItemsRegisterCode = "ab.ar_open_items";
    public const string ArOpenItemDimensionCode = "ab.ar_open_item";

    public const string DashboardOverviewReport = "ab.dashboard_overview";
    public const string UnbilledTimeReport = "ab.unbilled_time_report";
    public const string ProjectProfitabilityReport = "ab.project_profitability";
    public const string InvoiceRegisterReport = "ab.invoice_register";
    public const string ArAgingReport = "ab.ar_aging";
    public const string TeamUtilizationReport = "ab.team_utilization";

    public const string DefaultCurrency = "USD";
}
