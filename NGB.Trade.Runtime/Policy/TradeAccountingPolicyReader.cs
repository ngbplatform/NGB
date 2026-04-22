using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Trade.Runtime.Policy;

public sealed class TradeAccountingPolicyReader(ICatalogService catalogs)
    : ITradeAccountingPolicyReader
{
    public async Task<TradeAccountingPolicy> GetRequiredAsync(CancellationToken ct = default)
    {
        var page = await catalogs.GetPageAsync(
            TradeCodes.AccountingPolicy,
            new PageRequestDto(Offset: 0, Limit: 2, Search: null),
            ct);

        if (page.Items.Count == 0)
        {
            throw new NgbConfigurationViolationException(
                "Trade accounting policy is not configured.",
                context: new Dictionary<string, object?> { ["catalogType"] = TradeCodes.AccountingPolicy });
        }

        if (page.Items.Count > 1)
        {
            throw new NgbConfigurationViolationException(
                $"Multiple '{TradeCodes.AccountingPolicy}' records exist. Expected a single record.",
                context: new Dictionary<string, object?> { ["catalogType"] = TradeCodes.AccountingPolicy, ["count"] = page.Items.Count });
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

        return new TradeAccountingPolicy(
            PolicyId: item.Id,
            CashAccountId: GetGuid("cash_account_id"),
            AccountsReceivableAccountId: GetGuid("ar_account_id"),
            InventoryAccountId: GetGuid("inventory_account_id"),
            AccountsPayableAccountId: GetGuid("ap_account_id"),
            SalesRevenueAccountId: GetGuid("sales_revenue_account_id"),
            CostOfGoodsSoldAccountId: GetGuid("cogs_account_id"),
            InventoryAdjustmentAccountId: GetGuid("inventory_adjustment_account_id"),
            InventoryMovementsRegisterId: GetGuid("inventory_movements_register_id"),
            ItemPricesRegisterId: GetGuid("item_prices_register_id"));
    }
}
