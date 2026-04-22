---
title: "Document + Definitions Integration Dense Source Map"
description: "Verified source-anchored chapter for how document metadata, relationship definitions, derivation definitions, and DocumentService fit together."
---

# Document + Definitions Integration Dense Source Map

<div class="doc-badge-row">
  <span class="doc-badge doc-badge--verified">Verified anchors</span>
  <span class="doc-badge doc-badge--inferred">Architecture synthesis</span>
  <span class="doc-badge doc-badge--template">Template guidance</span>
</div>

## How to read this page

This chapter is intentionally narrow. It does not try to re-document the entire document subsystem. Instead, it traces the **integration boundary** between:

- document metadata shape;
- document relationship definitions;
- document derivation definitions;
- the universal runtime service that exposes documents to the rest of the platform.

Use this page together with:

- [Document Subsystem Dense Source Map](/platform/document-subsystem-dense-source-map)
- [Definitions and Metadata Boundary Dense Source Map](/platform/definitions-metadata-boundary-dense-source-map)
- [Document Subsystem Verified Anchors](/reference/document-subsystem-verified-anchors)
- [Definitions/Metadata Boundary Verified Anchors](/reference/definitions-metadata-boundary-verified-anchors)

## Verified anchors used in this chapter

- `NGB.Application.Abstractions/Services/IDocumentService.cs`
- `NGB.Runtime/Documents/DocumentService.cs`
- `NGB.Runtime/Documents/Derivations/IDocumentDerivationService.cs`
- `NGB.Definitions/DefinitionsRegistry.cs`
- `NGB.Definitions/Documents/Derivations/DocumentDerivationDefinition.cs`
- `NGB.Definitions/Documents/Relationships/DocumentRelationshipTypeDefinition.cs`
- `NGB.Metadata/Documents/Hybrid/DocumentTableMetadata.cs`
- `NGB.Metadata/Documents/Hybrid/DocumentColumnMetadata.cs`
- `NGB.Persistence/Documents/IDocumentRepository.cs`
- `NGB.Persistence/Documents/Universal/IDocumentReader.cs`
- `NGB.Persistence/Documents/Universal/IDocumentWriter.cs`
- `NGB.Persistence/Documents/Universal/IDocumentPartsReader.cs`
- `NGB.Persistence/Documents/Universal/IDocumentPartsWriter.cs`

## What this boundary is responsible for

The document subsystem in NGB is not just CRUD for arbitrary head tables. The verified anchors show a more structured model:

1. **Metadata** describes the physical and UI-facing shape of a document type.
2. **Definitions** register semantic rules that sit above metadata:
   - the document type itself;
   - relationship types between documents;
   - derivation actions that create one document from another.
3. **Runtime** exposes that model through one universal service: `IDocumentService`.
4. **Persistence** provides the generic registry and universal head/parts readers and writers that let the runtime execute metadata-driven behavior.

That is the core integration point: runtime is not inventing document semantics on the fly. It is consuming a definition+metadata graph and turning it into platform behavior.

## Layer-by-layer view

### 1. Metadata layer: physical and structural shape

The verified metadata anchors show that document shape is described in a hybrid table model.

`DocumentTableMetadata` carries:

- physical table name;
- table kind;
- list of columns;
- optional indexes;
- optional part code.

That means metadata is allowed to represent both:

- the **head table** for the document;
- the **tabular part tables** for the document.

`DocumentColumnMetadata` carries:

- physical column name;
- data type;
- required flag;
- optional UI label;
- optional lookup source;
- optional mirrored relationship metadata;
- optional options list.

This is important because the runtime can use one metadata object for several purposes at once:

- persistence validation and mapping;
- form/list metadata projection;
- lookup resolution;
- special relationship-aware fields.

### 2. Definitions layer: semantic registration

`DefinitionsRegistry` is the verified anchor that shows definitions are stored as an immutable, pre-resolved snapshot. It contains independent dictionaries for:

- documents;
- catalogs;
- document relationship types;
- document derivations.

This is the strongest available evidence that NGB treats relationship types and derivation actions as first-class platform definitions, not as incidental runtime conventions.

#### Relationship definitions

`DocumentRelationshipTypeDefinition` carries:

- code;
- name;
- bidirectionality flag;
- cardinality;
- optional allowed source types;
- optional allowed target types.

It also exposes computed limits:

- `MaxOutgoingPerFrom`
- `MaxIncomingPerTo`

Those computed values come directly from the declared cardinality.

This is a very important integration signal: relationship semantics are not merely labels such as `created_from` or `based_on`. The definition object can constrain allowed graph shape.

#### Derivation definitions

`DocumentDerivationDefinition` carries:

- derivation code;
- action name;
- source document type;
- target document type;
- relationship codes to write;
- optional handler type.

The verified summary comment on this type is especially revealing:

- derivation is a declarative platform feature for “Create based on”;
- it creates a new **draft** of the target type;
- it can optionally invoke a handler for prefilling;
- it writes outgoing relationship edges from the derived draft to the source document.

That gives a direct bridge from declarative definition to runtime behavior.

### 3. Runtime layer: universal document API

`IDocumentService` is the public application-facing contract. The verified method list shows that runtime exposes one unified surface for:

- metadata discovery;
- list/detail reads;
- cross-type lookup;
- draft create/update/delete;
- post/unpost/repost;
- mark/unmark for deletion;
- derivation actions;
- relationship graph;
- effects;
- actual derivation.

This is the cleanest proof that document metadata, document lifecycle, document graph, and document effects are intentionally presented as one coherent runtime API.

## How DocumentService consumes metadata and definitions

`DocumentService` is the central verified implementation anchor.

