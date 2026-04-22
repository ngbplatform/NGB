---
title: "Reporting Subsystem Verified Anchors"
description: "Verified source anchors used for the dense reporting subsystem chapter."
---

# Reporting Subsystem Verified Anchors

<div class="doc-badge-row">
  <span class="doc-badge verified">Verified</span>
  <span class="doc-badge standard">Reference</span>
</div>

## How to read this page

This page lists only the source files that were explicitly verified and then used to write the dense reporting chapter.

Use it together with:

- [Reporting Subsystem Dense Source Map](/platform/reporting-subsystem-dense-source-map)
- [Reporting Class Collaborators Map](/platform/reporting-class-collaborators-map)

## Verified file set

### Runtime reporting orchestration

- `NGB.Runtime/Reporting/ReportEngine.cs`
- `NGB.Runtime/Reporting/ReportExecutionPlanner.cs`
- `NGB.Runtime/Reporting/ReportSheetBuilder.cs`

### PostgreSQL reporting execution

- `NGB.PostgreSql/Reporting/IPostgresReportDatasetSource.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetCatalog.cs`
- `NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs`

## What these anchors are enough to prove

These files are sufficient to document, at a verified level:

1. the high-level execution shape of the reporting subsystem;
2. the boundary between Runtime planning/rendering and PostgreSQL dataset execution;
3. the dataset registration mechanism for composable reporting;
4. the SQL-building and row-materialization path inside the PostgreSQL provider;
5. the sheet-building stage that turns rows into UI-facing report sheets.

## What remains outside the current verified set

This page does **not** claim direct file verification for every report-related contract or every canonical report executor.

Where chapter pages discuss broader reporting architecture beyond the files above, that material is labeled as:

- **Inferred**, when it is a grounded architectural synthesis from the verified files; or
- **Template**, when it is guidance for extension work rather than a claim about already verified implementation paths.

## Continue with

After this page, read:

- [Reporting Subsystem Dense Source Map](/platform/reporting-subsystem-dense-source-map)
