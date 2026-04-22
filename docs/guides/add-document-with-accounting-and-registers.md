---
title: Add a Document with Accounting and Registers
---

# Add a Document with Accounting and Registers

This guide describes the recommended NGB workflow for adding a production-style business document.

The example uses an American-market document: **Sales Invoice**.

The example assumes the document must do all of the following:

- store a document head and line items;
- post accounting entries;
- reduce inventory in an operational register;
- increase customer receivables in an operational register;
- appear correctly in document flow and accounting effects.

<div class="doc-status-row">
  <span class="doc-badge doc-badge-verified">Verified: core runtime and migrator anchors</span>
  <span class="doc-badge doc-badge-template">Template: implementation shape</span>
</div>

<div class="doc-reading-box">
  <p><strong>How to read this page.</strong> Treat the verified anchors as the confirmed platform boundaries, then use the numbered workflow as the recommended vertical implementation pattern.</p>
</div>

## Verified anchors behind this guide

- `NGB.Runtime/Documents/DocumentService.cs`
- `NGB.PropertyManagement.Api/Program.cs`
- `NGB.Migrator.Core/PlatformMigratorCli.cs`
- `docker/pm/migrator/seed-and-migrate.sh`

Everything else on this page is recommended implementation shape consistent with those anchors.

## 1. Start from the business invariant, not from the controller

Before creating files, lock the business rule set.

For a `sales.invoice` document, decide at minimum:

- can it exist as Draft and Posted;
- whether lines are inventory items, service lines, or both;
- whether posting is reversible only through storno/reversal;
- whether inventory and receivable effects are derived from posted lines only;
- whether totals are stored or recalculated.

In NGB, the wrong place to start is the API route. The verified `DocumentService` anchor shows that the universal document path already exists.

## 2. Define the metadata shape

**Template guidance**

Create the document definition and presentation metadata for:

- type code: `sales.invoice`
- display name: `Sales Invoice`
- head fields such as:
  - `customer_id`
  - `invoice_date_utc`
  - `due_on_utc`
  - `warehouse_id`
  - `currency_code`
  - `total_amount`
- part rows such as:
  - `item_id`
  - `description`
  - `quantity`
  - `unit_price`
  - `line_amount`
  - `inventory_cost`

The metadata must be rich enough for the universal document CRUD path to validate payload fields and part rows.

The verified `DocumentService` source shows that:

- head columns are validated against document metadata;
- parts are validated against part-table metadata;
- list filters and presentation fields are read from metadata;
- amount-field presentation is consumed by runtime.

## 3. Add the database schema

**Template guidance with verified migrator boundary**

Create PostgreSQL migrations for:

- typed head table for `sales.invoice`;
- typed part table for `sales.invoice.lines`;
- indexes needed for lookup, list pages, and posting reads;
- any register-specific read-optimized indexes.

The verified migrator anchors confirm that this schema work should enter through the migration packs and be applied through the migrator host rather than through ad hoc SQL execution.

## 4. Make the universal document path able to resolve the type

**Template guidance**

Register the new document type in the vertical module so that runtime can resolve it from `documentType`.

This registration typically needs to make the following available:

- document type metadata;
- head table descriptor;
- part table descriptors;
- optional list filters and presentation options.

The verified `DocumentService` anchor proves that the runtime expects this model to exist before CRUD can work.

## 5. Implement posting behavior

**Template guidance**

For `sales.invoice`, the posting behavior usually needs three effect groups.

### Accounting effects

Typical accounting intent:

- Debit `Accounts Receivable`
- Credit `Revenue`
- Debit `Cost of Goods Sold`
- Credit `Inventory`

### Operational register effects

Typical operational intent:

- inventory on hand decreases by item and warehouse;
- customer open balance increases by customer and invoice.

### Reference-register effects

Not required for this example.

## 6. Recommended posting structure

Use one document-centric posting implementation that reads the fully shaped document and emits all required effects in one posting transaction.

```csharp
// Illustrative example, not a verbatim repository excerpt.
public sealed class SalesInvoicePostingHandler
{
    public async Task PostAsync(DocumentPostingContext ctx, CancellationToken ct)
    {
        var invoice = await LoadInvoiceAggregateAsync(ctx.DocumentId, ct);

        var accounting = BuildAccountingEntries(invoice);
        var inventoryMovements = BuildInventoryMovements(invoice);
        var receivableMovements = BuildCustomerBalanceMovements(invoice);

        await WriteAccountingAsync(accounting, ct);
        await WriteOperationalRegisterAsync(inventoryMovements, ct);
        await WriteOperationalRegisterAsync(receivableMovements, ct);
    }
}
```

The exact interfaces may differ in your vertical, but the important rule stays the same: post the document as a single business effect, not as disconnected sub-operations.

## 7. Connect document effects and explainability

The verified `DocumentService` anchor shows that runtime exposes:

- relationship graph access;
- accounting entries and register effects queries;
- UI effects such as whether the document can be edited, posted, unposted, or reapplied.

That means a production-ready document should not stop at persistence and posting. It should also support:

- document flow relationships;
- accounting effects visibility;
- operational-register effect visibility;
- audit trail visibility.

## 8. Compose the vertical host

**Verified anchor:** `NGB.PropertyManagement.Api/Program.cs`

Once the document is implemented below the host, compose it by ensuring the vertical host registers:

- the runtime module;
- the PostgreSQL module;
- the vertical definition/runtime/provider modules that contain the new document.

Do not add a one-off controller just for the new document type. The universal document path is the intended surface.

## 9. Add seeds when they help developer velocity

If the document is fundamental to the vertical, extend default seeds or demo seeds so developers and reviewers can actually exercise the flow.

The verified bootstrap script shows that Docker startup already expects:

- migrate;
- seed-defaults;
- optional seed-demo.

A document that cannot be exercised from a seeded environment will be slower to validate.

## 10. Testing matrix

At minimum, cover:

- metadata resolution;
- create draft;
- update draft;
- required field validation;
- post;
- unpost or reversal behavior;
- accounting entries correctness;
- inventory operational movements correctness;
- receivable operational movements correctness;
- effects endpoint visibility;
- document flow visibility;
- migration and seed safety.

## 11. Production checklist for `sales.invoice`

Before calling the feature done, confirm all of the following:

- Draft and Posted lifecycle works through the universal path;
- posting is idempotent for repeated commands;
- reversal/unposting rules are explicit;
- storno philosophy is respected;
- inventory and receivable balances are explainable from document effects;
- list and form metadata are UI-ready.

## Read next

- [Platform Extension Points](/guides/platform-extension-points)
- [Runtime Source Map](/platform/runtime-source-map)
