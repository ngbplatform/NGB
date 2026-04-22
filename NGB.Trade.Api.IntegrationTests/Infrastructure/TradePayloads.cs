using System.Text.Json;
using NGB.Contracts.Common;

namespace NGB.Trade.Api.IntegrationTests.Infrastructure;

internal static class TradePayloads
{
    public readonly record struct PurchaseReceiptLineRow(
        int Ordinal,
        Guid ItemId,
        decimal Quantity,
        decimal UnitCost,
        decimal LineAmount);

    public readonly record struct SalesInvoiceLineRow(
        int Ordinal,
        Guid ItemId,
        decimal Quantity,
        decimal UnitPrice,
        decimal UnitCost,
        decimal LineAmount);

    public readonly record struct InventoryTransferLineRow(
        int Ordinal,
        Guid ItemId,
        decimal Quantity);

    public readonly record struct InventoryAdjustmentLineRow(
        int Ordinal,
        Guid ItemId,
        decimal QuantityDelta,
        decimal UnitCost,
        decimal LineAmount);

    public readonly record struct CustomerReturnLineRow(
        int Ordinal,
        Guid ItemId,
        decimal Quantity,
        decimal UnitPrice,
        decimal UnitCost,
        decimal LineAmount);

    public readonly record struct VendorReturnLineRow(
        int Ordinal,
        Guid ItemId,
        decimal Quantity,
        decimal UnitCost,
        decimal LineAmount);

    public readonly record struct ItemPriceUpdateLineRow(
        int Ordinal,
        Guid ItemId,
        Guid PriceTypeId,
        string Currency,
        decimal UnitPrice);

    public static RecordPayload Payload(object fields, IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
    {
        var element = JsonSerializer.SerializeToElement(fields);
        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in element.EnumerateObject())
            dict[property.Name] = property.Value;

        return new RecordPayload(dict, parts);
    }

    public static IReadOnlyDictionary<string, RecordPartPayload> PurchaseReceiptLines(params PurchaseReceiptLineRow[] rows)
    {
        var list = new List<IReadOnlyDictionary<string, JsonElement>>(rows.Length);

        foreach (var row in rows)
        {
            list.Add(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ordinal"] = JsonSerializer.SerializeToElement(row.Ordinal),
                ["item_id"] = JsonSerializer.SerializeToElement(row.ItemId),
                ["quantity"] = JsonSerializer.SerializeToElement(row.Quantity),
                ["unit_cost"] = JsonSerializer.SerializeToElement(row.UnitCost),
                ["line_amount"] = JsonSerializer.SerializeToElement(row.LineAmount)
            });
        }

        return new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
        {
            ["lines"] = new RecordPartPayload(list)
        };
    }

    public static IReadOnlyDictionary<string, RecordPartPayload> SalesInvoiceLines(params SalesInvoiceLineRow[] rows)
    {
        var list = new List<IReadOnlyDictionary<string, JsonElement>>(rows.Length);

        foreach (var row in rows)
        {
            list.Add(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ordinal"] = JsonSerializer.SerializeToElement(row.Ordinal),
                ["item_id"] = JsonSerializer.SerializeToElement(row.ItemId),
                ["quantity"] = JsonSerializer.SerializeToElement(row.Quantity),
                ["unit_price"] = JsonSerializer.SerializeToElement(row.UnitPrice),
                ["unit_cost"] = JsonSerializer.SerializeToElement(row.UnitCost),
                ["line_amount"] = JsonSerializer.SerializeToElement(row.LineAmount)
            });
        }

        return new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
        {
            ["lines"] = new(list)
        };
    }

    public static IReadOnlyDictionary<string, RecordPartPayload> InventoryTransferLines(
        params InventoryTransferLineRow[] rows)
    {
        var list = new List<IReadOnlyDictionary<string, JsonElement>>(rows.Length);

        foreach (var row in rows)
        {
            list.Add(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ordinal"] = JsonSerializer.SerializeToElement(row.Ordinal),
                ["item_id"] = JsonSerializer.SerializeToElement(row.ItemId),
                ["quantity"] = JsonSerializer.SerializeToElement(row.Quantity)
            });
        }

        return new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
        {
            ["lines"] = new(list)
        };
    }

    public static IReadOnlyDictionary<string, RecordPartPayload> InventoryAdjustmentLines(
        params InventoryAdjustmentLineRow[] rows)
    {
        var list = new List<IReadOnlyDictionary<string, JsonElement>>(rows.Length);

        foreach (var row in rows)
        {
            list.Add(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ordinal"] = JsonSerializer.SerializeToElement(row.Ordinal),
                ["item_id"] = JsonSerializer.SerializeToElement(row.ItemId),
                ["quantity_delta"] = JsonSerializer.SerializeToElement(row.QuantityDelta),
                ["unit_cost"] = JsonSerializer.SerializeToElement(row.UnitCost),
                ["line_amount"] = JsonSerializer.SerializeToElement(row.LineAmount)
            });
        }

        return new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
        {
            ["lines"] = new(list)
        };
    }

    public static IReadOnlyDictionary<string, RecordPartPayload> CustomerReturnLines(
        params CustomerReturnLineRow[] rows)
    {
        var list = new List<IReadOnlyDictionary<string, JsonElement>>(rows.Length);

        foreach (var row in rows)
        {
            list.Add(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ordinal"] = JsonSerializer.SerializeToElement(row.Ordinal),
                ["item_id"] = JsonSerializer.SerializeToElement(row.ItemId),
                ["quantity"] = JsonSerializer.SerializeToElement(row.Quantity),
                ["unit_price"] = JsonSerializer.SerializeToElement(row.UnitPrice),
                ["unit_cost"] = JsonSerializer.SerializeToElement(row.UnitCost),
                ["line_amount"] = JsonSerializer.SerializeToElement(row.LineAmount)
            });
        }

        return new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
        {
            ["lines"] = new(list)
        };
    }

    public static IReadOnlyDictionary<string, RecordPartPayload> VendorReturnLines(params VendorReturnLineRow[] rows)
    {
        var list = new List<IReadOnlyDictionary<string, JsonElement>>(rows.Length);

        foreach (var row in rows)
        {
            list.Add(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ordinal"] = JsonSerializer.SerializeToElement(row.Ordinal),
                ["item_id"] = JsonSerializer.SerializeToElement(row.ItemId),
                ["quantity"] = JsonSerializer.SerializeToElement(row.Quantity),
                ["unit_cost"] = JsonSerializer.SerializeToElement(row.UnitCost),
                ["line_amount"] = JsonSerializer.SerializeToElement(row.LineAmount)
            });
        }

        return new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
        {
            ["lines"] = new RecordPartPayload(list)
        };
    }

    public static IReadOnlyDictionary<string, RecordPartPayload> ItemPriceUpdateLines(params ItemPriceUpdateLineRow[] rows)
    {
        var list = new List<IReadOnlyDictionary<string, JsonElement>>(rows.Length);

        foreach (var row in rows)
        {
            list.Add(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ordinal"] = JsonSerializer.SerializeToElement(row.Ordinal),
                ["item_id"] = JsonSerializer.SerializeToElement(row.ItemId),
                ["price_type_id"] = JsonSerializer.SerializeToElement(row.PriceTypeId),
                ["currency"] = JsonSerializer.SerializeToElement(row.Currency),
                ["unit_price"] = JsonSerializer.SerializeToElement(row.UnitPrice)
            });
        }

        return new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
        {
            ["lines"] = new RecordPartPayload(list)
        };
    }
}
