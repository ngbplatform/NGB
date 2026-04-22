---
title: "Reporting + Definitions Integration Dense Source Map"
description: "Source-anchored map of how report definitions become runtime dataset models, plans, rendered sheets, and PostgreSQL execution."
---

# Reporting + Definitions Integration Dense Source Map

## Why this boundary matters

This is the boundary where NGB turns a declarative report definition into an executable reporting pipeline.

At a high level, the flow is:

1. a `ReportDefinitionDto` enters the runtime layer;
2. runtime materializes a normalized definition model;
3. runtime materializes a normalized dataset model;
4. planner converts effective layout + filters + parameters into a `ReportQueryPlan`;
5. executor path resolves either composable execution or canonical/prebuilt execution;
6. sheet builder turns data pages into a `ReportSheetDto`;
7. PostgreSQL reporting infrastructure resolves dataset bindings, SQL projection, and row materialization.

That end-to-end conversion is visible in the verified anchors listed below.

## Verified anchors

### Runtime definition and dataset materialization

#### `NGB.Runtime/Reporting/ReportDefinitionRuntimeModel.cs`

This class is the first runtime wrapper around `ReportDefinitionDto`.

Verified responsibilities:

- rejects null definition input;
- normalizes `ReportCode` into `ReportCodeNorm`;
- exposes `Capabilities`;
- exposes `DefaultLayout`;
- builds a `ReportDatasetDefinition` when `definition.Dataset` is present;
- resolves the effective layout from request or default layout.

This class is the clearest verified proof that NGB treats report definition DTOs as **input metadata**, then lifts them into a stricter runtime object before planning.

#### `NGB.Runtime/Reporting/ReportDatasetDefinition.cs`

This class is the runtime dataset model.

Verified responsibilities:

- normalizes `DatasetCode`;
- materializes field definitions into `ReportDatasetFieldDefinition`;
- materializes measure definitions into runtime measure definitions;
- rejects duplicate field codes;
- rejects duplicate measure codes;
- precomputes capability sets for:
  - filterable fields,
  - groupable fields,
  - sortable fields,
  - selectable fields;
- resolves whether a field supports a given time grain;
- resolves whether a measure supports a given aggregation;
- resolves default/actual aggregation for a measure.

This is the key integration point between **definition-time dataset metadata** and **planner-time execution logic**.

#### `NGB.Runtime/Reporting/ReportDatasetFieldDefinition.cs`

This class validates and normalizes dataset field metadata.

Verified responsibilities:

- normalizes field codes;
- loads supported time grains from DTO;
- rejects invalid configuration where non-time fields declare time grains;
- exposes `SupportsTimeGrain`.

This is important because planner behavior is not driven by raw DTO text.  
It is driven by a normalized and validated runtime field model.

### Runtime planning and rendering

#### `NGB.Runtime/Reporting/ReportExecutionPlanner.cs`

This class converts runtime definition + effective request into `ReportQueryPlan`.

Verified responsibilities:

- requires `ReportExecutionContext`;
- uses `ReportDefinitionRuntimeModel`;
- reads `definition.Dataset`;
- builds:
  - row groups,
  - column groups,
  - measures,
  - detail fields,
  - predicates,
  - parameters,
  - sorts,
  - plan shape,
  - paging;
- for canonical paths without dataset, builds canonical predicates from filter metadata;
- for dataset-backed paths, resolves fields and measures through `ReportDatasetDefinition`;
- validates that requested sorts are backed by selected groups/fields/measures;
- throws invariant/configuration errors when the planner state should have been prevented earlier by validation.

This is the most important verified bridge from **definition model** to **execution model**.

#### `NGB.Runtime/Reporting/ReportEngine.cs`

This class orchestrates the full runtime reporting pipeline.

Verified responsibilities:

- loads report definition through `IReportDefinitionProvider`;
- optionally resolves a report variant;
- validates layout through `IReportLayoutValidator`;
- creates `ReportDefinitionRuntimeModel`;
- optionally expands filter scopes;
- resolves effective layout;
- builds execution context;
- builds query plan via `ReportExecutionPlanner`;
- chooses rendered-sheet paging behavior for grouped composable reports;
- executes through `IReportPlanExecutor`;
- enriches interactive document fields through `IDocumentDisplayReader`;
- builds final sheet through `ReportSheetBuilder`;
- returns `ReportExecutionResponseDto`.

This is the central verified orchestration point where definition-time and executor-time concerns meet.

