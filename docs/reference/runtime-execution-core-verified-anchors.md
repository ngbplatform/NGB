---
title: "Runtime Execution Core Verified Anchors"
description: "Verified source anchors used for the dense runtime execution core chapter."
---

# Runtime Execution Core Verified Anchors

<div class="doc-badge-row">
  <span class="doc-badge verified">Verified</span>
  <span class="doc-badge standard">Reference</span>
</div>

## How to read this page

This page lists only the source files that were explicitly verified and then used to write the dense runtime execution core chapter.

Use it together with:

- [Runtime Execution Core Dense Source Map](/platform/runtime-execution-core-dense-source-map)
- [Runtime Execution Core Collaborators Map](/platform/runtime-execution-core-collaborators-map)

## Verified file set

### Application and Runtime entry points

- `NGB.Application.Abstractions/Services/IDocumentService.cs`
- `NGB.Runtime/Documents/DocumentService.cs`
- `NGB.Runtime/Documents/Derivations/IDocumentDerivationService.cs`
- `NGB.Runtime/Reporting/ReportEngine.cs`
- `NGB.Runtime/Reporting/ReportExecutionPlanner.cs`
- `NGB.Runtime/Reporting/ReportSheetBuilder.cs`
- `NGB.Runtime/NGB.Runtime.csproj`

### Persistence boundaries for documents

- `NGB.Persistence/Documents/IDocumentRepository.cs`
- `NGB.Persistence/Documents/Universal/IDocumentReader.cs`
- `NGB.Persistence/Documents/Universal/IDocumentWriter.cs`
- `NGB.Persistence/Documents/Universal/IDocumentPartsReader.cs`
- `NGB.Persistence/Documents/Universal/IDocumentPartsWriter.cs`

### PostgreSQL reporting execution

- `NGB.PostgreSql/Reporting/IPostgresReportDatasetSource.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetCatalog.cs`
- `NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs`
- `NGB.PostgreSql/NGB.PostgreSql.csproj`

### Vertical host composition

- `NGB.PropertyManagement.Api/Program.cs`

## What these anchors are enough to prove

These files are sufficient to document, at a verified level:

1. the public application surface for document operations;
2. the orchestration role of `DocumentService` in draft CRUD, derivation, relationship graph loading, and effects retrieval;
3. the boundary between runtime orchestration and persistence abstractions for documents;
4. the boundary between runtime reporting orchestration and PostgreSQL reporting execution;
5. the fact that a vertical API host composes Runtime and PostgreSQL together rather than embedding those responsibilities inside the host itself.

## What remains outside the current verified set

This page does **not** claim direct file verification for every posting service, every relationship-graph reader implementation, or every DI registration helper.

Where the chapter discusses broader execution-core architecture beyond the files above, that material is labeled as:

- **Inferred**, when it is a grounded architectural synthesis from the verified files; or
- **Template**, when it is guidance for extension work rather than a claim about already verified implementation paths.

## Continue with

After this page, read:

- [Runtime Execution Core Dense Source Map](/platform/runtime-execution-core-dense-source-map)
- [Document Subsystem Dense Source Map](/platform/document-subsystem-dense-source-map)
- [Reporting Subsystem Dense Source Map](/platform/reporting-subsystem-dense-source-map)
