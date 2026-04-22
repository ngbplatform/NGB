namespace NGB.PropertyManagement.Contracts;

/// <summary>
/// Outcome of running PM default setup.
/// </summary>
public sealed record PropertyManagementSetupResult(
    Guid CashAccountId,
    Guid DefaultBankAccountCatalogId,
    Guid AccountsReceivableTenantsAccountId,
    Guid AccountsPayableVendorsAccountId,
    Guid RentalIncomeAccountId,
    Guid LateFeeIncomeAccountId,
    Guid PayablesOpenItemsOperationalRegisterId,
    Guid TenantBalancesOperationalRegisterId,
    Guid ReceivablesOpenItemsOperationalRegisterId,
    Guid AccountingPolicyCatalogId,
    bool CreatedCashAccount,
    bool CreatedDefaultBankAccount,
    bool CreatedAccountsReceivableTenants,
    bool CreatedAccountsPayableVendors,
    bool CreatedRentalIncome,
    bool CreatedLateFeeIncome,
    bool CreatedPayablesOpenItemsOperationalRegister,
    bool CreatedTenantBalancesOperationalRegister,
    bool CreatedReceivablesOpenItemsOperationalRegister,
    bool CreatedAccountingPolicy);
