using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Policy;

public sealed class PropertyManagementAccountingPolicyReader(ICatalogService catalogs)
    : IPropertyManagementAccountingPolicyReader
{
    public async Task<PropertyManagementAccountingPolicy> GetRequiredAsync(CancellationToken ct = default)
    {
        // Single-record policy: read first 2 to detect duplicates.
        var page = await catalogs.GetPageAsync(
            PropertyManagementCodes.AccountingPolicy,
            new PageRequestDto(Offset: 0, Limit: 2, Search: null),
            ct);

        if (page.Items.Count == 0)
        {
            throw new NgbConfigurationViolationException(
                "Property Management accounting policy is not configured.",
                context: new Dictionary<string, object?> { ["catalogType"] = PropertyManagementCodes.AccountingPolicy });
        }

        if (page.Items.Count > 1)
        {
            throw new NgbConfigurationViolationException(
                $"Multiple '{PropertyManagementCodes.AccountingPolicy}' records exist. Expected a single record.",
                context: new Dictionary<string, object?> { ["catalogType"] = PropertyManagementCodes.AccountingPolicy, ["count"] = page.Items.Count });
        }

        var item = page.Items[0];

        Guid GetGuid(string field)
        {
            var fields = item.Payload.Fields;
            if (fields is null || !fields.TryGetValue(field, out var el))
                throw new NgbConfigurationViolationException($"Accounting policy field '{field}' is missing.");

            try
            {
                // UI responses may contain enriched refs: { id, display }.
                return el.ParseGuidOrRef();
            }
            catch (Exception ex)
            {
                throw new NgbConfigurationViolationException(
                    $"Accounting policy field '{field}' is not a valid GUID.",
                    context: new Dictionary<string, object?>
                    {
                        ["field"] = field,
                        ["valueKind"] = el.ValueKind.ToString(),
                        ["error"] = ex.Message
                    });
            }
        }

        return new PropertyManagementAccountingPolicy(
            PolicyId: item.Id,
            CashAccountId: GetGuid("cash_account_id"),
            AccountsReceivableTenantsAccountId: GetGuid("ar_tenants_account_id"),
            AccountsPayableVendorsAccountId: GetGuid("ap_vendors_account_id"),
            RentalIncomeAccountId: GetGuid("rent_income_account_id"),
            LateFeeIncomeAccountId: GetGuid("late_fee_income_account_id"),
            TenantBalancesOperationalRegisterId: GetGuid("tenant_balances_register_id"),
            ReceivablesOpenItemsOperationalRegisterId: GetGuid("receivables_open_items_register_id"),
            PayablesOpenItemsOperationalRegisterId: GetGuid("payables_open_items_register_id"));
    }
}
