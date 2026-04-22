---
title: Operational Registers
---

# Operational Registers

Operational Registers are one of the platform’s core business abstractions.

They are used when the system needs to maintain durable, queryable operational facts and balances that are not just accounting ledger entries.

Examples include:

- inventory by warehouse;
- customer receivable open items;
- vendor payable open items;
- project hours;
- service capacity consumption;
- quantity on order.

## Why Operational Registers exist

Business systems often need projections like:

- “how many units are on hand in Warehouse A?”
- “what amount remains unpaid on invoice SI-000123?”
- “what was the balance for this register as of month-end?”
- “what was the turnover during March?”

Those questions are not always best answered from raw document tables or directly from the general ledger.

Operational Registers exist to make those operational balances explicit, durable, and queryable.

## Core idea

An Operational Register is built from **append-only movements**.

A movement says that some business event changed operational state.

For example:

- a purchase receipt adds quantity to inventory;
- a sales invoice removes quantity from inventory;
- a customer invoice creates an open receivable;
- a payment reduces an open receivable.

The register engine then supports balance and turnover style reads from those movement streams.

## Common shape of an OR

An OR usually has:

- a **register code**;
- a set of **dimensions** that identify the slice of business state;
- one or more **resources** that change over time;
- append-only movements with sign and period;
- read paths for balances, turnovers, or details.

### Example: inventory by warehouse

Dimensions:

- item;
- warehouse;
- lot or unit of measure if needed.

Resources:

- quantity;
- optionally cost amount.

### Example: receivables open items

Dimensions:

- party / customer;
- document;
- property / location / business unit if needed.

Resources:

- open amount;
- open amount in document currency;
- due date attributes for aging logic.

## Why OR is not the same as Accounting

Accounting tells the financial story.

Operational Registers tell the operational story.

Sometimes those stories overlap, but they are not interchangeable.

Example:

- Accounting can tell you total Accounts Receivable.
- An Operational Register can tell you which exact customer document still has an open balance and when it is due.

That distinction is why both systems belong in the platform.

## Append-only model

OR movements are append-only. The register is not updated by overwriting a current-balance row as the primary truth.

Instead, the durable truth is the movement stream. Any summaries or finalized balances exist to make reads efficient and deterministic, not to replace the source-of-truth movement history.

## Finalization and dirty months

For time-sensitive register reads, the platform supports the idea of dirty periods and finalization.

That means:

- new movements can mark a month or register slice as needing recalculation;
- a bounded background process can finalize that work;
- reads can use finalized state where appropriate.

This design is important for production scale because it avoids full recomputation on every read.

## Design rules for a new OR

When adding a new Operational Register, define the following clearly:

1. what business question the register answers;
2. what the dimensions are;
3. what the resources are;
4. what movement sign conventions are used;
5. what documents produce movements;
6. how reversals or corrections work;
7. what the main read patterns are.

If those decisions are vague, the register will become difficult to extend or explain.

## When to use an OR

Use an OR when you need one or more of these:

- operational balances;
- turnover by period;
- open-item state;
- detail traceability from documents to operational consequences;
- state that is not naturally or sufficiently represented by the general ledger.

## When not to use an OR

Do not create an OR if the business question can be answered cleanly from:

- the document itself;
- a simple read model;
- the accounting ledger alone;
- a reference register.

Registers are powerful, but they should remain purposeful. A platform full of unnecessary registers becomes harder to reason about.

## Typical developer workflow

When a new document is introduced, the developer should ask:

- does this document change operational state?
- if yes, should that state be queryable as balances or turnovers?
- if yes, is there an existing OR to append to, or do we need a new one?

That question is often the difference between a system that only records documents and a system that can actually explain its operational state.
