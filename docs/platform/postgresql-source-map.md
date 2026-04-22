---
title: PostgreSQL Source Map
---

# PostgreSQL Source Map

This page is the reading map for `NGB.PostgreSql`.

Use it when you want to understand how the platform’s provider-specific implementation turns abstract runtime requests into PostgreSQL execution, Dapper materialization, and embedded migration packs.

<div class="doc-status-row">
  <span class="doc-badge doc-badge-verified">Verified: confirmed source anchors</span>
  <span class="doc-badge doc-badge-inferred">Inferred: provider-shape conclusions</span>
</div>

<div class="doc-reading-box">
  <p><strong>How to read this page.</strong> Read the project boundary first, then the reporting dataset catalog, then the SQL builder and executor pair. That sequence gives the clearest view of how provider-backed composable reporting actually works.</p>
</div>

## Confirmed source anchors

```text
NGB.PostgreSql/NGB.PostgreSql.csproj
NGB.PostgreSql/Reporting/IPostgresReportDatasetSource.cs
NGB.PostgreSql/Reporting/PostgresReportDatasetCatalog.cs
NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs
NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs
```

## What the project boundary tells you first

`NGB.PostgreSql/NGB.PostgreSql.csproj` confirms the provider role of the module.

It references:

- `Npgsql`
- `Dapper`
- `Evolve`

and depends on platform abstractions such as:

- `NGB.Persistence`
- `NGB.Application.Abstractions`
- `NGB.Metadata`
- `NGB.Accounting`
- `NGB.OperationalRegisters`
- `NGB.ReferenceRegisters`

It also embeds SQL scripts from `db/migrations/**/*.sql` as resources. That is the concrete bridge between the platform model and PostgreSQL execution.

## Start with the dataset registration boundary

`NGB.PostgreSql/Reporting/IPostgresReportDatasetSource.cs` and `PostgresReportDatasetCatalog.cs` establish the provider-side extension point for composable reporting.

That pair proves that dataset bindings are discovered through registered dataset sources, then normalized into a catalog before SQL execution starts.

This is an important architectural detail: a composable report is not just a runtime definition. It also needs a provider-backed dataset registration path.

## Then read PostgresReportSqlBuilder

`NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs` is the best first file for understanding the provider-side reporting contract.

It builds SQL from a normalized execution request by combining:

- dataset binding metadata;
- row groups;
- column groups;
- detail fields;
- measures;
- predicates;
- sorts;
- paging.

It also appends support fields such as account/document IDs that later enable interactive report behavior.

## What these files prove about the provider design

### SQL generation is explicit

The builder constructs `SELECT`, `WHERE`, `GROUP BY`, `ORDER BY`, and paging clauses directly. There is no hidden ORM translation layer.

### The provider respects runtime planning

The runtime decides the logical plan. `PostgresReportSqlBuilder` translates that plan into safe PostgreSQL SQL.

### Interactive drilldown support is built into the provider

The builder injects support fields for account/document navigation and display-driven scenarios. The infrastructure is aware of those technical fields, even though the user-facing behavior is owned by Runtime/UI.

### Dataset registration is a first-class extension point

The dataset catalog and `IPostgresReportDatasetSource` confirm that new composable datasets should be added through provider registrations, not hard-coded into the executor.

## Then read PostgresReportDatasetExecutor

`NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs` shows the execution half of the same provider pipeline.

The file does four important things:

1. build the SQL statement;
2. ensure the unit-of-work connection is open;
3. execute the command through Dapper;
4. materialize rows into provider-neutral report row objects plus diagnostics.

That makes it the clean mirror of the runtime planner/executor boundary.

## Why the separation is good

`PostgresReportSqlBuilder` and `PostgresReportDatasetExecutor` are separate for a reason.

The split keeps the provider easier to reason about:

- one file translates logical plan to SQL;
- one file executes and materializes;
- diagnostics stay explicit;
- paging behavior stays inspectable.

## Recommended reading order

```text
1. NGB.PostgreSql/NGB.PostgreSql.csproj
2. NGB.PostgreSql/Reporting/IPostgresReportDatasetSource.cs
3. NGB.PostgreSql/Reporting/PostgresReportDatasetCatalog.cs
4. NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs
5. NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs
```

After that, continue into:

- provider-side readers and writers for documents/catalogs;
- migration packs under `db/migrations`;
- dependency-injection registration for PostgreSQL services.

## Safe change rules for NGB.PostgreSql

### Keep provider concerns in this module

If a change requires SQL, Dapper, `Npgsql`, migration resource handling, or provider-specific locking semantics, this is the right module.

### Do not let PostgreSQL concerns leak upward

Runtime should not start depending on SQL text, Dapper row shapes, or provider-specific cursor codecs.

### Preserve explainability diagnostics

The executor returns diagnostics on aggregation and row counts. Keep that habit across provider code: infra should stay inspectable.

## Read next

- [Add a Composable Report](/guides/add-composable-report-workflow)
- [Reporting Execution Map](/platform/reporting-execution-map)
