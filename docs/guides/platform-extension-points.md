---
title: Platform Extension Points
---

# Platform Extension Points

This page describes where a new feature should enter the platform.

The most important design rule is simple: **extend NGB at the correct layer**.

## 1. Host composition

**Verified anchor:** `NGB.PropertyManagement.Api/Program.cs`

The API host proves the composition model used by NGB today:

- the host configures infrastructure and health checks;
- the host adds `AddNgbRuntime()`;
- the host adds `AddNgbPostgres(...)`;
- the host adds vertical module registrations;
- the host maps controllers and middleware;
- the host does not contain the business implementation itself.

This means a new feature should usually be *implemented below the host* and *registered from the host*.

## 2. Metadata-driven document extension point

**Verified anchor:** `NGB.Runtime/Documents/DocumentService.cs`

`DocumentService` confirms several important things about the platform contract:

- documents are resolved by `documentType` through a type registry;
- CRUD is universal and metadata-driven;
- head fields and part rows are validated through metadata;
- posting, derivation, relationship graph, audit, and effects are delegated to dedicated runtime services;
- the service does not embed vertical-specific document logic.

For a new document type, the extension point is not a new controller. The extension point is the metadata + runtime registrations that make the universal controller path able to resolve the document type.

## 3. Reporting extension point

**Verified anchors:**

- `NGB.Runtime/Reporting/ReportEngine.cs`
- `NGB.Runtime/Reporting/ReportExecutionPlanner.cs`

These files show that reporting flows through a common pipeline:

- resolve definition;
- validate request;
- build effective layout;
- build plan;
- execute plan;
- build sheet.

This means a new report should plug into the definition/planning/execution model rather than inventing a parallel reporting surface.

## 4. PostgreSQL dataset registration extension point

**Verified anchors:**

- `NGB.PostgreSql/Reporting/PostgresReportDatasetCatalog.cs`
- `NGB.PostgreSql/Reporting/IPostgresReportDatasetSource.cs`
- `NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs`

These files confirm the provider-specific composable-report path:

- PostgreSQL gathers dataset bindings from `IPostgresReportDatasetSource` registrations;
- the dataset catalog resolves a dataset by normalized code;
- the SQL builder converts selected fields, groups, measures, predicates, and sorts into SQL;
- the dataset executor runs the SQL through the current unit of work connection.

For a new composable report, the main provider-specific extension point is a new dataset binding source.

## 5. Migrator and bootstrap extension point

**Verified anchors:**

- `NGB.Migrator.Core/PlatformMigratorCli.cs`
- `docker/pm/migrator/seed-and-migrate.sh`

These confirm that schema and seed flows are intentionally explicit:

- migration packs are discovered and planned by the shared migrator CLI;
- vertical migrator hosts call the shared CLI;
- Docker bootstrap executes migrate → seed-defaults → optional seed-demo.

Any new feature that requires storage should arrive with its migration and, when appropriate, default seed support.

## 6. Recommended placement by feature type

### New catalog

Recommended placement:

- metadata/definition layer for the catalog shape;
- PostgreSQL migration for typed storage;
- provider registrations so the universal catalog path can persist and read it.

### New document

Recommended placement:

- metadata/definition layer for head, parts, filters, and presentation;
- PostgreSQL migration for head and part tables;
- runtime validation, posting, derivation, and UI-effects contributors as needed;
- provider-side readers/writers only if the generic path is insufficient.

### New canonical report

Recommended placement:

- report definition in the definitions layer;
- runtime canonical execution registration;
- specialized provider read path when needed;
- sheet and interaction behavior through the shared reporting engine.

### New composable report

Recommended placement:

- dataset and report definition in the definitions layer;
- PostgreSQL dataset binding via `IPostgresReportDatasetSource`;
- no custom controller and no parallel query surface.

## 7. What should stay out of the API host

Do not push the following into the vertical API `Program.cs` or controllers:

- document-specific business validation;
- posting logic;
- ledger rules;
- register write logic;
- report SQL construction;
- document-type-specific CRUD branching.

The verified anchors already show that these concerns belong below the host.

## 8. Safe extension checklist

Before you start implementation, confirm all of the following:

- the feature belongs to a vertical, not to the shared platform core;
- the code can be expressed through metadata + runtime + provider layers;
- the host only needs registrations, not business logic;
- migrations and seeds are planned from day one;
- tests can be written at runtime/provider or integration level.
