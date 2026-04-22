namespace NGB.AgencyBilling.Contracts;

/// <summary>
/// Outcome of running Agency Billing default setup.
/// </summary>
public sealed record AgencyBillingSetupResult(
    Guid CashAccountId,
    Guid AccountsReceivableAccountId,
    Guid ServiceRevenueAccountId,
    Guid ProjectTimeLedgerOperationalRegisterId,
    Guid UnbilledTimeOperationalRegisterId,
    Guid ProjectBillingStatusOperationalRegisterId,
    Guid ArOpenItemsOperationalRegisterId,
    Guid AccountingPolicyCatalogId,
    bool CreatedCashAccount,
    bool CreatedAccountsReceivableAccount,
    bool CreatedServiceRevenueAccount,
    bool CreatedProjectTimeLedgerOperationalRegister,
    bool CreatedUnbilledTimeOperationalRegister,
    bool CreatedProjectBillingStatusOperationalRegister,
    bool CreatedArOpenItemsOperationalRegister,
    bool CreatedAccountingPolicy);
