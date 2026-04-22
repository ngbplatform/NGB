---
title: "Reference Registers Deep Dive"
description: "How NGB models append-only reference state such as prices, rates, or policy snapshots."
---

# Reference Registers Deep Dive

> **Page intent**
> This page explains reference registers as a distinct engine for versioned reference state rather than transactional balances.

## Trust level

- **Verified anchors:** `NGB.Runtime/Documents/DocumentService.cs`, `NGB.Runtime/NGB.Runtime.csproj`, `NGB.Definitions/NGB.Definitions.csproj`, `NGB.ReferenceRegisters/NGB.ReferenceRegisters.csproj`
- **Architecture synthesis:** yes
- **Template guidance:** yes

## Chapter navigation

- [Accounting and Registers](/platform/accounting-and-registers)
- [Definitions Source Map](/platform/definitions-source-map)
- [Reference Registers Source Map](/platform/reference-registers-source-map)
- [Platform Extension Points](/guides/platform-extension-points)

## Why reference registers exist

Reference registers solve a different problem from both accounting and operational registers.

They are best suited for reference-like state that changes over time and must remain:

- traceable;
- document-driven;
- version-aware;
- queryable as effective state.

Typical examples:

- pricing;
- tax parameters;
- rate tables;
- policy snapshots;
- document-driven business settings.

## Verified source anchors

### Runtime is wired to orchestrate reference-register writes

Confirmed in:

- `NGB.Runtime/NGB.Runtime.csproj`
- `NGB.ReferenceRegisters/NGB.ReferenceRegisters.csproj`

### Document effects explicitly expose reference-register writes

Confirmed in:

- `NGB.Runtime/Documents/DocumentService.cs`

This means reference writes are expected to be visible and explainable in the same way as accounting entries or operational movements.

## Conceptual model

Reference register writes are usually not about "balance". They are about effective reference state over time.

For example, a pricing document should not mutate a catalog row in place and lose history. Instead, it should write a new reference-state slice that the read side can interpret as the current effective price.

## When to choose a reference register

Use a reference register when:

- you need a document-driven history of reference values;
- users need to know which document introduced the current value;
- effective state is time-aware or period-aware;
- overwriting a catalog field would destroy explainability.

## When **not** to use a reference register

Do not use it when:

- the state is transactional turnover/balance state;
- the state should be modeled as document head/part data only;
- the state is static master data that does not need append-only history.

## Recommended review questions

- Is the value reference-like rather than transactional?
- Must the system preserve prior states for audit/explainability?
- Must users answer “which document set this value?”
- Is effective-state resolution needed?

## Related pages

- [Documents, Flow, Effects, Derive](/platform/documents-flow-effects-deep-dive)
- [Reference Registers Source Map](/platform/reference-registers-source-map)
- [Database Naming and DDL Patterns](/reference/database-naming-and-ddl-patterns)
