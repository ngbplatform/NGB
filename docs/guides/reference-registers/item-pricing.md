---
title: Guide · Item Pricing Reference Register
---

# Guide · Add a Reference Register for Item Pricing

This guide shows how to add a Reference Register for **Item Pricing**.

The business goal is straightforward:

- track prices over time;
- support multiple price types such as Retail and Wholesale;
- preserve pricing history;
- answer “what price was effective on date X?”

This is exactly the kind of problem RR is designed for.

## Target register

Recommended business meaning:

**Item Price**

Recommended code:

```text
trd.item_price
```

## Step 1: Define the dimensions

A good Item Price RR usually uses:

- item;
- price type;
- currency;
- optional sales channel or business unit.

## Step 2: Define the resource

The main resource is:

- price amount

Optional resources:

- minimum quantity;
- discount percentage;
- price basis metadata.

Keep the first version simple.

## Step 3: Define effective-date behavior

This is the heart of the design.

A movement should state that a price becomes effective from a specific date.

That lets the system answer:

- current effective price;
- effective price as of order date;
- price history for audit or support.

## Step 4: Add the definition

Pseudo-example:

```csharp
public sealed class ItemPriceReferenceRegister : ReferenceRegisterDefinition
{
    public const string Code = "trd.item_price";

    public override string RegisterCode => Code;

    public override IReadOnlyList<DimensionDefinition> Dimensions => new[]
    {
        Dimension.Guid("item_id", "Item"),
        Dimension.Guid("price_type_id", "Price Type"),
        Dimension.Text("currency_code", "Currency")
    };

    public override IReadOnlyList<ResourceDefinition> Resources => new[]
    {
        Resource.Decimal("price", "Price")
    };
}
```

## Step 5: Add migration storage

```sql
create table if not exists trd_rr_item_price_movement
(
    movement_id uuid primary key,
    register_code text not null,
    source_document_id uuid not null,
    effective_from date not null,
    item_id uuid not null,
    price_type_id uuid not null,
    currency_code text not null,
    price numeric(18,4) not null,
    created_utc timestamp with time zone not null
);

create index if not exists ix_trd_rr_item_price_movement__lookup
    on trd_rr_item_price_movement (item_id, price_type_id, currency_code, effective_from desc);
```

## Step 6: Create the producing document

The standard business document for this register is usually something like:

- `Item Price Update`

That document becomes the source of truth for pricing changes.

## Step 7: Append movements from the document

```csharp
public static ReferenceRegisterMovement BuildPriceMovement(ItemPriceUpdate doc)
{
    return new ReferenceRegisterMovement
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
}
```

## Step 8: Resolve the effective price

The main read behavior is:

- latest movement by `effective_from` that is less than or equal to the requested as-of date.

That logic belongs in the RR read path, not scattered across UI code.

## Step 9: Think about supersession, not overwrite

When the price changes, do not edit the prior movement row.

Append a new movement with a later effective date.

That preserves historical truth and lets old reports or documents explain why a specific price was used at that time.

## Checklist

Before calling the RR complete, verify:

- the register definition exists;
- the migration is applied;
- the Item Price Update document appends movements;
- current and as-of lookups work;
- pricing history remains visible and auditable.

Pricing is one of the best examples of why Reference Registers belong in NGB as a first-class platform concept.
