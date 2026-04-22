---
title: "Reporting Subsystem Dense Source Map"
description: "Dense source-anchored chapter for the reporting subsystem using only verified file anchors."
---

# Reporting Subsystem Dense Source Map

<div class="doc-badge-row">
  <span class="doc-badge verified">Verified</span>
  <span class="doc-badge inferred">Inferred synthesis</span>
</div>

## How to read this page

This chapter is intentionally denser than the overview pages.

It is meant to answer:

- how report execution is staged;
- where Runtime stops and PostgreSQL starts;
- which classes are orchestration classes versus execution classes;
- how composable reporting is registered and executed.

## 1. Reporting is staged, not monolithic

The verified files show that reporting is built as a staged pipeline rather than a single giant executor.

A grounded reading of the source shows these stages:

1. **definition and request normalization** in Runtime;
2. **plan construction** in Runtime;
3. **provider execution** through a plan executor;
4. **SQL translation and row execution** in PostgreSQL;
5. **sheet rendering** back in Runtime.

This is important because it explains why NGB can support both canonical and composable reporting without collapsing everything into one database-specific layer.

## 2. Runtime orchestration starts in `ReportEngine`

**Verified anchor**

- `NGB.Runtime/Reporting/ReportEngine.cs`

`ReportEngine` is the central coordinator for report execution.

From the verified constructor dependencies and method flow, the engine does all of the following:

- resolves the report definition;
- resolves/report-variant request state when applicable;
- validates the effective layout and request;
- expands scoped filters when a filter-expansion service is present;
- constructs a `ReportExecutionContext`;
- asks the planner for a `ReportQueryPlan`;
- invokes the plan executor;
- enriches interactive document fields through `IDocumentDisplayReader`;
- builds the final sheet through `ReportSheetBuilder`;
- optionally stores and reuses rendered-sheet snapshots for grouped paging scenarios.

### Why this matters

This proves that `ReportEngine` is the real reporting orchestration hub. It is not a trivial façade.

It also proves that paging in the reporting subsystem is not only database paging. There is also a **rendered-sheet paging** mode for composable reports when grouping/pivot/subtotals make raw row paging insufficient.

## 3. Planning is explicit in `ReportExecutionPlanner`

**Verified anchor**

- `NGB.Runtime/Reporting/ReportExecutionPlanner.cs`

`ReportExecutionPlanner` turns the effective request into a `ReportQueryPlan`.

The verified code shows that planning covers:

- row groups;
- column groups;
- measures;
- detail fields;
- predicates;
- parameters;
- sorts;
- plan shape;
- paging.

### What is architecturally important here

The planner is where semantic reporting concepts become normalized execution concepts.

That is a strong design decision, because it means:

- validation and normalization happen before provider execution;
- providers can execute against a stable plan model rather than raw HTTP DTOs;
- Runtime remains the owner of reporting semantics.

## 4. Rendering is explicit in `ReportSheetBuilder`

**Verified anchor**

- `NGB.Runtime/Reporting/ReportSheetBuilder.cs`

`ReportSheetBuilder` converts execution results into a `ReportSheetDto`.

The verified source shows separate paths for:

- empty skeleton sheets;
- regular non-pivot sheets;
- pivot sheets;
- prebuilt sheet merging.

It also shows that the builder attaches meta/diagnostics and enforces visible-row caps for composable reports.

### What this proves

The final report surface returned to the UI is not just the raw SQL result set.

Instead, Runtime is responsible for:

- row hierarchy handling;
- subtotal composition;
- pivot header generation;
- sheet metadata and diagnostics;
- the UI-facing shape of the result.

That separation is one of the key platform qualities of the reporting subsystem.

## 5. PostgreSQL composable reporting is source-registered

**Verified anchors**

- `NGB.PostgreSql/Reporting/IPostgresReportDatasetSource.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetCatalog.cs`

`IPostgresReportDatasetSource` is the extension seam through which modules contribute PostgreSQL dataset bindings.

`PostgresReportDatasetCatalog` then:

- loads bindings from all registered sources;
- normalizes dataset codes;
- rejects duplicates;
- serves dataset bindings by normalized code.

### What this proves

Composable reporting datasets are modular and registration-driven.

That is the provider-side equivalent of what the definitions/planning layer is doing on the Runtime side.

