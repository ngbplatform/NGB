namespace NGB.PropertyManagement.Runtime.Policy;

public interface IPropertyManagementAccountingPolicyReader
{
    Task<PropertyManagementAccountingPolicy> GetRequiredAsync(CancellationToken ct = default);
}

public sealed record PropertyManagementAccountingPolicy(
    Guid PolicyId,
    Guid CashAccountId,
    Guid AccountsReceivableTenantsAccountId,
    Guid AccountsPayableVendorsAccountId,
    Guid RentalIncomeAccountId,
    Guid LateFeeIncomeAccountId,
    Guid TenantBalancesOperationalRegisterId,
    Guid ReceivablesOpenItemsOperationalRegisterId,
    Guid PayablesOpenItemsOperationalRegisterId);
