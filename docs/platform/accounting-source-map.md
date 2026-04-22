---
title: Accounting source map
---

# Accounting source map

This page documents the architectural reading path for `NGB.Accounting`.

## Confirmed source anchors

```text
NGB.Accounting/NGB.Accounting.csproj
NGB.Runtime/NGB.Runtime.csproj
NGB.Runtime/Documents/DocumentService.cs
NGB.PropertyManagement.Api/Program.cs
```

## What is confirmed directly in the source shape

`NGB.Accounting/NGB.Accounting.csproj` is a deliberately lean core engine project.

It depends only on:

- `NGB.Core`
- `NGB.Tools`

That tells you something important immediately: Accounting is intended to be a **domain engine / contract layer**, not a runtime orchestrator and not a PostgreSQL provider.

`NGB.Runtime/NGB.Runtime.csproj` then confirms that Runtime depends on Accounting, not the other way around.

## How Runtime proves the role of Accounting

Even though the deepest accounting engine files are not the primary anchors on this page, `NGB.Runtime/Documents/DocumentService.cs` makes the platform contract visible.

Runtime exposes document effects and posting-oriented operations on top of Accounting. That is the architectural direction you want to preserve:

- Accounting defines the semantics of entries/effects;
- Runtime coordinates when and why those semantics are invoked;
- PostgreSQL persists them.

## Why the host composition still matters

`NGB.PropertyManagement.Api/Program.cs` composes Runtime + PostgreSQL + vertical modules. It does not compose Accounting directly into the HTTP host as a separate concern.

That is also correct. Accounting should normally be reached through Runtime and module registrations, not by becoming a web layer of its own.

## What to look for next in your local checkout

When you continue deeper in the codebase, use this page as the boundary map and then inspect the accounting engine files that define:

- entries and posting semantics;
- validation rules;
- invariants for debits/credits and period handling;
- append-only/reversal behavior.

## Safe change rules for Accounting

### Keep Accounting infrastructure-free

Do not put Dapper, SQL, or host-specific concerns into `NGB.Accounting`.

### Keep Accounting independent of vertical business terms

The engine should stay reusable across Property Management, Trade, Agency Billing, and future verticals.

### Put orchestration above, not inside, the engine

Workflow transitions, HTTP behavior, and UI shaping belong to Runtime / API layers, not to the Accounting core.
