---
title: "Document Subsystem Dense Source Map"
description: "Verified class-by-class map of the NGB document subsystem based on confirmed source anchors."
---

# Document Subsystem Dense Source Map

## How to read this page

Use this page when you need to understand the document subsystem as an execution surface rather than as a high-level concept.

This page answers four questions:

1. What is the public application contract for documents?
2. Which runtime class actually orchestrates document behavior?
3. Which collaborators sit underneath that runtime class?
4. What concrete responsibilities are visible from the verified anchors?

For broader architecture context, start with the existing Runtime and Documents pages. For evidence policy, see the reference section on verified source anchors and source evidence policy.

## Verified anchors used on this page

### Public contract

- `NGB.Application.Abstractions/Services/IDocumentService.cs`

### Runtime orchestration

- `NGB.Runtime/Documents/DocumentService.cs`

### Persistence collaborators

- `NGB.Persistence/Documents/IDocumentRepository.cs`
- `NGB.Persistence/Documents/Universal/IDocumentReader.cs`
- `NGB.Persistence/Documents/Universal/IDocumentWriter.cs`
- `NGB.Persistence/Documents/Universal/IDocumentPartsReader.cs`
- `NGB.Persistence/Documents/Universal/IDocumentPartsWriter.cs`

### Derivation

- `NGB.Runtime/Documents/Derivations/IDocumentDerivationService.cs`

## Layered view

```text
IDocumentService
    ↓
DocumentService
    ↓
Document registry + universal head/part persistence + posting/derivation/graph/effects collaborators
```

The important architectural point is that the document subsystem is not modeled as one class per document type. Instead, the platform exposes a universal service contract and resolves per-type behavior through metadata, registries, posting handlers, derivation definitions, and persistence descriptors.

## 1. Public application boundary: `IDocumentService`

`IDocumentService` is the application-facing contract for the entire generic document surface.

From the verified contract, the subsystem includes these capability groups:

### Metadata and lookup

- get all document type metadata
- get metadata for one document type
- lookup documents across types
- resolve multiple document ids across types

### Generic CRUD for drafts

- create draft
- update draft
- delete draft
- page documents of a type
- get by id

### Workflow and state transitions

- post
- unpost
- repost
- mark for deletion
- unmark for deletion
- execute custom action

### Derived behaviors and explainability

- list derivation actions for a document
- derive a new document from another document
- get relationship graph
- get accounting and register effects

That contract makes it explicit that the document subsystem in NGB is broader than CRUD. It is a generic runtime for document lifecycle, explainability, derivation, and posting-related readbacks.

## 2. Runtime center: `DocumentService`

`NGB.Runtime/Documents/DocumentService.cs` is the core verified orchestration class for generic documents.

It is where the platform brings together:

- metadata-driven type resolution
- universal persistence
- draft validation
- posting integration
- derivation integration
- relationship graph reads
- effects reads
- audit write hooks
- UI-oriented disabled-reason logic

### What `DocumentService` clearly owns

From the verified implementation, `DocumentService` owns these responsibilities.

#### Type resolution and metadata shaping

It resolves the document model from the type registry, builds form/list metadata DTOs, validates amount-field presentation metadata, and translates storage-oriented metadata into UI/API DTOs.

#### Generic list/read flow

It builds document queries from search and filters, translates soft-delete filters, resolves period filters, reads typed head rows through the universal reader, and enriches results with reference payload labels.

#### Draft create/update/delete

It creates drafts through the draft service, parses scalar fields and parts from `RecordPayload`, validates payloads through optional validators, writes head rows, replaces part rows, updates registry header timestamps, and optionally emits audit entries.

#### Workflow transitions

It delegates post, unpost, repost, mark-for-deletion, and unmark-for-deletion to the posting subsystem while preserving the generic document API surface.

#### Relationship graph and effects

It resolves the document flow graph in one API call, bulk-loads typed head rows for graph nodes, computes graph DTOs, and builds the effects DTO by combining posting availability, UI contributor output, and effects query results.

#### Derive

It resolves derivation actions, supports backward-compatible scaffold behavior when explicit derivation definitions are missing but initial payload is present, and delegates actual draft creation to the derivation service.

## 3. Collaborator map from the verified constructor

The verified constructor of `DocumentService` is one of the most valuable source anchors in the current documentation set because it exposes the subsystem composition directly.

### Core persistence collaborators

- `IUnitOfWork`
- `IDocumentRepository`
- `IDocumentDraftService`
- `IDocumentReader`
- `IDocumentPartsReader`
- `IDocumentPartsWriter`
- `IDocumentWriter`

### Runtime collaborators

- `IDocumentPostingService`
- `IDocumentDerivationService`
- `IDocumentPostingActionResolver`
- `IDocumentOperationalRegisterPostingActionResolver`
- `IDocumentReferenceRegisterPostingActionResolver`
- `IDocumentRelationshipGraphReadService`
- `IDocumentEffectsQueryService`