### Metadata consumption

The service resolves a `DocumentModel` from the requested `documentType`. The verified code shows that this step includes:

- loading document type metadata from a registry;
- resolving the head table;
- extracting scalar head columns;
- validating presentation fields such as `AmountField`;
- building a `DocumentHeadDescriptor`.

From there, all core document operations run through generic readers and writers instead of typed handlers:

- list page → `IDocumentReader.CountAsync` + `GetPageAsync`
- detail → `IDocumentReader.GetByIdAsync` + `IDocumentPartsReader.GetPartsAsync`
- draft save → `IDocumentWriter.UpsertHeadAsync`
- parts replace → `IDocumentPartsWriter.ReplacePartsAsync`

This is the universal metadata-driven path.

### Definition consumption for derivations

The verified `DeriveInternalAsync` path shows that runtime does not hardcode derivation rules. Instead it:

1. loads the source document record;
2. asks derivation infrastructure for actions available for the source type;
3. filters them by target type and relationship code;
4. resolves the selected derivation action;
5. delegates actual draft creation to `IDocumentDerivationService`.

This is where the definition boundary is the most concrete. `DocumentService` is not the owner of derivation rules. It is the orchestrator that finds the matching declarative rule and then invokes derivation infrastructure.

### Definition consumption for relationship graph

The verified constructor of `DocumentService` includes `IDocumentRelationshipGraphReadService`, and the public contract includes `GetRelationshipGraphAsync`.

From the verified implementation flow we can say confidently:

- the graph is loaded as a dedicated domain read model;
- runtime then enriches it with universal document head rows through a bulk cross-type read;
- node display/status/amount are shaped for the UI at runtime.

So the document graph feature is also definition-aware and metadata-aware, but surfaced through one runtime endpoint.

## Persistence boundary in this integration

The verified persistence interfaces show a strict split.

### Common registry repository

`IDocumentRepository` is explicitly documented as the repository for the shared `documents` table. The interface comment states:

- only the common header lives there;
- per-type data belongs in `doc_{type_code}` and `doc_{type_code}__{part}`;
- state transitions must use `GetForUpdateAsync` inside an active transaction.

This is a strong architectural statement. It means document definitions and metadata sit above a stable storage pattern:

- one shared registry row per document;
- typed head table;
- typed part tables.

### Universal readers and writers

The verified universal interfaces confirm how runtime uses metadata:

- `IDocumentReader` works with `DocumentHeadDescriptor`
- `IDocumentWriter` upserts one head row given metadata-shaped values
- `IDocumentPartsReader` reads raw part rows by physical table
- `IDocumentPartsWriter` replaces rows by document for the specified part tables

This is the mechanical boundary where metadata becomes executable persistence behavior.

## Integration flow: “Create based on”

The verified anchors are already enough to reconstruct the high-level flow.

1. A caller asks `IDocumentService.DeriveAsync(...)`.
2. `DocumentService` validates source and target.
3. It resolves a matching derivation definition by source type, target type, and relationship code.
4. It delegates to `IDocumentDerivationService.CreateDraftAsync(...)`.
5. The derivation infrastructure uses the declared relationship codes and optional handler.
6. The result is a **draft** target document linked to the source document.

The key architectural point is that this flow crosses all three layers:

- **Definitions** decide what derivations exist.
- **Metadata** decides how the new document is shaped and persisted.
- **Runtime** orchestrates the action and returns a document DTO.

## Integration flow: Relationship graph

The verified anchors also support a smaller but still important flow.

1. A caller asks `IDocumentService.GetRelationshipGraphAsync(...)`.
2. Runtime loads graph structure through relationship-graph read infrastructure.
3. Runtime resolves metadata models for every type present in the graph.
4. Runtime bulk-loads head rows for graph nodes across types.
5. Runtime shapes node titles, subtitles, status, and amount for the UI.

This is an example of how relationship definitions and document metadata converge at read time, not only at write time.

## What is verified vs inferred here

### Verified directly from source

- Relationship types are separately registered in `DefinitionsRegistry`.
- Derivation definitions are separately registered in `DefinitionsRegistry`.
- Derivation definitions carry relationship codes and optional handler type.
- `IDocumentService` exposes both `GetRelationshipGraphAsync` and `DeriveAsync`.
- `DocumentService` consumes metadata and generic readers/writers for universal document operations.
- `IDocumentRepository` owns the common `documents` registry and documents the typed table pattern.
- `IDocumentPartsWriter` uses replace-by-document semantics for draft parts.
- `DocumentColumnMetadata` can contain lookup metadata and mirrored relationship metadata.

### Architecture synthesis

- Relationship definitions likely govern more than UI labels; they represent enforceable graph semantics because the definition type includes cardinality-derived limits.
- Metadata and definitions together form the stable contract between vertical modules and generic runtime.
- The document subsystem is intentionally designed so that graph/effects/derivations are not bolt-ons but part of the same universal service surface.

### Template guidance

When documenting or extending a real vertical module, describe it using this sequence:

1. **Document metadata shape**
2. **Relationship types**
3. **Derivation definitions**
4. **Runtime integration point**
5. **Persistence tables**
6. **Read surfaces** such as graph and effects

That sequence matches the platform architecture much better than starting from controllers or UI forms.

## Where to go next

- [Document Subsystem Dense Source Map](/platform/document-subsystem-dense-source-map)
- [Definitions and Metadata Boundary Dense Source Map](/platform/definitions-metadata-boundary-dense-source-map)
- [Runtime Execution Core Dense Source Map](/platform/runtime-execution-core-dense-source-map)
- [Document + Definitions Integration Verified Anchors](/reference/document-definitions-integration-verified-anchors)
