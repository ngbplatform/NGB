---
title: "Accounting and Registers"
description: "Accounting, operational registers, and reference registers as specialized business engines."
---

# Accounting and Registers

<div class="doc-status-row">
  <span class="doc-badge doc-badge-verified">Verified module boundaries</span>
  <span class="doc-badge doc-badge-inferred">Engine-role interpretation</span>
  <span class="doc-badge doc-badge-template">Extension guidance</span>
</div>

> Companion source maps:
> - [Accounting source map](/platform/accounting-source-map)
> - [Operational Registers source map](/platform/operational-registers-source-map)
> - [Reference Registers source map](/platform/reference-registers-source-map)

## Verified anchors

- `NGB.Accounting/NGB.Accounting.csproj`
- `NGB.OperationalRegisters/NGB.OperationalRegisters.csproj`
- `NGB.ReferenceRegisters/NGB.ReferenceRegisters.csproj`
- `NGB.Definitions/NGB.Definitions.csproj`
- `NGB.Runtime/NGB.Runtime.csproj`

## Working model

NGB treats accounting and registers as **first-class platform engines**, not as random vertical utilities.

### Accounting
The accounting module represents ledger/posting semantics and durable accounting effects.

### Operational Registers
Operational registers model operational state and movement history such as stock, balances, quantities, and mutual settlements.

### Reference Registers
Reference registers model effective reference-style state such as prices, rates, or other time-sensitive business reference values.

## Why this split is good

This architecture allows a document to produce several different categories of effects without collapsing all business state into a single ledger.

That is especially important in platforms where:

- accounting truth
- operational truth
- reference/effective truth

must coexist but should not be confused.

## What is verified vs inferred here

Verified:
- these modules exist as distinct platform projects
- runtime depends on them
- definitions depends on them
- reference registers also depend on metadata

Inferred:
- the exact internal object model of each engine is not fully anchor-verified in this session
- the engine-role descriptions here come from the verified module boundaries plus the already verified runtime/document/report orchestration layer and prior full-repo analysis

## Best companion guides

- [Add a Document with Accounting and Registers](/guides/add-document-with-accounting-and-registers)
- [Developer Workflows](/guides/developer-workflows)
