---
title: "Reporting + Definitions Integration Verified Anchors"
description: "Compact evidence index for the boundary between report definitions, runtime dataset models, planning, rendering, and PostgreSQL dataset execution."
---

# Reporting + Definitions Integration Verified Anchors

This page is a compact evidence index for the reporting/definitions integration boundary.

## Runtime definition and dataset model

- `NGB.Runtime/Reporting/ReportDefinitionRuntimeModel.cs`
- `NGB.Runtime/Reporting/ReportDatasetDefinition.cs`
- `NGB.Runtime/Reporting/ReportDatasetFieldDefinition.cs`

## Runtime planning and rendering

- `NGB.Runtime/Reporting/ReportExecutionPlanner.cs`
- `NGB.Runtime/Reporting/ReportEngine.cs`
- `NGB.Runtime/Reporting/ReportSheetBuilder.cs`

## PostgreSQL dataset-backed execution

- `NGB.PostgreSql/Reporting/IPostgresReportDatasetSource.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetCatalog.cs`
- `NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs`

## Interpretation policy

Use these anchors as the trust base for:

- how report definitions are normalized;
- how datasets are normalized;
- how planner-time validation/execution semantics are derived from dataset capabilities;
- how PostgreSQL dataset sources bridge report planning into SQL execution.

For broader repository rules, see:
