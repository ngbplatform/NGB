namespace NGB.AgencyBilling.Derivations;

public sealed record AgencyBillingInvoiceDraftDefaults(
    Guid ContractId,
    string CurrencyCode,
    string? InvoiceMemo,
    int DueDays);
