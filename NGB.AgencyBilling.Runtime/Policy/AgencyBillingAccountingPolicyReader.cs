using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.AgencyBilling.Runtime.Policy;

public sealed class AgencyBillingAccountingPolicyReader(ICatalogService catalogs)
    : IAgencyBillingAccountingPolicyReader
{
    public async Task<AgencyBillingAccountingPolicy> GetRequiredAsync(CancellationToken ct = default)
    {
        var page = await catalogs.GetPageAsync(
            AgencyBillingCodes.AccountingPolicy,
            new PageRequestDto(Offset: 0, Limit: 2, Search: null),
            ct);

        if (page.Items.Count == 0)
        {
            throw new NgbConfigurationViolationException(
                "Agency Billing accounting policy is not configured.",
                context: new Dictionary<string, object?> { ["catalogType"] = AgencyBillingCodes.AccountingPolicy });
        }

        if (page.Items.Count > 1)
        {
            throw new NgbConfigurationViolationException(
                $"Multiple '{AgencyBillingCodes.AccountingPolicy}' records exist. Expected a single record.",
                context: new Dictionary<string, object?> { ["catalogType"] = AgencyBillingCodes.AccountingPolicy, ["count"] = page.Items.Count });
        }

        var item = page.Items[0];

        Guid GetGuid(string field)
        {
            var fields = item.Payload.Fields;
            if (fields is null || !fields.TryGetValue(field, out var el))
                throw new NgbConfigurationViolationException($"Accounting policy field '{field}' is missing.");

            try
            {
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

        return new AgencyBillingAccountingPolicy(
            PolicyId: item.Id,
            CashAccountId: GetGuid("cash_account_id"),
            AccountsReceivableAccountId: GetGuid("ar_account_id"),
            ServiceRevenueAccountId: GetGuid("service_revenue_account_id"),
            ProjectTimeLedgerOperationalRegisterId: GetGuid("project_time_ledger_register_id"),
            UnbilledTimeOperationalRegisterId: GetGuid("unbilled_time_register_id"),
            ProjectBillingStatusOperationalRegisterId: GetGuid("project_billing_status_register_id"),
            ArOpenItemsOperationalRegisterId: GetGuid("ar_open_items_register_id"));
    }
}
