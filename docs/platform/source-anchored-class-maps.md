---
title: "Source-Anchored Class Maps"
description: "Verified class-level map for runtime, reporting, PostgreSQL reporting, and host composition anchors in NGB Platform."
---

# Source-Anchored Class Maps

## Why this page exists

The earlier architecture chapters explain how NGB is designed. This page explains the same platform through a smaller set of **verified source anchors** that are already enough to understand the real execution shape of the system.

Use this page when you want to answer questions like:

- where document CRUD actually lives;
- where report execution is orchestrated;
- where the runtime plan becomes SQL;
- where a vertical host composes the platform;
- which classes are central enough to read first.

## Verified anchors covered here

### Runtime and document flow

- `NGB.Runtime/Documents/DocumentService.cs`
- `NGB.Runtime/NGB.Runtime.csproj`

### Runtime reporting orchestration

- `NGB.Runtime/Reporting/ReportEngine.cs`
- `NGB.Runtime/Reporting/ReportExecutionPlanner.cs`
- `NGB.Runtime/Reporting/ReportSheetBuilder.cs`

### PostgreSQL reporting execution

- `NGB.PostgreSql/Reporting/IPostgresReportDatasetSource.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetCatalog.cs`
- `NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs`
- `NGB.PostgreSql/NGB.PostgreSql.csproj`

### Host composition

- `NGB.PropertyManagement.Api/Program.cs`
- `NGB.PropertyManagement.BackgroundJobs/Program.cs`
- `NGB.PropertyManagement.Watchdog/Program.cs`
- `NGB.Api/NGB.Api.csproj`

## Runtime: the document center of gravity

### `NGB.Runtime/Documents/DocumentService.cs`

This file is the strongest verified anchor for the **universal document subsystem**.

The service is not just CRUD. It is the runtime-level orchestrator for:

- metadata-driven document list and form access;
- draft creation and draft update;
- typed head-table persistence;
- part-table replace semantics for draft editing;
- posting and unposting entry points;
- derivation entry points;
- document flow graph loading;
- effective document effects loading;
- UI-effect capability shaping.

That makes it one of the best files to read if you want to understand the practical meaning of “metadata-driven documents” in NGB.

### What this tells us architecturally

From this file alone, several platform traits are confirmed:

- the platform uses a **generic document service**, not one hand-written CRUD controller per document type;
- document persistence is split between a **common registry** and **typed head/part storage**;
- posting, derivation, relationship graph, and effects are **runtime collaborators**, not hardcoded inside a single persistence class;
- list filtering, payload validation, enrichment, and UI capability shaping happen in runtime, not in the web client.

### Recommended reading order inside the file

Read the file in this order:

1. constructor and collaborators;
2. `GetPageAsync` / `GetByIdAsync`;
3. `CreateDraftAsync` / `UpdateDraftAsync` / `DeleteDraftAsync`;
4. `PostAsync` / `UnpostAsync` / `RepostAsync` / deletion markers;
5. `GetRelationshipGraphAsync`;
6. `GetEffectsAsync`;
7. helper methods that build metadata-driven queries and parse payloads.

That order mirrors how the platform thinks about documents: load, edit, persist, post, explain, trace.

## Runtime reporting: planner → executor → sheet

The reporting runtime is already verified through three central files.

### `NGB.Runtime/Reporting/ReportEngine.cs`

This file is the runtime **report execution orchestrator**.

Its responsibilities include:

- resolving the report definition;
- resolving a variant into an effective request;
- validating the request and layout;
- expanding filter scopes;
- building a `ReportExecutionContext` and `ReportQueryPlan`;
- executing the plan;
- enriching interactive fields such as document display values;
- building the final sheet response;
- handling rendered-sheet snapshot paging for composable reports.

This confirms that `ReportEngine` is not the SQL layer and not merely a formatter. It is the orchestration seam between report definition, planner, execution backend, enrichment, and returned UI sheet.

### `NGB.Runtime/Reporting/ReportExecutionPlanner.cs`

This file is the verified anchor for how a user request becomes a **normalized internal query plan**.

It builds:

- row groups;
- column groups;
- measures;
- detail fields;
- predicates;
- parameters;
- sorts;
- shape metadata;
- paging metadata.

The planner is where the runtime translates “what the user asked for” into “what the executor must run.”

That is an important architectural boundary: planners normalize semantics, executors run them.

### `NGB.Runtime/Reporting/ReportSheetBuilder.cs`

This file is the verified anchor for how the runtime turns a materialized page into a **renderable report sheet**.

It confirms several important reporting behaviors:

