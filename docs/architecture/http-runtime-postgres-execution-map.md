---
title: "HTTP → Runtime → PostgreSQL Execution Map"
---

# HTTP → Runtime → PostgreSQL Execution Map

This page documents the **verified execution spine** of NGB as it appears in the current repository state.

It does not try to enumerate every controller or every registration by assumption. Instead, it focuses on the concrete source anchors that were verified directly and explains the execution path that can be stated confidently from those files.

## Why this page exists

After the module-level source maps, the next useful layer is the **execution map**:

- where a vertical API host composes the platform;
- which platform modules are injected into the host;
- which runtime services sit at the center of execution;
- how reporting reaches the PostgreSQL foundation;
- where durable state and SQL shaping happen.

## Verified directly

### 1. Vertical API host composes platform + PostgreSQL provider

Verified source anchor:

```text
NGB.PropertyManagement.Api/Program.cs
```

This file proves that a vertical API host composes the platform in roughly this order:

- ASP.NET Core host bootstrap
- Serilog
- health checks
- infrastructure wiring
- `AddNgbRuntime()`
- `AddNgbPostgres(connectionString)`
- vertical module registrations
- controllers API
- authentication / authorization
- mapped controllers

It also proves that the host is intentionally thin: the vertical API host does not reimplement core business mechanics itself; it composes the reusable platform core and the vertical module.

### 2. Runtime is the orchestration center

Verified source anchors:

```text
NGB.Runtime/NGB.Runtime.csproj
NGB.Runtime/Documents/DocumentService.cs
NGB.Runtime/Reporting/ReportEngine.cs
```

From these files, the following is directly grounded:

- `NGB.Runtime` depends on `NGB.Definitions`, `NGB.Metadata`, `NGB.Persistence`, `NGB.Accounting`, `NGB.OperationalRegisters`, `NGB.ReferenceRegisters`, `NGB.Contracts`, and `NGB.Application.Abstractions`.
- `DocumentService` is the universal, metadata-driven document CRUD and runtime orchestration entry for document behavior.
- `ReportEngine` is the runtime execution center for report definitions, layout validation, planning, execution, sheet building, paging, and rendered-sheet snapshot logic.

### 3. PostgreSQL provider owns SQL shaping and dataset execution

Verified source anchors:

```text
NGB.PostgreSql/NGB.PostgreSql.csproj
NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs
NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs
```

These files prove that:

- `NGB.PostgreSql` is the concrete persistence/provider module, built around `Npgsql`, `Dapper`, and `Evolve`.
- PostgreSQL reporting uses a dedicated SQL builder, not ad hoc SQL assembled in runtime.
- query execution is separated from SQL construction;
- report execution returns structured rows plus paging/diagnostics metadata.

## End-to-end map

<script setup>
const flowchart = String.raw`flowchart LR
    Request["HTTP request"] --> Api["Vertical API host<br/>NGB.PropertyManagement.Api/Program.cs"]

    Api -->|compose runtime| Runtime["NGB.Runtime"]
    Api -->|compose provider| Pg["NGB.PostgreSql"]

    Runtime --> Doc["DocumentService"]
    Runtime --> Report["ReportEngine"]

    Doc --> Persist["Persistence abstractions"]
    Persist --> Db[("PostgreSQL")]

    Report --> Planner["Planning and validation"]
    Planner --> Sql["PostgresReportSqlBuilder"]
    Sql --> Exec["PostgresReportDatasetExecutor"]
    Exec --> Db

    Pg --> Sql`
</script>

<MermaidDiagram :chart="flowchart" />

## Document request execution

## Entry point

The exact shared platform controller file path was **not asserted in the current verified anchor set**.

What *is* verified directly is that the vertical host maps controllers and composes runtime. The validated runtime document execution center is:

```text
NGB.Runtime/Documents/DocumentService.cs
```

## What `DocumentService` actually centralizes

From the file itself, `DocumentService` is responsible for:

- metadata-driven document type resolution;
- document list/read operations;
- draft create/update/delete;
- posting, unposting, reposting;
- mark/unmark for deletion;
- relationship graph loading for Document Flow;
- effects loading for Accounting / OR / RR sections;
- derivation-based document creation;
- payload validation, part validation, reference enrichment, and audit write coordination.

That makes it the main **business-facing runtime façade** for document behavior.

## Reporting request execution

The validated runtime reporting center is:

```text
NGB.Runtime/Reporting/ReportEngine.cs
```

The file shows a layered reporting flow:

1. resolve report definition;
2. optionally resolve report variant;
3. validate request/layout;
4. build runtime model;
5. expand filter scopes;
6. compute effective layout;
7. build execution context;
8. build query plan;
9. execute through plan executor;
10. enrich interactive fields;
11. build report sheet;
12. optionally materialize rendered-sheet snapshots for grouped paging.

This is important architecturally: runtime owns **report semantics and orchestration**, while PostgreSQL owns **SQL realization**.

## PostgreSQL reporting flow

The validated PostgreSQL reporting files are:

```text
NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs
NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs
```

Together they show:

- dataset-based SQL construction;
- safe alias enforcement;
- explicit handling for row groups, column groups, details, measures, predicates, sorts, and support fields;
- execution through the active unit of work / transaction;
- paging with `OFFSET` / `LIMIT + 1` in the generic composable path;
- structured diagnostics returned to the runtime/report layer.

## Architectural reading

This gives a clean separation:

### Host layer
Owns:
- ASP.NET Core bootstrapping
- infrastructure composition
- authentication middleware
- controller mapping

### Runtime layer
Owns:
- document semantics
- report semantics
- orchestration of definitions, validation, posting, derivation, effects, and graph building

### PostgreSQL layer
Owns:
- SQL building
- query execution
- migration pack embedding
- concrete provider implementation

## Inferred from composition and project references

The following statements are reasonable and useful, but should be read as **inferred from composition and module boundaries**, not as individually asserted path-by-path in the current verified anchor set:

- shared platform controllers likely delegate into runtime services rather than containing business logic directly;
- provider-specific readers/writers sit in `NGB.PostgreSql` and satisfy persistence abstractions consumed by runtime;
- vertical modules contribute definitions, handlers, and read-model extensions, while the platform host stays generic.

## What to read next

- [Host Composition and DI Map](/architecture/host-composition-and-di-map)
- [Runtime Execution Map](/platform/runtime-execution-map)
- [Reporting Execution Map](/platform/reporting-execution-map)
