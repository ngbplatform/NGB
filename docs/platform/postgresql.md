---
title: "PostgreSQL"
description: "How NGB.PostgreSql realizes persistence, migrations, and reporting execution."
---

# PostgreSQL

<div class="doc-status-row">
  <span class="doc-badge doc-badge-verified">Verified anchors</span>
  <span class="doc-badge doc-badge-inferred">Infrastructure interpretation</span>
</div>

> File-level companion page: [PostgreSQL Source Map](/platform/postgresql-source-map)

## Verified anchors

- `NGB.PostgreSql/NGB.PostgreSql.csproj`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetCatalog.cs`
- `NGB.PostgreSql/Reporting/IPostgresReportDatasetSource.cs`
- `NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs`

## What is directly visible

### Infrastructure role
`NGB.PostgreSql.csproj` confirms the PostgreSQL provider owns concrete storage concerns:

- Dapper
- Npgsql
- Evolve
- persistence/application abstractions
- embedded SQL migrations

### Reporting dataset registration
`PostgresReportDatasetCatalog.cs` and `IPostgresReportDatasetSource.cs` show the provider supports dataset-source registration.

### SQL generation and execution
`PostgresReportSqlBuilder.cs` and `PostgresReportDatasetExecutor.cs` show a clean split between:

- logical-plan-to-SQL building
- SQL execution/materialization

## What this means architecturally

`NGB.PostgreSql` is not just a repository bucket. It is the infrastructure realization of several platform capabilities:

- readers/writers
- migrations
- SQL-backed reporting execution
- performance-oriented data access

## Extension points that are now verified

For composable reporting, a new PostgreSQL-backed dataset path clearly involves:

1. implementing an `IPostgresReportDatasetSource`
2. returning one or more `PostgresReportDatasetBinding`
3. letting the dataset catalog pick it up
4. relying on SQL builder + dataset executor for execution

That is one of the most important verified extension patterns in the current docs set.

## Continue with

- [Add a Composable Report](/guides/add-composable-report-workflow)
- [Migrator CLI](/reference/migrator-cli)
- [Background job catalog](/reference/background-job-catalog)