- there is an explicit empty-sheet/skeleton path;
- grouped reports go through row-tree / subtotal logic;
- pivot reports go through a dedicated pivot path;
- prebuilt sheets can be merged rather than always rebuilt;
- diagnostics are added into the sheet metadata;
- composable reports enforce visible-row caps at the sheet-building stage.

This means report rendering is not just a serialization concern. It is part of the platform’s reporting model.

## PostgreSQL reporting: dataset registry → SQL builder → executor

The verified PostgreSQL reporting files give a very clear execution chain.

### `NGB.PostgreSql/Reporting/IPostgresReportDatasetSource.cs`

This interface confirms that PostgreSQL composable reporting datasets are registered through **sources** that return one or more dataset bindings.

That is the registration extension point to keep in mind when adding a new composable report dataset.

### `NGB.PostgreSql/Reporting/PostgresReportDatasetCatalog.cs`

This file confirms that dataset sources are aggregated into a **catalog** with duplicate-code protection and normalized lookup.

This is the runtime registry for PostgreSQL-backed report datasets.

### `NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs`

This file is the most important verified anchor for how composable reports become SQL.

It builds:

- projected columns;
- grouping expressions;
- predicate SQL;
- sorting SQL;
- support-field projection for interactive cells;
- paging clauses;
- the final SQL statement and output column metadata.

This file is especially valuable because it confirms concrete platform behavior that was already visible architecturally:

- the platform explicitly injects support fields for clickable interactive cells;
- composable reports still keep alias safety and normalized output-code handling;
- SQL generation is deliberate and structured, not scattered across many readers.

### `NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs`

This file confirms the final execution step:

- build the SQL statement;
- ensure the DB connection is open;
- execute the command through Dapper inside the current unit of work;
- materialize rows;
- apply limit-plus-one paging semantics;
- return execution diagnostics.

This is the clearest verified proof that PostgreSQL report execution is a distinct layer from runtime planning.

## Host composition: how a vertical host wires the platform

### `NGB.PropertyManagement.Api/Program.cs`

This file is the best verified host-composition anchor currently available.

It confirms that a vertical API host composes the platform in layers:

1. infrastructure and health checks;
2. platform runtime;
3. PostgreSQL provider;
4. vertical definition/runtime/provider modules;
5. web/API additions such as controllers, error handling, SSO, report-variant access context, and vertical UI services.

This is important because it shows that the platform is not meant to be used “raw.” It is meant to be composed by a host.

### `NGB.PropertyManagement.BackgroundJobs/Program.cs`

This file confirms that the background-jobs host is also a composition root, not a separate business engine.

It bootstraps infrastructure and then composes:

- `AddNgbRuntime()`
- `AddNgbPostgres(...)`
- vertical modules
- vertical background-job module

So background jobs are a host-level surface over the same platform core.

### `NGB.PropertyManagement.Watchdog/Program.cs`

This file confirms that Watchdog is even thinner:

- build host;
- add watchdog services;
- use watchdog middleware/endpoints;
- run.

This reinforces the intended role of Watchdog: operational visibility, not domain execution.

## End-to-end verified execution path

For reporting, the currently verified execution path is:

1. vertical API host composes runtime and PostgreSQL provider;
2. runtime `ReportEngine` resolves definition, validates, expands, and plans;
3. `ReportExecutionPlanner` creates a normalized internal plan;
4. PostgreSQL dataset catalog resolves the dataset binding;
5. `PostgresReportSqlBuilder` generates SQL and output metadata;
6. `PostgresReportDatasetExecutor` runs the query;
7. `ReportSheetBuilder` turns result pages into a final sheet.

For documents, the currently verified execution path is:

1. vertical API host composes runtime and PostgreSQL provider;
2. runtime `DocumentService` resolves metadata and document model;
3. the service delegates persistence, posting, derivation, effects, graph, and enrichment to collaborators;
4. runtime returns a UI-ready document DTO and related effects/graph outputs.

## What is still intentionally not claimed here

This page does **not** claim verified controller file paths or verified DI extension file paths unless those files were directly confirmed.

That matters because the docs should stay trustworthy. When a path is not verified, it belongs in architecture synthesis or template guidance, not in a source-anchored map.

## Related pages

- [Runtime](/platform/runtime)
- [Runtime Source Map](/platform/runtime-source-map)
- [PostgreSQL](/platform/postgresql)
- [PostgreSQL Source Map](/platform/postgresql-source-map)
- [HTTP → Runtime → PostgreSQL Execution Map](/architecture/http-runtime-postgres-execution-map)