### Validation, UI, and enrichment collaborators

- `IReferencePayloadEnricher`
- `IEnumerable<IDocumentDraftPayloadValidator>`
- `IEnumerable<IDocumentUiEffectsContributor>`

### Optional infrastructure collaborators

- `IAuditLogService`
- `TimeProvider`

This constructor shape confirms a key NGB design choice: the generic document service is not thin. It is an orchestration boundary that coordinates multiple domain engines and infrastructure services while keeping the HTTP/API surface universal.

## 4. Persistence model visible from verified anchors

### Registry repository: `IDocumentRepository`

`IDocumentRepository` makes the common header model explicit.

The verified interface documents these facts directly:

- the registry table is `documents`
- per-type document data lives in separate tables like `doc_{type_code}` and `doc_{type_code}__{part}`
- state transitions should use `GetForUpdateAsync` inside an active transaction
- common header mutations include status changes, draft-header updates, one-time numbering, and delete attempts

This is a strong source anchor for the platform rule that the registry row and typed head rows are separate concerns.

### Universal head reader/writer

The verified `IDocumentReader` and `IDocumentWriter` show that typed head-table access is descriptor-driven rather than hardcoded per document class.

The reader supports:

- count and page by `DocumentHeadDescriptor`
- get one document by type descriptor + id
- get many ids of one type
- bulk get head rows across multiple document types
- cross-type lookup and bulk id resolution

The writer supports:

- upsert head values by descriptor + document id + typed values

This is the concrete persistence boundary that enables generic document CRUD on top of type metadata.

### Universal part reader/writer

The verified parts interfaces make draft part semantics explicit.

`IDocumentPartsReader` reads raw DB values for all tabular part tables for a given document id.

`IDocumentPartsWriter` states the important semantic directly:

- drafts use replace-by-document semantics
- implementations are expected to delete all existing part rows for the document and then insert the supplied rows inside the same transaction

That is a critical behavior to preserve in docs because it affects correctness, auditability, and performance expectations for draft editing.

## 5. Derivation boundary

The verified `IDocumentDerivationService` documents the platform meaning of “Create based on”.

Important verified points:

- derivation actions are defined through `DocumentDerivationDefinition`
- those definitions are registered in `DefinitionsRegistry`
- derivation creates a new draft only
- derivation writes relationship links according to the requested relationship codes
- the service exposes both discovery and execution APIs

This is the most concrete verified source anchor currently available for the derive subsystem.

## 6. What is visible indirectly through `DocumentService`

The following collaborators are visible from `DocumentService`, but their implementations were not verified directly in the current anchor set:

- posting service internals
- posting action resolver implementations
- relationship graph read implementation
- effects query implementation
- audit log write implementation
- UI effects contributor implementations

Because of that, this page documents their role only at the orchestration boundary.

### Posting

`DocumentService` treats posting as a delegated workflow concern. It does not compute accounting or register movements itself. Instead, it calls the posting service and action resolvers.

### Relationship graph

`DocumentService` treats document flow as a read model. It requests a graph, bulk-loads node head rows across types, and shapes UI-ready node/edge DTOs.

### Effects

`DocumentService` assembles the response for accounting entries, operational register movements, reference register writes, and UI action availability, but it does not itself compute the underlying accounting/register state.

## 7. Architectural conclusions supported by the verified anchors

### Universal API, metadata-driven execution

The contract and implementation together confirm that documents in NGB are not controller-per-document or entity-class-per-document in the classic CRUD sense. The platform exposes a universal document runtime that is driven by metadata and registries.

### Registry + typed storage split

The registry repository and universal typed readers/writers confirm the split between:

- common document identity and workflow header in `documents`
- type-specific head table data in `doc_{type}`
- type-specific part data in `doc_{type}__{part}`

### Draft editing is replace-oriented for parts

The verified part writer contract confirms that draft part editing is modeled as replace-by-document rather than incremental row mutation.

### Explainability is first-class

The public contract includes both relationship graph and effects. This means explainability is not an afterthought or an admin-only concern. It is part of the main document API surface.

### Derive is platform behavior, not ad hoc vertical glue

The derivation service contract confirms that “create based on” is implemented as a platform capability backed by shared definitions rather than as one-off per-vertical helper code.

## 8. Reading path from this page

After this page, the most useful next pages are:

- `Document Subsystem Verified Anchors` for a compact evidence index
- `Source Anchored Class Maps` for the broader runtime/reporting class layer
- the existing deep-dive page on documents, flow, effects, and derive for higher-level synthesis

## 9. What should be verified next

The highest-value next source anchors for this subsystem are:

- `IDocumentPostingService` and its implementation
- posting action resolver implementations
- relationship graph read service contract and implementation
- effects query service contract and implementation
- document draft service contract and implementation
- document type registry / definitions registry path for document metadata resolution

Those files would let the docs move from orchestration-centered evidence to nearly full end-to-end source anchoring for the document chapter.
