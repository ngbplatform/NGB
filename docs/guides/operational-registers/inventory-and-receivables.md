---
title: Guide · Inventory and Receivables Operational Registers
---

# Guide · Add Operational Registers for Inventory and Receivables

This guide shows one clean way to add two Operational Registers in an American-market Trade vertical:

1. **Inventory by Warehouse**
2. **Customer Receivables Open Items**

These two examples demonstrate the two most common OR patterns:

- quantity balance register;
- open-item amount register.

## Goal

We want durable operational facts that answer questions such as:

- how many units of an item are on hand in a warehouse?
- how much is still open on a customer invoice?

## Step 1: Define the business question first

### Register A: Inventory by Warehouse

Use this when you need:

- quantity on hand;
- warehouse balances;
- inventory movement traceability.

### Register B: Customer Receivables Open Items

Use this when you need:

- outstanding invoice balance by document;
- aging support;
- payment application traceability.

## Step 2: Choose clear register codes

Example codes:

- `trd.inventory_by_warehouse`
- `trd.customer_receivables_open_items`

The exact format can vary, but keep the code stable and descriptive.

## Step 3: Define dimensions and resources

### Inventory by Warehouse

Dimensions:

- item
- warehouse
- optionally lot, serial, unit of measure

Resources:

- quantity
- optional amount / cost basis

### Customer Receivables Open Items

Dimensions:

- customer
- document
- document date
- due date
- optional business unit

Resources:

- open amount
- open amount in document currency

## Step 4: Create register metadata or definition

Your vertical should declare the register so the platform knows:

- the code;
- dimensions;
- resources;
- read semantics;
- which documents are expected to append movements.

Pseudo-example:

```csharp
public sealed class InventoryByWarehouseRegister : OperationalRegisterDefinition
{
    public const string Code = "trd.inventory_by_warehouse";

    public override string RegisterCode => Code;

    public override IReadOnlyList<DimensionDefinition> Dimensions => new[]
    {
        Dimension.Guid("item_id", "Item"),
        Dimension.Guid("warehouse_id", "Warehouse")
    };

    public override IReadOnlyList<ResourceDefinition> Resources => new[]
    {
        Resource.Decimal("quantity", "Quantity"),
        Resource.Decimal("amount", "Amount")
    };
}
```

## Step 5: Add storage migrations

A simple example movement table for inventory might look like:

```sql
create table if not exists trd_or_inventory_movement
(
    movement_id uuid primary key,
    register_code text not null,
    posting_utc timestamp with time zone not null,
    source_document_id uuid not null,
    item_id uuid not null,
    warehouse_id uuid not null,
    quantity numeric(18,4) not null,
    amount numeric(18,4) not null default 0
);

create index if not exists ix_trd_or_inventory_movement__item_wh_posting
    on trd_or_inventory_movement (item_id, warehouse_id, posting_utc);
```

A receivables movement table may look similar but use customer/document/due-date dimensions.

## Step 6: Decide sign conventions

This is critical.

### Inventory

- inbound quantity = positive
- outbound quantity = negative

### Receivables open items

- invoice open amount = positive
- payment or credit application = negative

If sign conventions are unclear, the register will become hard to debug.

## Step 7: Produce movements from documents

### Sales Invoice

Typically produces:

- negative inventory quantity movements;
- positive receivable open-item movement.

### Customer Payment

Typically produces:

- negative receivable open-item movement when applied.

## Example movement builder

```csharp
public static IReadOnlyList<OperationalRegisterMovement> BuildSalesInvoiceInventoryMovements(SalesInvoice doc)
{
    return doc.Lines.Select(line => new OperationalRegisterMovement
    {
        RegisterCode = "trd.inventory_by_warehouse",
        PostingUtc = doc.PostingUtc,
        SourceDocumentId = doc.Id,
        Dimensions = new Dictionary<string, object?>
        {
            ["item_id"] = line.ItemId,
            ["warehouse_id"] = line.WarehouseId
        },
        Resources = new Dictionary<string, decimal>
        {
            ["quantity"] = -line.Quantity,
            ["amount"] = -line.CostAmount
        }
    }).ToArray();
}
```

## Step 8: Read balances and details

The OR should expose read paths that match the business question.

Examples:

- inventory balance by item and warehouse as of date;
- open receivables by customer as of date;
- detail movements for one document.

## Step 9: Think about finalization and performance

If the register will be read heavily across periods, design for:

- good movement indexes;
- dirty-period tracking if applicable;
- bounded finalization jobs;
- report-friendly read models.

## Checklist

Before considering the register ready, verify:

- sign conventions are documented;
- documents append the correct movements;
- reversal or storno flows are explicit;
- balances reconcile with expected business behavior;
- reports can read the register without UI-side N+1 lookups.

These two examples cover the most important OR patterns you will reuse across real NGB verticals.
