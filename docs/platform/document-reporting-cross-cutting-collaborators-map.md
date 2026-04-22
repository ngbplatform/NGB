---
title: "Document + Reporting Cross-Cutting Collaborators"
description: "Compact collaborator map for the verified document/reporting interaction boundary in NGB Platform."
---

# Document + Reporting Cross-Cutting Collaborators

## Purpose of this page

This page is the compact companion to:

- [Document + Reporting Cross-Cutting Integration](/platform/document-reporting-cross-cutting-dense-source-map)

It shows the main collaborator clusters instead of narrating the full boundary.

## Collaborator cluster A: document application boundary

**Anchor files**

- `NGB.Application.Abstractions/Services/IDocumentService.cs`
- `NGB.Runtime/Documents/DocumentService.cs`

**Role**

The document boundary exposes:

- draft CRUD;
- posting transitions;
- derivation actions and derivation execution;
- relationship graph;
- effects surface.

This is the primary transactional/document-facing runtime service.

## Collaborator cluster B: document persistence shape

**Anchor files**

- `NGB.Persistence/Documents/IDocumentRepository.cs`
- `NGB.Persistence/Documents/Universal/IDocumentReader.cs`
- `NGB.Persistence/Documents/Universal/IDocumentWriter.cs`
- `NGB.Persistence/Documents/Universal/IDocumentPartsReader.cs`
- `NGB.Persistence/Documents/Universal/IDocumentPartsWriter.cs`

**Role**

These interfaces provide the persistence split used by `DocumentService`:

- common registry row in `documents`;
- typed head rows;
- typed part rows.

This gives the reporting side a stable document identity model without collapsing all document reads into one monolithic repository.

## Collaborator cluster C: document derivation boundary

**Anchor files**

- `NGB.Runtime/Documents/Derivations/IDocumentDerivationService.cs`
- `NGB.Runtime/Documents/DocumentService.cs`

**Role**

Derivation remains in the document subsystem.

Reports can lead users toward documents and actions, but actual draft derivation is delegated through the dedicated derivation service.

## Collaborator cluster D: reporting execution core

**Anchor files**

- `NGB.Runtime/Reporting/ReportEngine.cs`
- `NGB.Runtime/Reporting/ReportExecutionPlanner.cs`
- `NGB.Runtime/Reporting/ReportSheetBuilder.cs`

**Role**

This cluster owns:

- report definition normalization at execution time;
- plan building;
- post-execution enrichment;
- final rendered sheet creation.

The important cross-cutting point here is `ReportEngine.EnrichInteractiveFieldsAsync(...)`.

## Collaborator cluster E: reporting runtime definition model

**Anchor files**

- `NGB.Runtime/Reporting/ReportDefinitionRuntimeModel.cs`
- `NGB.Runtime/Reporting/ReportDatasetDefinition.cs`
- `NGB.Runtime/Reporting/ReportDatasetFieldDefinition.cs`

**Role**

This cluster decides what the report runtime knows about:

- selected fields;
- dataset fields and measures;
- capabilities;
- default layout;
- time-grain support;
- selectable/groupable/filterable fields.

This is where document-facing interactivity becomes structurally possible before SQL is even built.

## Collaborator cluster F: PostgreSQL report execution

**Anchor files**

- `NGB.PostgreSql/Reporting/IPostgresReportDatasetSource.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetCatalog.cs`
- `NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs`

**Role**

This cluster handles:

- dataset-source registration;
- dataset binding resolution;
- SQL generation;
- row materialization.

The verified cross-cutting contribution is that the SQL builder appends support fields for interactive document/account displays when those fields are selected.

## Cross-cutting interaction summary

### Documents contribute

- document ids;
- document lifecycle;
- graph/effects explanations;
- derivation features;
- typed payload assembly.

### Reporting contributes

- analytical layout;
- runtime plan;
- row rendering;
- interactive support-column handling;
- document display enrichment.

### Shared bridge

The bridge is intentionally small:

- document id support fields;
- display resolution;
- action-aware sheet cells;
- separate document graph/effects endpoints.

## Recommended reading order

1. [Document Subsystem Dense Source Map](/platform/document-subsystem-dense-source-map)
2. [Reporting Subsystem Dense Source Map](/platform/reporting-subsystem-dense-source-map)
3. [Document + Reporting Cross-Cutting Integration](/platform/document-reporting-cross-cutting-dense-source-map)
4. [Runtime Execution Core Dense Source Map](/platform/runtime-execution-core-dense-source-map)
