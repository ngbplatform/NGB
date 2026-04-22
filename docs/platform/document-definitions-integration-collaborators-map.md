---
title: "Document + Definitions Integration Collaborators Map"
description: "Collaborator-oriented map of how definitions, metadata, runtime, and persistence meet in the document subsystem."
---

# Document + Definitions Integration Collaborators Map

## How to read this page

This page is the compact companion to:

- [Document + Definitions Integration Dense Source Map](/platform/document-definitions-integration-dense-source-map)

It is meant for fast orientation. Each block shows a verified collaborator and the role it plays in the integration boundary.

## Core collaborator map

### `DefinitionsRegistry`

Role:
- immutable source of registered document, catalog, relationship, and derivation definitions.

Why it matters:
- it is the verified handoff point between vertical definition registration and platform runtime consumption.

### `DocumentRelationshipTypeDefinition`

Role:
- declarative semantics for relationship edges.

What it contributes:
- relationship code and display identity;
- bidirectionality;
- cardinality;
- type restrictions.

### `DocumentDerivationDefinition`

Role:
- declarative semantics for “Create based on”.

What it contributes:
- source type;
- target type;
- relationship codes to emit;
- optional prefilling handler.

### `DocumentTableMetadata`

Role:
- physical and logical shape of a document table.

What it contributes:
- head vs part distinction;
- physical table name;
- column collection;
- optional part code.

### `DocumentColumnMetadata`

Role:
- column-level description used by runtime and UI shaping.

What it contributes:
- physical name;
- type;
- required flag;
- lookup;
- mirrored relationship metadata;
- options.

### `IDocumentService`

Role:
- public runtime contract for universal document behavior.

Why it matters:
- graph, derivation, lifecycle, lookup, and effects are all presented through one coherent service.

### `DocumentService`

Role:
- integration orchestrator.

What it actually connects:
- document metadata;
- document definitions;
- generic persistence;
- derivation infrastructure;
- relationship graph read infrastructure;
- effect queries.

### `IDocumentDerivationService`

Role:
- specialized platform service that executes derivation definitions.

Why it matters:
- keeps `DocumentService` orchestration-focused instead of embedding derivation mechanics directly.

### `IDocumentRepository`

Role:
- common document registry repository.

Why it matters:
- anchors the split between shared header row and typed document storage.

### `IDocumentReader` / `IDocumentWriter`

Role:
- universal head-table read/write boundary driven by descriptors.

Why they matter:
- this is how metadata becomes executable without per-document-type repositories in runtime.

### `IDocumentPartsReader` / `IDocumentPartsWriter`

Role:
- universal tabular-parts boundary.

Why they matter:
- they let runtime treat parts generically while still preserving typed physical tables.

## Integration narrative in one paragraph

Definitions decide **what a document means in the graph**. Metadata decides **how a document is shaped in storage and UI**. Runtime, through `DocumentService`, decides **how those two models become operations** such as draft creation, derivation, relationship graph reads, and effects reads. Persistence supplies the generic execution primitives that make this universal model practical.

## Verified anchor list

- `NGB.Definitions/DefinitionsRegistry.cs`
- `NGB.Definitions/Documents/Relationships/DocumentRelationshipTypeDefinition.cs`
- `NGB.Definitions/Documents/Derivations/DocumentDerivationDefinition.cs`
- `NGB.Metadata/Documents/Hybrid/DocumentTableMetadata.cs`
- `NGB.Metadata/Documents/Hybrid/DocumentColumnMetadata.cs`
- `NGB.Application.Abstractions/Services/IDocumentService.cs`
- `NGB.Runtime/Documents/DocumentService.cs`
- `NGB.Runtime/Documents/Derivations/IDocumentDerivationService.cs`
- `NGB.Persistence/Documents/IDocumentRepository.cs`
- `NGB.Persistence/Documents/Universal/IDocumentReader.cs`
- `NGB.Persistence/Documents/Universal/IDocumentWriter.cs`
- `NGB.Persistence/Documents/Universal/IDocumentPartsReader.cs`
- `NGB.Persistence/Documents/Universal/IDocumentPartsWriter.cs`
