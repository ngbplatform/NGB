---
title: "Runtime Execution Core Collaborators Map"
description: "Collaborator map for the verified runtime execution core anchors across documents, reporting, persistence, and host composition."
---

# Runtime Execution Core Collaborators Map

<div class="doc-badge-row">
  <span class="doc-badge verified">Verified</span>
  <span class="doc-badge inferred">Inferred synthesis</span>
</div>

## How to read this page

This page focuses on collaborators and boundaries.

It answers:

- which contracts define the document-facing application surface;
- which dependencies `DocumentService` exposes as part of the runtime orchestration layer;
- which abstractions separate Runtime from persistence;
- which classes separate Runtime reporting from PostgreSQL execution;
- where the vertical host composes the execution core.

See also:

- [Runtime Execution Core Dense Source Map](/platform/runtime-execution-core-dense-source-map)
- [Runtime Execution Core Verified Anchors](/reference/runtime-execution-core-verified-anchors)

## 1. `IDocumentService`

**Verified file**

- `NGB.Application.Abstractions/Services/IDocumentService.cs`

**Role**

`IDocumentService` defines the document-facing application contract consumed above the runtime layer.

**What the verified contract proves**

The interface includes methods for:

- metadata retrieval;
- paged browsing and point lookup;
- cross-type lookup helpers;
- draft creation, update, and deletion;
- post, unpost, repost;
- mark/unmark for deletion;
- derivation actions and `DeriveAsync`;
- relationship graph retrieval;
- document effects retrieval.

**Why this matters**

This proves the document subsystem surface is broader than CRUD. The runtime-facing document boundary already includes flow/navigation/effects behavior and not just basic persistence operations.

## 2. `DocumentService`

**Verified file**

- `NGB.Runtime/Documents/DocumentService.cs`

**Role**

`DocumentService` is the universal, metadata-driven document orchestration class inside Runtime.

**Explicit collaborators visible in source**

- `IUnitOfWork`
- `IDocumentRepository`
- `IDocumentDraftService`
- `IDocumentTypeRegistry`
- `IDocumentReader`
- `IDocumentPartsReader`
- `IDocumentPartsWriter`
- `IDocumentWriter`
- `IDocumentPostingService`
- `IDocumentDerivationService`
- posting action resolvers
- UI effects contributors
- `IDocumentRelationshipGraphReadService`
- `IReferencePayloadEnricher`
- draft payload validators
- optional audit/effects services

**What this proves**

`DocumentService` is not a thin repository wrapper. It is a coordination point where metadata, validation, transaction scope, draft lifecycle, posting flow, derivation flow, relationship graph assembly, and effects retrieval meet.

## 3. `IDocumentRepository`

**Verified file**

- `NGB.Persistence/Documents/IDocumentRepository.cs`

**Role**

`IDocumentRepository` owns the common document registry row (`documents` table) rather than typed head-table payload.

**What the verified comments prove**

The interface documentation states:

- state transitions should use `GetForUpdateAsync` inside an active transaction;
- the repository stores only the common header;
- typed document data lives in `doc_{type_code}` and `doc_{type_code}__{part}` tables;
- number assignment is a guarded one-time operation via `TrySetNumberAsync`.

**Why this matters**

This makes the registry/head-table split explicit. It also shows that locking and lifecycle serialization are deliberate runtime-level concerns, not accidental implementation details.

## 4. Universal document readers and writers

**Verified files**

- `NGB.Persistence/Documents/Universal/IDocumentReader.cs`
- `NGB.Persistence/Documents/Universal/IDocumentWriter.cs`
- `NGB.Persistence/Documents/Universal/IDocumentPartsReader.cs`
- `NGB.Persistence/Documents/Universal/IDocumentPartsWriter.cs`

**Role**

These interfaces form the persistence boundary that `DocumentService` uses for typed head-table and part-table access.

**What the verified contracts prove**

- `IDocumentReader` handles head-table paging, point reads, cross-type lookup, and multi-type graph support reads.
- `IDocumentWriter` handles head upsert.
- `IDocumentPartsReader` returns raw DB values keyed by column name, with Runtime converting them to JSON payload shape.
- `IDocumentPartsWriter` uses replace-by-document semantics for draft parts (`DELETE + INSERT` within the same transaction).

**Why this matters**

This is a strong proof that the universal document model is split intentionally:

- Runtime owns payload shaping and orchestration;
- persistence abstractions own physical table access;
- part-table updates in Draft are not treated as append-only effects but as a mutable draft-editing surface.