#### `NGB.Runtime/Reporting/ReportSheetBuilder.cs`

This class converts `ReportQueryPlan + ReportDataPage` into the UI-facing sheet.

Verified responsibilities:

- builds empty skeleton sheets;
- builds standard sheets;
- merges prebuilt sheets;
- builds pivot sheets;
- uses row hierarchy column semantics;
- enforces visible-row cap for composable reports;
- stamps diagnostics into sheet meta.

This shows that NGB has an explicit render stage between raw execution rows and UI response payload.

### PostgreSQL dataset-backed execution

#### `NGB.PostgreSql/Reporting/IPostgresReportDatasetSource.cs`

This is the registration seam for PostgreSQL dataset bindings.

Verified responsibility:

- contributors return `IReadOnlyList<PostgresReportDatasetBinding>`.

This is the clearest verified extension point for composable PostgreSQL-backed datasets.

#### `NGB.PostgreSql/Reporting/PostgresReportDatasetCatalog.cs`

This is the registry for PostgreSQL dataset bindings.

Verified responsibilities:

- consumes all `IPostgresReportDatasetSource` registrations;
- builds a dictionary keyed by normalized dataset code;
- rejects duplicate dataset bindings;
- resolves a dataset binding by normalized code;
- throws if a requested dataset binding is not registered.

This is the infrastructure mirror of runtime dataset normalization.

#### `NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs`

This class transforms a PostgreSQL execution request into SQL.

Verified responsibilities:

- resolves dataset binding from `PostgresReportDatasetCatalog`;
- projects row groups, column groups, detail fields, and measures;
- adds support fields for interactivity;
- applies base where clause;
- applies predicates and parameters;
- resolves sorts;
- applies offset/limit paging;
- produces `PostgresReportSqlStatement`.

This is the verified point where declarative report selections become SQL projection and filtering.

#### `NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs`

This class executes the SQL statement.

Verified responsibilities:

- calls `PostgresReportSqlBuilder`;
- opens the unit-of-work connection;
- runs SQL via Dapper;
- implements over-fetch paging (`limit + 1`);
- materializes rows into execution rows;
- returns diagnostics like executor kind and row count.

This is the verified executor edge of the composable PostgreSQL reporting pipeline.

## Integration flow, written against verified anchors

### 1. Definition DTO enters runtime

Verified by:

- `ReportEngine`
- `ReportDefinitionRuntimeModel`

Runtime does not plan directly from a raw DTO.  
It first normalizes the definition into `ReportDefinitionRuntimeModel`.

### 2. Dataset DTO becomes runtime dataset model

Verified by:

- `ReportDefinitionRuntimeModel`
- `ReportDatasetDefinition`
- `ReportDatasetFieldDefinition`

This is where code normalization and dataset capability validation happen.

### 3. Effective request becomes execution plan

Verified by:

- `ReportExecutionPlanner`

The planner converts layout/filter/parameter intent into a stable `ReportQueryPlan`.

### 4. Plan reaches executor

Verified by:

- `ReportEngine`
- `PostgresReportDatasetCatalog`
- `PostgresReportSqlBuilder`
- `PostgresReportDatasetExecutor`

For PostgreSQL-backed composable reports, runtime plan information is translated into dataset binding lookup, SQL generation, and execution.

### 5. Data page becomes rendered sheet

Verified by:

- `ReportSheetBuilder`

The final sheet is not raw SQL output.  
It is a rendering-stage artifact with columns, rows, meta, diagnostics, row hierarchy, and pivot support.

## Architecture synthesis

The verified anchors strongly support this reading of the platform:

- **Definitions / contracts** describe the report declaratively.
- **Runtime models** normalize and validate those definitions for execution.
- **Planner** converts declarative layout into an executable plan.
- **PostgreSQL dataset bindings** convert plan semantics into SQL semantics.
- **Sheet builder** converts execution results into UI semantics.

That separation is one of the strongest architectural qualities of the current reporting stack.

## Related pages

- [Reporting Subsystem Dense Source Map](/platform/reporting-subsystem-dense-source-map)
- [Definitions + Metadata Boundary Dense Source Map](/platform/definitions-metadata-boundary-dense-source-map)
- [Runtime Execution Core Dense Source Map](/platform/runtime-execution-core-dense-source-map)
- [Reporting Subsystem Verified Anchors](/reference/reporting-subsystem-verified-anchors)