## 6. SQL generation is isolated in `PostgresReportSqlBuilder`

**Verified anchor**

- `NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs`

This class is the PostgreSQL translation layer.

The verified code shows that it is responsible for:

- resolving the dataset binding;
- projecting row groups, column groups, detail fields, and measures;
- generating support fields for interaction;
- building predicates;
- building sort clauses;
- building group-by;
- applying offset/limit paging;
- producing a final `PostgresReportSqlStatement`.

### Architectural meaning

This is the concrete provider bridge.

`ReportExecutionPlanner` builds a provider-agnostic execution shape; `PostgresReportSqlBuilder` turns that shape into PostgreSQL SQL.

That is the cleanest verified split in the subsystem.

## 7. Execution/materialization is isolated in `PostgresReportDatasetExecutor`

**Verified anchor**

- `NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs`

`PostgresReportDatasetExecutor` is deliberately thin.

The verified code shows that it:

- ensures the DB connection is open through `IUnitOfWork`;
- executes the SQL via Dapper;
- uses `limit + 1` semantics to determine `HasMore`;
- materializes rows into dictionaries;
- returns PostgreSQL execution diagnostics.

### Architectural meaning

This class is not trying to own planning, validation, or rendering. It simply executes and materializes.

That is a good sign: the provider is focused, not overloaded.

## 8. Verified execution chain

The verified anchors support the following chain:

```text
ReportEngine
  → ReportExecutionPlanner
  → IReportPlanExecutor
  → PostgresReportDatasetCatalog
  → PostgresReportSqlBuilder
  → PostgresReportDatasetExecutor
  → ReportSheetBuilder
  → ReportSheetDto
```

## 9. What this says about Canonical vs Composable

### Verified directly

From the verified anchors we can say with confidence that:

- the reporting engine and planner support a flexible plan model;
- the sheet builder supports grouped and pivoted rendering;
- the PostgreSQL provider supports dataset-driven composable execution;
- Runtime owns rendered-sheet paging and enrichment behavior.

### Inferred from the verified files

A grounded inference is that:

- **Composable reporting** is the most explicit fit for the verified PostgreSQL dataset registration flow;
- **Canonical reporting** can coexist by using the same Runtime orchestration boundary while executing through different plan executors or prebuilt-sheet paths.

This inference is consistent with the verified files, but the full canonical executor family is not yet fully re-verified in the current anchor set.

## 10. Extension guidance grounded in verified files

### To add a PostgreSQL-backed composable dataset

The verified provider-side workflow is:

1. implement `IPostgresReportDatasetSource`;
2. return one or more `PostgresReportDatasetBinding` values;
3. make sure the dataset code is unique;
4. allow `PostgresReportDatasetCatalog` to discover the binding;
5. rely on Runtime planning to shape row groups / measures / filters / sorts;
6. let `PostgresReportSqlBuilder` and `PostgresReportDatasetExecutor` handle SQL execution and materialization.

### To understand where to debug a failure

- wrong layout / wrong grouping / invalid sorting:
  - inspect `ReportExecutionPlanner`
- wrong dataset registration / duplicate dataset code:
  - inspect `PostgresReportDatasetCatalog`
- wrong SQL shape:
  - inspect `PostgresReportSqlBuilder`
- wrong row paging / `HasMore` behavior:
  - inspect `PostgresReportDatasetExecutor`
- wrong final sheet shape / pivot rendering / visible row cap:
  - inspect `ReportSheetBuilder`
- wrong orchestration / variant / rendered-sheet paging:
  - inspect `ReportEngine`

## 11. Recommended reading order

For dense reporting work, read in this order:

1. [Reporting Subsystem Verified Anchors](/reference/reporting-subsystem-verified-anchors)
2. [Reporting Class Collaborators Map](/platform/reporting-class-collaborators-map)
3. this page
4. [Add a Canonical Report](/guides/add-canonical-report-workflow)
5. [Add a Composable Report](/guides/add-composable-report-workflow)

## 12. Cross-links

- [Reporting Execution Map](/platform/reporting-execution-map) and the earlier reporting-oriented source-map pages when you need a lighter entry point before this dense chapter
- [Runtime Execution Map](/platform/runtime-execution-map)
- [PostgreSQL Source Map](/platform/postgresql-source-map)
