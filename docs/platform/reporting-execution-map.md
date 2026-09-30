---
title: "Reporting Execution Map"
---

# Reporting Execution Map

This page focuses on the verified reporting path across runtime and PostgreSQL.

## Verified source anchors

```text
NGB.Runtime/Reporting/ReportQueryService.cs
NGB.Runtime/Reporting/ReportEngine.cs
NGB.Runtime/Reporting/ReportPagedQueryExecutor.cs
NGB.Runtime/Reporting/ReportDownloadService.cs
NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs
NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs
```

## High-level split

The validated reporting architecture is:

- **Runtime** owns report semantics and orchestration.
- **PostgreSQL provider** owns SQL building and execution.

That split is important because it prevents provider-specific SQL concerns from leaking into the runtime planning layer.

## Verified flow

<script setup>
const flowchart = String.raw`flowchart LR
    Host["API host"] -->|execute report| Query["ReportQueryService"]
    Query -->|read session and cursor validation| Runtime["ReportEngine and page executors"]
    Runtime -->|resolve definition and layout| Planner["Planner and validators"]
    Planner -->|normalized report plan| Runtime
    Runtime -->|provider execution request| PgBuilder["PostgresReportSqlBuilder"]
    PgBuilder -->|SQL, parameters, output columns| PgExec["PostgresReportDatasetExecutor"]
    PgExec -->|execute SQL| DB[("PostgreSQL")]
    DB -->|rows| PgExec
    PgExec -->|execution result and diagnostics| Runtime
    Runtime -->|interactive enrichment, sheet build, paging| Response["Report response"]`
</script>

<MermaidDiagram :chart="flowchart" />


## Runtime side details

The verified `ReportEngine` file explicitly performs these stages:

- resolve definition;
- optionally resolve variant;
- validate layout;
- build runtime model;
- expand filter scopes;
- compute effective layout;
- build plan;
- choose paging mode;
- execute through plan executor;
- enrich interactive fields;
- build final sheet;
- delegate grouped or specialized accounting page reads to bounded query executors.

`ReportQueryService` is registered as `IReportEngine` and owns the read-session lifetime and
public cursor protection. Full downloads use `IReportDownloadService` / `ReportDownloadService`
and stream through `IStreamingReportExportService`; they do not materialize a complete sheet.
There is no rendered-sheet snapshot store.

## PostgreSQL side details

### `PostgresReportSqlBuilder`

This file explicitly constructs SQL for:

- row groups;
- column groups;
- detail fields;
- measures;
- support fields for interactive navigation;
- predicates;
- sorting;
- paging.

It also enforces safe aliases and keeps a structured representation of projected output columns.

### `PostgresReportDatasetExecutor`

This file explicitly:

- uses the active unit of work;
- ensures the connection is open;
- executes SQL with Dapper;
- materializes rows into dictionaries;
- applies the `limit + 1` paging convention;
- returns diagnostics.

## What this proves architecturally

### 1. Runtime and provider responsibilities are cleanly separated

Runtime is not assembling SQL text.
PostgreSQL provider is not deciding report semantics.

### 2. Reporting is dataset-driven, not controller-driven

The verified files show a real reporting subsystem, not a controller that hand-builds one-off queries.

### 3. Diagnostics are first-class

Both layers surface diagnostics, which is important for operability and report troubleshooting.

## Performance note

Interactive report HTTP requests require zero offset and follow an opaque continuation cursor.
The provider uses deterministic cursor predicates and `LIMIT + 1` over-fetching to determine
`HasMore`; grouped browsing reads one branch at a time. Full financial aggregates and exports
still scan their matching source range. See
[Report Browsing and Direct Downloads](/architecture/report-execution-results) for limits and
performance validation.

## Recommended reading order

1. `NGB.Runtime/Reporting/ReportEngine.cs`
2. `NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs`
3. `NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs`

## Continue with

- [Runtime Execution Map](/platform/runtime-execution-map)
- [PostgreSQL Source Map](/platform/postgresql-source-map)
