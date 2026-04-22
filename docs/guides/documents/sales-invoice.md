---
title: Guide · Sales Invoice
---

# Guide · Add a Sales Invoice Document

This guide shows one clean way to add a **Sales Invoice** document in a Trade-style vertical.

The example uses American-market terminology and demonstrates the full business pattern:

- document definition;
- accounting posting;
- Operational Register movements for inventory;
- Operational Register movements for customer receivables;
- Document Flow and explainability.

## Business meaning

A Sales Invoice represents a posted sale to a customer.

Typical consequences:

- revenue recognition;
- inventory outflow;
- cost recognition;
- customer receivable creation.

## Step 1: Decide the document boundary

A good Sales Invoice usually owns:

- invoice number;
- invoice date;
- customer;
- currency;
- warehouse or fulfillment location where relevant;
- line items;
- totals;
- posting status.

It should not directly store derived open balances as mutable current-state fields. Those should come from OR or accounting views.

## Step 2: Example file layout

```text
NGB.Trade.Contracts/
  Documents/SalesInvoices/
    SalesInvoiceDto.cs
    SalesInvoiceLineDto.cs

NGB.Trade.Definitions/
  Documents/SalesInvoices/
    TrdSalesInvoiceDocumentDefinition.cs

NGB.Trade.Runtime/
  Documents/SalesInvoices/
    SalesInvoicePostingHandler.cs
    SalesInvoiceValidator.cs

NGB.Trade.PostgreSql/
  Bootstrap/db/migrations/
    V2026_04_18_0002__add_trd_sales_invoice.sql
```

## Step 3: Define the payload

```csharp
public sealed class SalesInvoiceDto
{
    public Guid Id { get; init; }
    public string Number { get; init; } = string.Empty;
    public DateOnly InvoiceDate { get; init; }
    public Guid CustomerId { get; init; }
    public string CurrencyCode { get; init; } = "USD";
    public Guid WarehouseId { get; init; }
    public IReadOnlyList<SalesInvoiceLineDto> Lines { get; init; } = [];
}

public sealed class SalesInvoiceLineDto
{
    public Guid ItemId { get; init; }
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal CostAmount { get; init; }
    public decimal LineAmount => Quantity * UnitPrice;
}
```

## Step 4: Add the document definition

The definition should declare:

- document code;
- display label;
- fields and parts;
- allowed actions;
- posting capability.

```csharp
public sealed class TrdSalesInvoiceDocumentDefinition : DocumentDefinition
{
    public const string Code = "trd.sales_invoice";

    public override string DocumentCode => Code;
    public override string DisplayName => "Sales Invoice";

    public override bool SupportsPosting => true;
}
```

## Step 5: Add the migration

```sql
create table if not exists trd_sales_invoice
(
    id uuid primary key,
    number text not null,
    invoice_date date not null,
    customer_id uuid not null,
    warehouse_id uuid not null,
    currency_code text not null,
    status int not null,
    created_utc timestamp with time zone not null,
    modified_utc timestamp with time zone not null
);

create table if not exists trd_sales_invoice_line
(
    id uuid primary key,
    sales_invoice_id uuid not null references trd_sales_invoice(id),
    line_no int not null,
    item_id uuid not null,
    quantity numeric(18,4) not null,
    unit_price numeric(18,4) not null,
    cost_amount numeric(18,4) not null
);
```

## Step 6: Validate before posting

Typical validation rules include:

- document is still Draft;
- customer exists and is active;
- warehouse exists;
- at least one line exists;
- quantities and prices are valid;
- posting period is open;
- inventory policy requirements are satisfied.

## Step 7: Build accounting entries

A simple posting meaning may be:

- Dr Accounts Receivable
- Cr Product Revenue
- Dr Cost of Goods Sold
- Cr Inventory

```csharp
public sealed class SalesInvoicePostingHandler : IDocumentPostingHandler
{
    public Task<PostingResult> PostAsync(SalesInvoice doc, CancellationToken ct)
    {
        var totalRevenue = doc.Lines.Sum(x => x.LineAmount);
        var totalCost = doc.Lines.Sum(x => x.CostAmount);

        var entries = new[]
        {
            Entry.Debit("1100", totalRevenue, "Accounts Receivable"),
            Entry.Credit("4000", totalRevenue, "Product Revenue"),
            Entry.Debit("5000", totalCost, "Cost of Goods Sold"),
            Entry.Credit("1200", totalCost, "Inventory")
        };

        // OR movement creation omitted here for clarity.
        return Task.FromResult(new PostingResult(entries));
    }
}
```

## Step 8: Append Operational Register movements

### Inventory by Warehouse

For each line:

- quantity = negative
- amount = negative cost amount

### Customer Receivables Open Items

For the document total:

- open amount = positive

This is what lets the system answer both inventory and open-item questions without forcing every read through raw documents.

## Step 9: Wire explainability surfaces

A posted Sales Invoice should show:

- Accounting Effects;
- Document Flow;
- Audit Log;
- operational consequences in downstream reports.

## Step 10: Think about reversals

A correction path should not silently edit the posted effects.

Use one of these patterns:

- reversal / storno document;
- credit memo;
- controlled repost flow where supported.

## Result

When this document is implemented correctly, the platform can explain:

- the original operational event;
- the accounting impact;
- the inventory impact;
- the customer receivable impact;
- downstream relationships and settlements.

## Final checklist

- definition exists;
- migration exists;
- posting validator exists;
- posting handler appends entries and movements;
- Audit Log is visible;
- Document Flow links future payment or credit documents;
- reports can drill into the document.

This is the model to follow for production-ready operational-plus-accounting documents in NGB.
