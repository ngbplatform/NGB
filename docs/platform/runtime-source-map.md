---
title: Runtime Source Map
---

# Runtime Source Map

This page is the **source-anchored reading map** for `NGB.Runtime`.

Use it when you want to understand where the platform actually turns metadata and definitions into executable business behavior.

<div class="doc-status-row">
  <span class="doc-badge doc-badge-verified">Verified: confirmed source anchors</span>
  <span class="doc-badge doc-badge-inferred">Inferred: module-level conclusions</span>
</div>

<div class="doc-reading-box">
  <p><strong>How to read this page.</strong> Start with the confirmed anchors, then use the rest of the page to understand what those files prove about Runtime’s role, boundaries, and safe change rules.</p>
</div>

## Confirmed source anchors

```text
NGB.Runtime/NGB.Runtime.csproj
NGB.Runtime/Documents/DocumentService.cs
NGB.Runtime/Reporting/ReportEngine.cs
NGB.Runtime/Reporting/ReportExecutionPlanner.cs
```

## What the project boundary tells you first

`NGB.Runtime/NGB.Runtime.csproj` shows the real dependency shape of the module.

Runtime sits above:

- `NGB.Definitions`
- `NGB.Metadata`
- `NGB.Persistence`
- `NGB.Accounting`
- `NGB.OperationalRegisters`
- `NGB.ReferenceRegisters`
- `NGB.Contracts`
- `NGB.Application.Abstractions`

That is the correct mental model: **Runtime is the orchestration center**, not the low-level engine itself and not the infrastructure provider.

## Start with DocumentService when reading Runtime

`NGB.Runtime/Documents/DocumentService.cs` is the best anchor for understanding the universal document model.

It is not a thin CRUD file. It coordinates the actual runtime responsibilities behind documents:

- metadata-driven document CRUD;
- page reads and point reads;
- draft create / update / delete;
- post / unpost / repost;
- mark / unmark for deletion;
- derivation entry points;
- document relationship graph loading;
- accounting / register / UI effects projection.

The constructor is especially important because it shows the real collaboration surface of Runtime. The service depends on repositories, readers, writers, posting services, derivation services, validators, UI contributors, relationship graph readers, enrichment, audit, and effects-query services. That makes the file a reliable map of what Runtime actually owns.

## What DocumentService proves about Runtime design

When you read `DocumentService.cs`, a few platform decisions become obvious.

### Runtime is metadata-driven

The service resolves a `DocumentModel` from the type registry and document metadata, then uses that model to validate payloads, build list queries, shape form metadata, parse parts, and read relationship graphs.

### Runtime is provider-agnostic

The file coordinates `IDocumentReader`, `IDocumentWriter`, `IDocumentRepository`, `IUnitOfWork`, `IDocumentPartsReader`, and `IDocumentPartsWriter`. It does not contain provider-specific SQL. That is exactly the boundary you want to preserve when evolving the platform.

### Runtime owns workflow semantics

Posting, deletion guards, derivation, and effective effects are surfaced as runtime operations. The low-level storage provider should not decide workflow rules.

### Runtime returns UI-ready results

`GetRelationshipGraphAsync` and `GetEffectsAsync` show that Runtime is not only a domain layer. It is also the place where domain state gets transformed into explainable, UI-consumable platform responses.

## Start with ReportEngine when reading reporting flow

`NGB.Runtime/Reporting/ReportEngine.cs` is the best anchor for reporting execution inside Runtime.

It shows the actual runtime reporting pipeline:

1. load report definition;
2. resolve variant;
3. validate layout and request;
4. expand filter scope;
5. build execution plan;
6. execute plan through `IReportPlanExecutor`;
7. build the sheet through `ReportSheetBuilder`;
8. optionally use rendered-sheet paging snapshots for grouped/composable results.

## Why ReportEngine and ReportExecutionPlanner matter together

### Canonical and composable both go through one runtime path

The engine works from definition + planner + executor + sheet builder instead of exposing completely separate public stacks.

### Paging behavior is intentional

`ReportEngine.cs` contains rendered-sheet snapshot logic, cursor decoding, fingerprinting, and diagnostics enrichment. That makes it the right place to understand why grouped/composable paging behaves differently from simpler result paging.

### Layout becomes an explicit query plan

`ReportExecutionPlanner.cs` confirms that Runtime normalizes row groups, column groups, measures, detail fields, filters, sorts, shape settings, and paging into one `ReportQueryPlan`.

### Interactive reporting is a runtime concern

The engine enriches rows with document display values and support fields for drilldown/navigation scenarios. That is a strong sign that report interactivity is part of the platform contract, not a UI hack.

## Recommended reading order

```text
1. NGB.Runtime/NGB.Runtime.csproj
2. NGB.Runtime/Documents/DocumentService.cs
3. NGB.Runtime/Reporting/ReportEngine.cs
4. NGB.Runtime/Reporting/ReportExecutionPlanner.cs
```

After that, continue into the specific sub-area you are changing:

- posting-related collaborators if you are changing document workflow;
- report planner / sheet builder / executors if you are changing reporting;
- query/effects services if you are changing explainability surfaces.

## Safe change rules for Runtime

### Do not move provider logic into Runtime

Runtime should coordinate abstractions and policies. PostgreSQL-specific SQL and Dapper code belong in `NGB.PostgreSql`.

### Keep Runtime vertical-agnostic

Do not leak Property Management, Trade, or any other vertical semantics into shared runtime services.

### Prefer explicit collaborators over hidden magic

`DocumentService.cs` is long, but it is also honest. The constructor shows the real composition. That is better than burying platform behavior behind implicit global state.

## Read next

- [Runtime Execution Map](/platform/runtime-execution-map)
- [Reporting Execution Map](/platform/reporting-execution-map)
