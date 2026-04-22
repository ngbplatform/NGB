---
title: "Reporting + Definitions Integration Collaborators Map"
description: "Verified collaborator map for the boundary between report definitions, runtime planning, sheet rendering, and PostgreSQL dataset execution."
---

# Reporting + Definitions Integration Collaborators Map

## Core collaborator chain

```text
ReportDefinitionDto
    -> ReportDefinitionRuntimeModel
        -> ReportDatasetDefinition
            -> ReportDatasetFieldDefinition / runtime measure definitions
                -> ReportExecutionPlanner
                    -> ReportQueryPlan
                        -> ReportEngine
                            -> IReportPlanExecutor
                                -> PostgreSQL dataset infrastructure
                                    -> PostgresReportDatasetCatalog
                                    -> PostgresReportSqlBuilder
                                    -> PostgresReportDatasetExecutor
                            -> ReportSheetBuilder
                                -> ReportSheetDto
```

## Class-by-class map

### `ReportDefinitionRuntimeModel`

Verified collaborators:

- `ReportDefinitionDto`
- `ReportCapabilitiesDto`
- `ReportLayoutDto`
- `ReportDatasetDefinition`

Role in the boundary:

- normalizes definition metadata;
- exposes default layout and capabilities;
- bridges DTO world into runtime world.

### `ReportDatasetDefinition`

Verified collaborators:

- `ReportDatasetDto`
- `ReportDatasetFieldDefinition`
- runtime measure definitions
- `ReportAggregationKind`
- `ReportTimeGrain`

Role in the boundary:

- materializes dataset metadata into normalized runtime structures;
- answers planner questions about field capabilities and measure aggregation support.

### `ReportDatasetFieldDefinition`

Verified collaborators:

- `ReportFieldDto`
- `ReportFieldKind`
- `ReportTimeGrain`

Role in the boundary:

- validates field-level metadata;
- provides planner-friendly capability checks for time-grain behavior.

### `ReportExecutionPlanner`

Verified collaborators:

- `ReportExecutionContext`
- `ReportDefinitionRuntimeModel`
- `ReportDatasetDefinition`
- `ReportQueryPlan`

Role in the boundary:

- converts effective report request into a normalized execution plan.

### `ReportEngine`

Verified collaborators:

- `IReportDefinitionProvider`
- `IReportLayoutValidator`
- `ReportExecutionPlanner`
- `IReportPlanExecutor`
- `ReportSheetBuilder`
- optional report variant resolver
- optional filter scope expander
- optional document display reader
- optional rendered snapshot store

Role in the boundary:

- top-level runtime orchestrator for reporting.

### `ReportSheetBuilder`

Verified collaborators:

- `ReportDefinitionRuntimeModel`
- `ReportQueryPlan`
- `ReportDataPage`
- `ReportSheetDto`

Role in the boundary:

- converts execution results into final sheet shape;
- owns pivot/non-pivot rendering decisions;
- attaches diagnostics and visible-row safety semantics.

### `IPostgresReportDatasetSource`

Verified collaborator role:

- extension seam that supplies `PostgresReportDatasetBinding` instances.

### `PostgresReportDatasetCatalog`

Verified collaborators:

- `IPostgresReportDatasetSource`
- `PostgresReportDatasetBinding`

Role in the boundary:

- registry of SQL-capable dataset bindings.

### `PostgresReportSqlBuilder`

Verified collaborators:

- `PostgresReportDatasetCatalog`
- `PostgresReportExecutionRequest`
- `PostgresReportSqlStatement`

Role in the boundary:

- converts dataset-backed execution request into SQL.

### `PostgresReportDatasetExecutor`

Verified collaborators:

- `IUnitOfWork`
- `PostgresReportSqlBuilder`
- Dapper
- `PostgresReportExecutionResult`

Role in the boundary:

- runs SQL and materializes result rows.

## Boundary interpretation

The verified collaborators support a strong layered pattern:

- **definition model layer**: report DTOs and dataset DTOs;
- **runtime normalization layer**: `ReportDefinitionRuntimeModel`, `ReportDatasetDefinition`, `ReportDatasetFieldDefinition`;
- **planning layer**: `ReportExecutionPlanner`;
- **execution orchestration layer**: `ReportEngine`;
- **provider-specific execution layer**: PostgreSQL dataset catalog / SQL builder / executor;
- **presentation layer**: `ReportSheetBuilder`.

## Related pages

- [Reporting + Definitions Integration Dense Source Map](/platform/reporting-definitions-integration-dense-source-map)
- [Reporting Class Collaborators Map](/platform/reporting-class-collaborators-map)
- [Definitions + Metadata Collaborators Map](/platform/definitions-metadata-collaborators-map)
