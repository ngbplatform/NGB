---
title: Guide · Item Price Update
---

# Guide · Add an Item Price Update Document

This guide shows how to add an **Item Price Update** document that writes to the Item Pricing Reference Register.

This is a good example of a document that has important business meaning but does **not** necessarily create ledger entries.

## Business meaning

The document says:

> starting on a given date, this item has this price for this price type and currency.

That is durable business meaning and should therefore be modeled as a document, not as a silent edit on the item master record.

## Typical fields

- effective from;
- item;
- price type;
- currency;
- new price;
- optional note or reason.

## Example file layout

```text
NGB.Trade.Contracts/
  Documents/ItemPriceUpdates/
    ItemPriceUpdateDto.cs

NGB.Trade.Definitions/
  Documents/ItemPriceUpdates/
    TrdItemPriceUpdateDocumentDefinition.cs

NGB.Trade.Runtime/
  Documents/ItemPriceUpdates/
    ItemPriceUpdateHandler.cs

NGB.Trade.PostgreSql/
  Bootstrap/db/migrations/
    V2026_04_18_0003__add_trd_item_price_update.sql
```

## DTO example

```csharp
public sealed class ItemPriceUpdateDto
{
    public Guid Id { get; init; }
    public string Number { get; init; } = string.Empty;
    public DateOnly EffectiveFrom { get; init; }
    public Guid ItemId { get; init; }
    public Guid PriceTypeId { get; init; }
    public string CurrencyCode { get; init; } = "USD";
    public decimal Price { get; init; }
}
```

## Definition example

```csharp
public sealed class TrdItemPriceUpdateDocumentDefinition : DocumentDefinition
{
    public const string Code = "trd.item_price_update";

    public override string DocumentCode => Code;
    public override string DisplayName => "Item Price Update";

    public override bool SupportsPosting => true;
}
```

Here “posting” means producing RR movements, not necessarily ledger entries.

## Migration example

```sql
create table if not exists trd_item_price_update
(
    id uuid primary key,
    number text not null,
    effective_from date not null,
    item_id uuid not null,
    price_type_id uuid not null,
    currency_code text not null,
    price numeric(18,4) not null,
    status int not null,
    created_utc timestamp with time zone not null,
    modified_utc timestamp with time zone not null
);
```

## Handler example

```csharp
public sealed class ItemPriceUpdateHandler : IDocumentPostingHandler
{
    public Task<PostingResult> PostAsync(ItemPriceUpdate doc, CancellationToken ct)
    {
        var movement = new ReferenceRegisterMovement
        {
            RegisterCode = "trd.item_price",
            SourceDocumentId = doc.Id,
            EffectiveFrom = doc.EffectiveFrom,
            Dimensions = new Dictionary<string, object?>
            {
                ["item_id"] = doc.ItemId,
                ["price_type_id"] = doc.PriceTypeId,
                ["currency_code"] = doc.CurrencyCode
            },
            Resources = new Dictionary<string, decimal>
            {
                ["price"] = doc.Price
            }
        };

        return Task.FromResult(PostingResult.ReferenceRegisterOnly(movement));
    }
}
```

## Why this should be a document

Making price changes document-driven gives you:

- approval or lifecycle options if needed;
- audit history;
- document flow;
- explainability for future price disputes;
- stable reference-register history.

## Why this should not overwrite item master data

Overwriting the current price on the item row makes it much harder to answer:

- what price was effective last month?
- why did a quote use that price?
- who changed the price?
- what document authorized the change?

The document + RR pattern answers all of those cleanly.

## Checklist

- document definition exists;
- migration exists;
- posting handler appends RR movement;
- current and as-of price reads work;
- Audit Log is visible;
- reports or lookups can use the effective price read path.

This is the standard NGB pattern for date-effective reference-state changes.
