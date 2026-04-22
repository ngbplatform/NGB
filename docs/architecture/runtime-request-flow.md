---
title: "Runtime Request Flow"
description: "How requests move from host/API into runtime orchestration and then into PostgreSQL execution."
---

# Runtime Request Flow

<div class="doc-status-row">
  <span class="doc-badge doc-badge-verified">Verified runtime/reporting anchors</span>
  <span class="doc-badge doc-badge-inferred">Flow synthesis</span>
</div>

> Related pages:
> - [HTTP → Runtime → PostgreSQL Execution Map](/architecture/http-runtime-postgres-execution-map)
> - [Runtime Execution Map](/platform/runtime-execution-map)
> - [Reporting Execution Map](/platform/reporting-execution-map)

## Verified anchors behind this flow

- `NGB.PropertyManagement.Api/Program.cs`
- `NGB.Runtime/Documents/DocumentService.cs`
- `NGB.Runtime/Reporting/ReportEngine.cs`
- `NGB.Runtime/Reporting/ReportExecutionPlanner.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetCatalog.cs`
- `NGB.PostgreSql/Reporting/IPostgresReportDatasetSource.cs`
- `NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs`

## Architecture flow

The platform is designed so that hosts stay thin and runtime orchestration stays central.
The following diagram shows the typical business flow through NGB.

<script setup>
const architectureFlow = String.raw`sequenceDiagram
    participant User as User
    participant Web as Vertical Web App
    participant Api as Vertical API Host
    participant Rt as NGB Runtime
    participant Def as Definitions / Metadata
    participant Acc as Accounting / Registers
    participant Pg as PostgreSQL
    participant Rpt as Reporting

    User->>Web: Create or edit business document
    Web->>Api: Submit document payload + action
    Api->>Rt: Execute platform document flow
    Rt->>Def: Resolve metadata, rules, actions, UI behavior
    Rt->>Pg: Persist business document state
    Rt->>Acc: Produce posting and business effects when applicable
    Acc->>Pg: Append accounting, operational, and reference effects
    Pg-->>Rt: Commit durable business state
    Rt-->>Api: Return document, status, metadata, effects
    Api-->>Web: Return UI-ready response
    Web-->>User: Render document and actions

    User->>Web: Run operational or financial report
    Web->>Api: Execute report
    Api->>Rt: Resolve report definition and execution plan
    Rt->>Rpt: Build dataset, grouping, layout, and interactions
    Rpt->>Pg: Read source data and aggregates
    Pg-->>Rpt: Return result set
    Rpt-->>Api: Return report sheet + interactive targets
    Api-->>Web: Return report response
    Web-->>User: Render report, drilldowns, exports, navigation`
</script>

<MermaidDiagram :chart="architectureFlow" />

## Document flow, simplified

1. A host composes the platform with `AddNgbRuntime()`, PostgreSQL provider registration, and vertical module registration.
2. HTTP enters through the host’s ASP.NET Core pipeline.
3. Request handling resolves platform services.
4. `DocumentService` performs metadata-driven CRUD/lifecycle orchestration.
5. Runtime delegates to posting/derivation/relationship/effects services where needed.
6. Persistence abstractions and PostgreSQL implementations perform durable reads/writes.
7. The host returns DTOs/UI-ready payloads.

## Reporting flow, simplified

1. A report request reaches runtime.
2. `ReportEngine` resolves the report definition and effective request/layout.
3. `ReportExecutionPlanner` builds a `ReportQueryPlan`.
4. Runtime delegates execution to the plan executor.
5. For PostgreSQL-backed composable reporting, the dataset catalog resolves the dataset binding.
6. SQL builder converts the logical plan into concrete SQL.
7. Dataset executor runs SQL through the active unit of work connection/transaction.
8. Runtime shapes the result into a sheet/response DTO.

## Why this split matters

The planner is not the SQL builder.  
The SQL builder is not the report engine.  
The host is not the runtime.

That separation is what keeps NGB extensible without making every host/provider re-implement orchestration logic.

## Recommended companion pages

- [Runtime](/platform/runtime)
- [PostgreSQL](/platform/postgresql)
- [Add a Composable Report](/guides/add-composable-report-workflow)
- [Add a Document with Accounting and Registers](/guides/add-document-with-accounting-and-registers)
