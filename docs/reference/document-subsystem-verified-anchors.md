---
title: "Document Subsystem Verified Anchors"
description: "Compact evidence index for the verified source anchors currently used by the NGB document subsystem documentation."
---

# Document Subsystem Verified Anchors

This page is a compact evidence index for the document subsystem chapter work.

Use it when you need a short list of the exact files that were verified directly and already incorporated into the documentation.

## Verified anchors

### Application contract

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

## What these anchors already support confidently

From these files, the docs can already support the following statements with high confidence:

- documents are exposed through a universal application service rather than one service per document type
- the runtime center for this subsystem is `DocumentService`
- the platform splits registry-state persistence from typed head/part persistence
- common registry state lives in `documents`
- typed head tables follow the `doc_{type}` pattern
- part tables follow the `doc_{type}__{part}` pattern
- tabular parts use replace-by-document semantics for drafts
- derive is a platform service backed by shared definitions
- relationship graph and effects are first-class capabilities on the document API surface

## What still needs direct verification

These are the highest-value next anchors for a future documentation expansion:

- posting service contract and implementation
- posting action resolver contracts and implementations
- graph read service contract and implementation
- effects query service contract and implementation
- document draft service contract and implementation
- document type registry / metadata resolution path
- audit log service contract and implementation used by the document runtime

## Where this evidence is used

This evidence set is currently used by:

- `Document Subsystem Dense Source Map`
- `Document Subsystem Collaborators Map`
- the broader source-anchored class map layer where document runtime behavior is summarized