## 5. `IDocumentDerivationService`

**Verified file**

- `NGB.Runtime/Documents/Derivations/IDocumentDerivationService.cs`

**Role**

This is the runtime service behind “Create based on”.

**What the verified comments prove**

The interface comments explicitly state that it:

- uses `DocumentDerivationDefinition` from `DefinitionsRegistry`;
- writes relationship codes such as `created_from` and `based_on`;
- creates a Draft only and does not post it.

**Why this matters**

This proves derivation is a first-class runtime workflow and not an ad hoc UI shortcut.

## 6. `ReportEngine`

**Verified file**

- `NGB.Runtime/Reporting/ReportEngine.cs`

**Role**

`ReportEngine` is the runtime orchestrator for report execution.

**Explicit collaborators visible in source**

- `IReportDefinitionProvider`
- `IReportLayoutValidator`
- `ReportExecutionPlanner`
- `IReportPlanExecutor`
- `ReportSheetBuilder`
- optional variant/filter/document-display/snapshot collaborators

**Why this matters for the execution core**

It shows that reporting orchestration sits in the same runtime layer that also hosts document orchestration, but with a separate execution pipeline and separate provider boundary.

## 7. `ReportExecutionPlanner`

**Verified file**

- `NGB.Runtime/Reporting/ReportExecutionPlanner.cs`

**Role**

Normalizes the effective report request into a `ReportQueryPlan`.

**What this proves**

The planner handles:

- row groups;
- column groups;
- measures;
- detail fields;
- predicates;
- parameters;
- sorts;
- shape and paging.

So Runtime does not pass raw client layout state directly into a database provider.

## 8. `ReportSheetBuilder`

**Verified file**

- `NGB.Runtime/Reporting/ReportSheetBuilder.cs`

**Role**

Builds the UI-facing `ReportSheetDto` from plan + data page.

**What the verified methods prove**

The builder contains explicit stages for:

- empty/skeleton sheets;
- merged prebuilt sheets;
- pivot sheets;
- non-pivot row hierarchy sheets;
- visible-row cap enforcement for composable reports.

**Why this matters**

This proves report rendering is still a Runtime responsibility even when SQL execution happens elsewhere.

## 9. PostgreSQL reporting execution collaborators

**Verified files**

- `NGB.PostgreSql/Reporting/IPostgresReportDatasetSource.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetCatalog.cs`
- `NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs`

**Role split visible in verified source**

- dataset registration comes from `IPostgresReportDatasetSource`;
- dataset lookup/registry happens in `PostgresReportDatasetCatalog`;
- SQL translation happens in `PostgresReportSqlBuilder`;
- SQL execution and row materialization happen in `PostgresReportDatasetExecutor`.

**Why this matters**

This proves the PostgreSQL provider is itself staged internally and does not collapse registration, query planning, SQL rendering, and execution into one class.

## 10. Module composition proof from project files and host bootstrap

**Verified files**

- `NGB.Runtime/NGB.Runtime.csproj`
- `NGB.PostgreSql/NGB.PostgreSql.csproj`
- `NGB.PropertyManagement.Api/Program.cs`

**What the verified files prove**

`NGB.Runtime.csproj` references:

- Accounting
- Application.Abstractions
- Contracts
- Definitions
- OperationalRegisters
- ReferenceRegisters
- Persistence
- Metadata

`NGB.PostgreSql.csproj` references:

- Core
- Accounting
- Metadata
- OperationalRegisters
- ReferenceRegisters
- Persistence
- Application.Abstractions
- Tools

`NGB.PropertyManagement.Api/Program.cs` composes:

- `.AddNgbRuntime()`
- `.AddNgbPostgres(cs)`
- vertical module/runtime/postgres registrations

**Why this matters**

This is the verified proof that the execution core is assembled from reusable platform modules and then pulled into a vertical host, rather than being coded inline in the API host.

## 11. What the collaborator map proves about the platform execution core

Taken together, the verified collaborators show a consistent pattern:

1. **application contracts** expose rich business surfaces (`IDocumentService`);
2. **Runtime orchestration** coordinates document and reporting workflows;
3. **persistence abstractions** isolate document table access from runtime orchestration;
4. **PostgreSQL provider classes** isolate dataset registration, SQL generation, and row execution;
5. **vertical hosts** compose these reusable pieces.

That is the real execution core of NGB as far as the verified anchors can prove it.
