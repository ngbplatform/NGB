namespace NGB.Trade;

/// <summary>
/// Stable type codes for the Trade vertical.
/// </summary>
public static class TradeCodes
{
    public const string Watchdog = "trd.health";
    public const string BackgroundJobs = "trd.background_jobs";

    public const string Party = "trd.party";
    public const string Item = "trd.item";
    public const string Warehouse = "trd.warehouse";
    public const string UnitOfMeasure = "trd.unit_of_measure";
    public const string PaymentTerms = "trd.payment_terms";
    public const string InventoryAdjustmentReason = "trd.inventory_adjustment_reason";
    public const string PriceType = "trd.price_type";
    public const string AccountingPolicy = "trd.accounting_policy";

    public const string PurchaseReceipt = "trd.purchase_receipt";
    public const string SalesInvoice = "trd.sales_invoice";
    public const string CustomerPayment = "trd.customer_payment";
    public const string VendorPayment = "trd.vendor_payment";
    public const string InventoryTransfer = "trd.inventory_transfer";
    public const string InventoryAdjustment = "trd.inventory_adjustment";
    public const string CustomerReturn = "trd.customer_return";
    public const string VendorReturn = "trd.vendor_return";
    public const string ItemPriceUpdate = "trd.item_price_update";

    public const string InventoryMovementsRegisterCode = "trd.inventory_movements";
    public const string ItemPricesRegisterCode = "trd.item_prices";
    public const string DashboardOverviewReport = "trd.dashboard_overview";
    public const string InventoryBalancesReport = "trd.inventory_balances";
    public const string InventoryMovementsReport = "trd.inventory_movements_report";
    public const string CurrentItemPricesReport = "trd.current_item_prices";
    public const string SalesByItemReport = "trd.sales_by_item";
    public const string SalesByCustomerReport = "trd.sales_by_customer";
    public const string PurchasesByVendorReport = "trd.purchases_by_vendor";

    public const string DefaultCurrency = "USD";
}
