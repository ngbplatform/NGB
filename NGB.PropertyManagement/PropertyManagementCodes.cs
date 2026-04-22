namespace NGB.PropertyManagement;

/// <summary>
/// Stable type codes for the Property Management vertical.
/// </summary>
public static class PropertyManagementCodes
{
    public const string Watchdog = "pm.health";
    public const string BackgroundJobs = "pm.background_jobs";

    public const string Party = "pm.party";
    public const string Property = "pm.property";

    public const string Lease = "pm.lease";

    public const string AccountingPolicy = "pm.accounting_policy";
    public const string BankAccount = "pm.bank_account";
    public const string MaintenanceCategory = "pm.maintenance_category";

    public const string MaintenanceRequest = "pm.maintenance_request";
    public const string WorkOrder = "pm.work_order";
    public const string WorkOrderCompletion = "pm.work_order_completion";
    public const string MaintenanceQueue = "pm.maintenance.queue";
    public const string TenantStatement = "pm.tenant.statement";
    
    public const string TenantBalancesRegisterCode = "pm.tenant_balances";

    // Receivable vs payable charge type catalogs are intentionally separate for clarity and symmetry.
    public const string ReceivableChargeType = "pm.receivable_charge_type";
    public const string PayableChargeType = "pm.payable_charge_type";

    public const string RentCharge = "pm.rent_charge";
    public const string ReceivableCharge = "pm.receivable_charge";
    public const string PayableCharge = "pm.payable_charge";
    public const string PayablePayment = "pm.payable_payment";
    public const string PayableCreditMemo = "pm.payable_credit_memo";
    public const string PayableApply = "pm.payable_apply";
    public const string LateFeeCharge = "pm.late_fee_charge";
    public const string ReceivablePayment = "pm.receivable_payment";
    public const string ReceivableReturnedPayment = "pm.receivable_returned_payment";
    public const string ReceivableCreditMemo = "pm.receivable_credit_memo";
    public const string ReceivableApply = "pm.receivable_apply";

    // Analytical dimension codes used by open-items registers.
    // ValueId is the open item identifier (typically a document id: charge/credit/apply).
    public const string ReceivableItem = "pm.receivable_item";
    public const string PayableItem = "pm.payable_item";

    public const string ReceivablesOpenItemsRegisterCode = "pm.receivables_open_items";
    public const string PayablesOpenItemsRegisterCode = "pm.payables_open_items";

    public static bool IsChargeLikeDocumentType(string? typeCode)
        => string.Equals(typeCode, ReceivableCharge, StringComparison.OrdinalIgnoreCase)
           || string.Equals(typeCode, RentCharge, StringComparison.OrdinalIgnoreCase)
           || string.Equals(typeCode, LateFeeCharge, StringComparison.OrdinalIgnoreCase);

    public static bool IsReceivableCreditSourceDocumentType(string? typeCode)
        => string.Equals(typeCode, ReceivablePayment, StringComparison.OrdinalIgnoreCase)
           || string.Equals(typeCode, ReceivableCreditMemo, StringComparison.OrdinalIgnoreCase);

    public static bool IsApplyCapableDocumentType(string? typeCode)
        => IsChargeLikeDocumentType(typeCode)
           || IsReceivableCreditSourceDocumentType(typeCode);

    public static bool IsPayableChargeLikeDocumentType(string? typeCode)
        => string.Equals(typeCode, PayableCharge, StringComparison.OrdinalIgnoreCase);

    public static bool IsPayableCreditSourceDocumentType(string? typeCode)
        => string.Equals(typeCode, PayablePayment, StringComparison.OrdinalIgnoreCase)
           || string.Equals(typeCode, PayableCreditMemo, StringComparison.OrdinalIgnoreCase);

    public static bool IsPayablesApplyCapableDocumentType(string? typeCode)
        => IsPayableChargeLikeDocumentType(typeCode)
           || IsPayableCreditSourceDocumentType(typeCode);
}
