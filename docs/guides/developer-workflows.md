---
title: Developer Workflows
---

# Developer Workflows

This section turns the platform documentation into an implementation playbook.

The goal is not to restate the architecture. The goal is to explain how a developer extends NGB in a production-oriented way without breaking the layering model.

## What is source-anchored in this guide set

The workflow guidance in this section is grounded in the following verified source anchors from the repository:

- `NGB.PropertyManagement.Api/Program.cs`
- `NGB.Runtime/Documents/DocumentService.cs`
- `NGB.Runtime/Reporting/ReportEngine.cs`
- `NGB.Runtime/Reporting/ReportExecutionPlanner.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetCatalog.cs`
- `NGB.PostgreSql/Reporting/IPostgresReportDatasetSource.cs`
- `NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs`
- `NGB.Migrator.Core/PlatformMigratorCli.cs`
- `docker/pm/migrator/seed-and-migrate.sh`

These files confirm the real execution boundaries used by the platform today.

## What is template guidance in this guide set

Some extension steps necessarily describe a recommended implementation pattern rather than a directly verified file path. This happens when the current session confirmed the runtime and provider entry points, but did not confirm every registration file or every vertical-specific folder in the repository tree.

When a section says **template guidance**, read it as:

- the pattern is consistent with the verified runtime and provider code;
- the exact file names in your vertical may differ;
- the layering and responsibilities should stay the same.

## Recommended order of work

When adding a new business capability, the safest order is:

1. decide the business invariant and lifecycle;
2. define metadata and user-facing shape;
3. add database schema and migration;
4. wire runtime behavior;
5. wire provider-specific persistence or dataset bindings;
6. compose the vertical host;
7. add tests before polishing UI behavior.

## Guides in this section

- [Platform Extension Points](/guides/platform-extension-points)
- [Add a Document with Accounting and Registers](/guides/add-document-with-accounting-and-registers)
- [Add a Canonical Report](/guides/add-canonical-report-workflow)
- [Add a Composable Report](/guides/add-composable-report-workflow)

## Practical rule

In NGB, business features are not added by pushing logic into the API host.

The API host composes modules. The runtime orchestrates use cases. The PostgreSQL provider persists or executes provider-specific query paths. Definitions and metadata describe what the system is supposed to do. This separation is visible in the verified anchors and should remain true for every new feature.
