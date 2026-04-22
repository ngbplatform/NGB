---
title: Operational Registers source map
---

# Operational Registers source map

This page is the architectural reading map for `NGB.OperationalRegisters`.

## Confirmed source anchors

```text
NGB.OperationalRegisters/NGB.OperationalRegisters.csproj
NGB.Runtime/NGB.Runtime.csproj
NGB.Runtime/Documents/DocumentService.cs
NGB.PropertyManagement.Api/Program.cs
```

## What the project boundary tells you first

`NGB.OperationalRegisters/NGB.OperationalRegisters.csproj` is another deliberately slim engine project.

It depends on:

- `NGB.Core`
- `NGB.Tools`

That means Operational Registers are modeled as a shared business engine, not as a runtime coordinator and not as provider code.

## How Runtime proves the role of Operational Registers

`NGB.Runtime/NGB.Runtime.csproj` references `NGB.OperationalRegisters`, which tells you Runtime is expected to coordinate OR behavior.

`NGB.Runtime/Documents/DocumentService.cs` then shows where operational effects become part of the effective document surface exposed back to the caller.

So the intended direction is:

- register semantics live in the OR engine;
- Runtime invokes and exposes them;
- provider modules persist them.

## Why this matters for the platform

Operational Registers are one of the core reasons NGB is more than CRUD.

They let the platform model operational state transitions and balances using business-engine concepts rather than scattered table updates.

## What to inspect next in your local checkout

After reading this boundary map, continue into the OR engine files that define:

- movement records;
- register metadata/contracts;
- balance/finalization semantics;
- dirty-month or period-finalization concepts where present.

## Safe change rules for OR

### Keep the engine provider-agnostic

Do not put SQL or host composition into `NGB.OperationalRegisters`.

### Preserve append-only semantics

Operational register engines should remain compatible with the platform’s append-only/storno philosophy.

### Keep vertical semantics above the shared engine

Warehouse inventory, receivables, occupancy, and other domain-specific meanings should be modeled through definitions/runtime usage, not by hard-coding a vertical into the shared OR core.
