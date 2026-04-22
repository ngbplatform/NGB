---
title: "Document + Reporting Cross-Cutting Verified Anchors"
description: "Verified source anchors used for the document/reporting cross-cutting chapter in NGB Platform docs."
---

# Document + Reporting Cross-Cutting Verified Anchors

## Scope

This index lists the verified files used by the cross-cutting chapter:

- [Document + Reporting Cross-Cutting Integration](/platform/document-reporting-cross-cutting-dense-source-map)
- [Document + Reporting Cross-Cutting Collaborators](/platform/document-reporting-cross-cutting-collaborators-map)

## Verified anchors

### Document-facing boundary

- `NGB.Application.Abstractions/Services/IDocumentService.cs`
- `NGB.Runtime/Documents/DocumentService.cs`
- `NGB.Runtime/Documents/Derivations/IDocumentDerivationService.cs`

### Document persistence boundary

- `NGB.Persistence/Documents/IDocumentRepository.cs`
- `NGB.Persistence/Documents/Universal/IDocumentReader.cs`
- `NGB.Persistence/Documents/Universal/IDocumentWriter.cs`
- `NGB.Persistence/Documents/Universal/IDocumentPartsReader.cs`
- `NGB.Persistence/Documents/Universal/IDocumentPartsWriter.cs`

### Reporting runtime boundary

- `NGB.Runtime/Reporting/ReportEngine.cs`
- `NGB.Runtime/Reporting/ReportExecutionPlanner.cs`
- `NGB.Runtime/Reporting/ReportSheetBuilder.cs`
- `NGB.Runtime/Reporting/ReportDefinitionRuntimeModel.cs`
- `NGB.Runtime/Reporting/ReportDatasetDefinition.cs`
- `NGB.Runtime/Reporting/ReportDatasetFieldDefinition.cs`

### PostgreSQL reporting execution boundary

- `NGB.PostgreSql/Reporting/IPostgresReportDatasetSource.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetCatalog.cs`
- `NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs`

## Why these anchors matter

This set is enough to verify the following claims without introducing speculative file paths:

- the document application boundary is centered around `IDocumentService` and `DocumentService`;
- document lifecycle / graph / effects / derivation are document-facing surfaces;
- the reporting runtime explicitly enriches document-facing fields when reports include interactive document display columns;
- PostgreSQL reporting execution injects support ids that make that enrichment possible;
- final report rendering happens after planning and enrichment, not inside raw SQL execution.

## What is intentionally out of scope

This anchor set does **not** prove every downstream UI detail or every API route. It is intentionally focused on the **runtime and persistence boundary** where document-facing and report-facing execution meet.

For adjacent chapters, see:

- [Document Subsystem Verified Anchors](/reference/document-subsystem-verified-anchors)
- [Reporting Subsystem Verified Anchors](/reference/reporting-subsystem-verified-anchors)
- [Runtime Execution Core Verified Anchors](/reference/runtime-execution-core-verified-anchors)
