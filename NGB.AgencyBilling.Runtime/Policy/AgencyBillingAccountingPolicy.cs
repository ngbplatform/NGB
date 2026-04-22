namespace NGB.AgencyBilling.Runtime.Policy;

public sealed record AgencyBillingAccountingPolicy(
    Guid PolicyId,
    Guid CashAccountId,
    Guid AccountsReceivableAccountId,
    Guid ServiceRevenueAccountId,
    Guid ProjectTimeLedgerOperationalRegisterId,
    Guid UnbilledTimeOperationalRegisterId,
    Guid ProjectBillingStatusOperationalRegisterId,
    Guid ArOpenItemsOperationalRegisterId);
