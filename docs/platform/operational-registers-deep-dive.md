---
title: "Operational Registers Deep Dive"
description: "How NGB models operational movements, balances, and document-driven operational effects."
---

# Operational Registers Deep Dive

> **Page intent**
> This page explains how to think about operational registers in NGB as a first-class business engine separate from accounting and separate from generic document CRUD.

## Trust level

- **Verified anchors:** `NGB.Runtime/Documents/DocumentService.cs`, `NGB.Runtime/NGB.Runtime.csproj`, `NGB.Definitions/NGB.Definitions.csproj`, `NGB.OperationalRegisters/NGB.OperationalRegisters.csproj`
- **Architecture synthesis:** yes
- **Template guidance:** yes

## Chapter navigation

- [Accounting and Registers](/platform/accounting-and-registers)
- [Definitions Source Map](/platform/definitions-source-map)
- [Operational Registers Source Map](/platform/operational-registers-source-map)
- [Platform Extension Points](/guides/platform-extension-points)

## Why operational registers exist

Operational registers capture business-state movement that should not be reduced to general ledger entries.

Typical examples:

- inventory on hand by warehouse;
- customer/vendor settlements by business key;
- reservations, allocations, and availability state;
- quantities and turnovers that must remain operationally meaningful even when accounting is summarized differently.

## Verified source anchors

### Runtime is designed to orchestrate operational effects

Confirmed in:

- `NGB.Runtime/NGB.Runtime.csproj`
- `NGB.OperationalRegisters/NGB.OperationalRegisters.csproj`

This dependency shape confirms that operational registers are not a vertical hack. They are part of the platform contract and runtime orchestration model.

### Document effects explicitly include operational register movements

Confirmed in:

- `NGB.Runtime/Documents/DocumentService.cs`

`GetEffectsAsync` returns a `DocumentEffectsDto` that includes:

- accounting entries;
- operational register movements;
- reference register writes;
- UI effect state.

That is a very important design signal: operational movements are treated as a first-class explainability surface, not as hidden implementation detail.

## Practical role of operational registers

Operational registers are the right tool when you need:

- balance-like operational state;
- precise turnover history;
- explainable per-document movement chains;
- dimensions that are business-operational rather than purely financial.

They are especially useful when the operational story and the accounting story are related but not identical.

## Recommended platform-level responsibilities

Operational register infrastructure should own:

- movement model;
- keys/dimensions/resources structure;
- append-only write model;
- balance/turnover/effective-state calculation rules;
- read-side abstractions for operational queries and document effects.

Generic document service should **not** implement those mechanics directly.

## Typical vertical use cases

### Inventory by warehouse

A sales shipment document may:

- decrease quantity in inventory operational register;
- create receivable/business settlement movement;
- create accounting entries for COGS / inventory / receivable / revenue.

### Settlements / open items

A payment or allocation document may:

- reduce operational open item exposure;
- keep a precise operational trail of what was applied to what;
- produce accounting movement separately.

## Recommended design rules

- Use operational registers for business state, not as a copy of the general ledger.
- Keep writes append-only.
- Make movements document-addressable and explainable.
- Prefer stable dimension keys.
- Model quantities/resources explicitly rather than hiding them in generic JSON payloads.

## Review checklist

- Is this state genuinely operational, not just financial?
- Do consumers need balances or turnovers by business dimension?
- Can each movement be traced back to a document?
- Is correction handled append-only?
- Is the register still meaningful without reading the entire document payload?

## Related pages

- [Accounting and Posting Deep Dive](/platform/accounting-posting-deep-dive)
- [Documents, Flow, Effects, Derive](/platform/documents-flow-effects-deep-dive)
- [Accounting and Registers](/platform/accounting-and-registers)
- [Operational Registers Source Map](/platform/operational-registers-source-map)
