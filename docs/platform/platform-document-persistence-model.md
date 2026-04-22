---
title: Platform document persistence model
description: The registry-head-parts persistence model that makes metadata-driven document CRUD possible in NGB.
---

# Platform document persistence model

<div class="doc-badge-row">
  <span class="doc-badge doc-badge--verified">Verified</span>
  <span class="doc-badge doc-badge--inferred">Inferred</span>
</div>

This page is the focused companion to the general persistence chapter. It explains the core document storage model that makes universal document CRUD possible.

## Verified anchors

```text
NGB.Persistence/Documents/IDocumentRepository.cs
NGB.Persistence/Documents/Universal/IDocumentReader.cs
NGB.Persistence/Documents/Universal/IDocumentWriter.cs
NGB.Persistence/Documents/Universal/IDocumentPartsReader.cs
NGB.Persistence/Documents/Universal/IDocumentPartsWriter.cs
NGB.Runtime/Documents/DocumentService.cs
```

## The three-layer storage model

### 1. Common registry row in `documents`

This is the row that the runtime locks for workflow transitions and common header changes.

### 2. Typed head table per document type

Each document type has a typed head table such as `doc_{type_code}`.

The runtime accesses it through a head descriptor instead of through a bespoke service for every document type.

### 3. Typed part tables per part

Tabular sections live in `doc_{type_code}__{part}` tables.

The verified parts writer contract documents draft replace semantics: delete all rows for the document, then insert the current draft rows in one transaction.

## Why the split matters

This split solves a classic business-platform problem.

If you keep everything in one generic JSON store, the database becomes weakly typed. If you create hand-written persistence for every document, the platform loses reuse.

NGB chooses a middle path:

- generic runtime orchestration;
- typed relational storage;
- metadata-driven descriptors in between.

## What DocumentService proves

The verified `DocumentService` constructor shows the actual collaboration model:

- registry repository for common state;
- universal readers/writers for typed head data;
- universal parts readers/writers for typed part data;
- posting, derivation, graph, effects, audit, and UI collaborators on top.

That is the practical proof that document persistence is not a side concern. It is the base of the whole execution core.

## Related pages

- [Persistence](/platform/persistence)
- [Document Subsystem Dense Source Map](/platform/document-subsystem-dense-source-map)
- [Database Naming and DDL Patterns](/reference/database-naming-and-ddl-patterns)
