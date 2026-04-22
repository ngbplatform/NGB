---
title: "Reporting Class Collaborators Map"
description: "Class-by-class collaborator map for the verified reporting subsystem anchors."
---

# Reporting Class Collaborators Map

<div class="doc-badge-row">
  <span class="doc-badge verified">Verified</span>
  <span class="doc-badge inferred">Inferred synthesis</span>
</div>

## How to read this page

This page focuses on collaborators, not on feature marketing language.

It answers:

- which class sits at which stage of reporting;
- which dependencies are explicit in the verified source;
- how Runtime and PostgreSQL responsibilities are separated.

See also:

- [Reporting Subsystem Dense Source Map](/platform/reporting-subsystem-dense-source-map)
- [Reporting Subsystem Verified Anchors](/reference/reporting-subsystem-verified-anchors)

## 1. `ReportEngine`

**Verified file**

- `NGB.Runtime/Reporting/ReportEngine.cs`

**Role**

`ReportEngine` is the orchestration entry point for report execution and export-sheet generation.

**Explicit collaborators visible in source**

- `IReportDefinitionProvider`
- `IReportLayoutValidator`
- `ReportExecutionPlanner`
- `IReportPlanExecutor`
- `ReportSheetBuilder`
- `ReportVariantRequestResolver`
- `ReportFilterScopeExpander`
- `IDocumentDisplayReader`
- `IRenderedReportSnapshotStore`

**What this proves**

`ReportEngine` is not itself the SQL executor and not itself the sheet renderer in the low-level sense. It orchestrates:

1. definition lookup;
2. effective request/variant resolution;
3. layout validation;
4. planning;
5. execution;
6. interactive enrichment;
7. final sheet construction;
8. optional rendered-sheet snapshot paging.

## 2. `ReportExecutionPlanner`

**Verified file**

- `NGB.Runtime/Reporting/ReportExecutionPlanner.cs`

**Role**

`ReportExecutionPlanner` transforms the effective request into a normalized `ReportQueryPlan`.

**Explicit responsibilities visible in source**

- builds row groups;
- builds column groups;
- builds measures;
- builds detail fields;
- builds predicates;
- builds parameters;
- builds sorts;
- builds plan shape;
- builds paging.

**What this proves**

The planner is the normalization boundary between definition/layout/filter semantics and execution semantics.

That is an important architectural point: Runtime creates an abstract plan first, and only later does a concrete executor turn that plan into database work.

## 3. `ReportSheetBuilder`

**Verified file**

- `NGB.Runtime/Reporting/ReportSheetBuilder.cs`

**Role**

`ReportSheetBuilder` converts materialized report data into `ReportSheetDto`.

**Explicit behaviors visible in source**

- builds empty skeleton sheets;
- merges prebuilt sheets;
- builds non-pivot sheets;
- builds pivot sheets;
- enforces visible-row caps for composable reporting;
- creates sheet metadata and diagnostics.

**Internal collaborators visible in source**

- `ReportComposableCellActionResolver`
- `ReportCellFormatter`
- `ReportGroupTreeBuilder`
- `ReportSubtotalBuilder`
- `ReportPivotMatrixBuilder`
- `ReportPivotHeaderBuilder`

**What this proves**

Rendering is not buried inside the PostgreSQL executor. The PostgreSQL side returns execution data; Runtime owns the UI-facing sheet composition.

## 4. `IPostgresReportDatasetSource`

**Verified file**

- `NGB.PostgreSql/Reporting/IPostgresReportDatasetSource.cs`

**Role**

This is the registration seam for PostgreSQL-backed composable datasets.

**What this proves**

Composable dataset registration is source-based and modular. Multiple modules can contribute dataset bindings, which are later collected into a catalog.

## 5. `PostgresReportDatasetCatalog`

**Verified file**

- `NGB.PostgreSql/Reporting/PostgresReportDatasetCatalog.cs`

**Role**

`PostgresReportDatasetCatalog` gathers dataset bindings from registered `IPostgresReportDatasetSource` implementations.

**What it enforces**

- registration must exist;
- dataset codes are normalized;
- duplicate dataset bindings are rejected.

**What this proves**

Dataset discovery is centralized and validated before execution. That is a strong platform pattern for modular report composition.

## 6. `PostgresReportSqlBuilder`

**Verified file**

- `NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs`

**Role**

This class translates the PostgreSQL execution request into a SQL statement object.

**Explicit responsibilities visible in source**

- resolve dataset binding;
- project row groups / column groups / detail fields / measures;
- append support fields for interactions;
- build predicates;
- build sort clauses;
- build group by;
- apply offset/limit paging;
- produce `PostgresReportSqlStatement`.

**What this proves**

The SQL builder is where the abstract plan becomes database-specific SQL semantics.

It is therefore the key provider-specific translation boundary.

## 7. `PostgresReportDatasetExecutor`

**Verified file**

- `NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs`

**Role**

This class executes the SQL statement through the active `IUnitOfWork` connection and materializes rows.

**Explicit behaviors visible in source**

- ensures DB connection is open;
- executes SQL through Dapper;
- applies `limit + 1` paging semantics for `hasMore`;
- materializes rows into dictionaries;
- returns diagnostics marking executor identity.

**What this proves**

The executor is intentionally thin. It does not plan, validate, or build UI sheets. It executes and materializes.

## End-to-end collaborator chain

```text
Report definition / effective request
    ↓
ReportEngine
    ↓
ReportExecutionPlanner
    ↓
IReportPlanExecutor
    ↓
PostgreSQL request + dataset binding
    ↓
PostgresReportSqlBuilder
    ↓
PostgresReportDatasetExecutor
    ↓
ReportDataPage
    ↓
ReportSheetBuilder
    ↓
ReportSheetDto
```

## Architectural takeaway

The verified files strongly support this design split:

- **Runtime owns report semantics, planning, enrichment, and sheet rendering**
- **PostgreSQL owns dataset registration, SQL generation, and row execution/materialization**

That split is one of the most important architectural ideas in the current reporting subsystem.

## Continue with

- [Reporting Subsystem Dense Source Map](/platform/reporting-subsystem-dense-source-map)
