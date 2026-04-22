---
title: "Architecture Overview"
description: "Layered architecture of NGB Platform and how hosts, runtime, definitions, engines, and PostgreSQL fit together."
---
# Architecture Overview

<div class="doc-status-row">
  <span class="doc-badge doc-badge-verified">Verified anchors</span>
  <span class="doc-badge doc-badge-inferred">Architecture synthesis</span>
</div>

> This page is the high-level architecture view. For implementation-level tracing, continue with:
> - [Runtime Source Map](/platform/runtime-source-map)
> - [PostgreSQL Source Map](/platform/postgresql-source-map)
> - [API Source Map](/platform/api-source-map)
> - [Source-Anchored Class Maps](/platform/source-anchored-class-maps)

## How to read this page

- Treat this page as the **platform map**.
- Treat the source-map pages as the **evidence layer**.
- Treat the developer guides as the **extension layer**.

## Architectural intent

NGB Platform is organized as a reusable business-application core rather than as a single vertical product. The platform is built around five cooperating layers:

1. **Hosts**  
   Executable applications that expose or operate the platform, such as API, background jobs, watchdog, and migrator hosts.

2. **Contracts, metadata, and definitions**  
   Public DTOs, metadata descriptors, and definition objects that describe catalogs, documents, reports, posting behavior, and UI-facing structure.

3. **Runtime orchestration**  
   The execution center that resolves metadata/definitions, validates requests, coordinates workflows, invokes posting/reporting/document services, and returns UI-ready results.

4. **Specialized business engines**  
   Accounting, operational registers, reference registers, and audit-related mechanisms that model effects and durable business history.

5. **PostgreSQL infrastructure**  
   The concrete persistence implementation, migration packs, SQL/reporting execution, Dapper/Npgsql access, and database-facing readers/writers.

## Verified anchors that support this model

The following files directly support the layering picture:

- `NGB.Runtime/NGB.Runtime.csproj`
- `NGB.Runtime/Documents/DocumentService.cs`
- `NGB.Runtime/Reporting/ReportEngine.cs`
- `NGB.Runtime/Reporting/ReportExecutionPlanner.cs`
- `NGB.PostgreSql/NGB.PostgreSql.csproj`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetCatalog.cs`
- `NGB.PostgreSql/Reporting/IPostgresReportDatasetSource.cs`
- `NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs`
- `NGB.Api/NGB.Api.csproj`
- `NGB.PropertyManagement.Api/Program.cs`
- `NGB.Metadata/NGB.Metadata.csproj`
- `NGB.Definitions/NGB.Definitions.csproj`
- `NGB.Accounting/NGB.Accounting.csproj`
- `NGB.OperationalRegisters/NGB.OperationalRegisters.csproj`
- `NGB.ReferenceRegisters/NGB.ReferenceRegisters.csproj`

## Practical reading order

If you want to understand the platform in the same order requests flow through it, read:

1. [Runtime Request Flow](/architecture/runtime-request-flow)
2. [API](/platform/api)
3. [Runtime](/platform/runtime)
4. [PostgreSQL](/platform/postgresql)
5. [Accounting and Registers](/platform/accounting-and-registers)

If you want to extend the platform, continue with:

- [Platform Extension Points](/guides/platform-extension-points)
- [Developer Workflows](/guides/developer-workflows)

## What this page intentionally does not do

This page does **not** try to prove every claim file-by-file. That job belongs to the source-map pages. Here the goal is to make the architecture understandable before you dive into implementation evidence.
