---
title: "Document Subsystem Collaborators Map"
description: "Verified collaborator-oriented map of the NGB document subsystem based on confirmed interfaces and runtime orchestration."
---

# Document Subsystem Collaborators Map

## Goal of this page

This page is the companion to the dense source map. The dense map explains the subsystem in execution order. This page explains the same subsystem by collaborator role.

Use it when you want to answer questions like:

- which collaborator owns common document state?
- which collaborator owns typed head reads and writes?
- where do tabular parts live?
- where does derive plug in?
- which collaborators are orchestration-only from the currently verified evidence set?

## Verified collaborator table

| Collaborator | Verified source anchor | Role visible from anchor |
|---|---|---|
| `IDocumentService` | `NGB.Application.Abstractions/Services/IDocumentService.cs` | Public application contract for generic documents |
| `DocumentService` | `NGB.Runtime/Documents/DocumentService.cs` | Runtime orchestration center |
| `IDocumentRepository` | `NGB.Persistence/Documents/IDocumentRepository.cs` | Registry row access and serialized workflow state changes |
| `IDocumentReader` | `NGB.Persistence/Documents/Universal/IDocumentReader.cs` | Universal typed head-table reads |
| `IDocumentWriter` | `NGB.Persistence/Documents/Universal/IDocumentWriter.cs` | Universal typed head-table upserts |
| `IDocumentPartsReader` | `NGB.Persistence/Documents/Universal/IDocumentPartsReader.cs` | Universal part-table reads |
| `IDocumentPartsWriter` | `NGB.Persistence/Documents/Universal/IDocumentPartsWriter.cs` | Universal part-table replace semantics for drafts |
| `IDocumentDerivationService` | `NGB.Runtime/Documents/Derivations/IDocumentDerivationService.cs` | Platform derive / create-based-on service |

## Collaborator groups

### 1. Application surface

`IDocumentService` is the contract that the rest of the platform and vertical hosts can depend on.

It deliberately hides the split between:

- document registry state
- typed head persistence
- parts persistence
- posting
- derivation
- explainability read models

That means callers interact with one application service while the runtime composes many collaborators underneath.

### 2. Runtime orchestration

`DocumentService` is where the subsystem becomes operational.

From the verified source, it clearly acts as the point where the platform:

- resolves document type metadata
- translates API payloads into typed head values and part rows
- delegates persistence
- delegates posting
- delegates derive
- delegates graph/effects reads
- shapes DTOs for UI and API consumers

### 3. Common registry state

`IDocumentRepository` is the collaborator that owns the `documents` registry row.

From the verified contract, registry state includes:

- identity and common header access
- row locking for state transitions
- status changes
- common draft-header updates
- one-time number assignment
- delete attempts

This collaborator is the clearest verified anchor for concurrency-sensitive document workflow behavior.

### 4. Typed head persistence

`IDocumentReader` and `IDocumentWriter` are the typed head persistence pair.

Their contracts show that document-type specific scalar data is accessed through `DocumentHeadDescriptor` rather than through one repository per document type.

This is one of the strongest concrete confirmations of the metadata-driven document model in NGB.

### 5. Part persistence

`IDocumentPartsReader` and `IDocumentPartsWriter` handle tabular parts.

The most important verified semantic here is that draft parts are replaced as a set for the document rather than mutated row-by-row through the generic runtime contract.

### 6. Derive

`IDocumentDerivationService` is the collaborator that converts derivation definitions into actual draft creation behavior.

The verified contract explicitly ties derive to shared definitions and relationship creation rules.

## What is visible only as a constructor dependency today

The following collaborators are visible in the verified `DocumentService` constructor, but their source contracts were not independently confirmed in the current anchor set:

- `IDocumentDraftService`
- `IDocumentPostingService`
- posting action resolvers
- graph read service
- effects query service
- audit log service
- reference payload enricher
- draft payload validators
- UI effects contributors

For these collaborators, the docs should currently describe role and boundary only, not full implementation details.

## Practical reading advice

When you update this chapter later, add new collaborators only when at least one of the following is true:

- the exact source file was verified directly
- the collaborator is visible from a verified constructor or contract and you clearly label the description as boundary-level rather than implementation-level

That keeps this page useful without drifting into unverified internals.
