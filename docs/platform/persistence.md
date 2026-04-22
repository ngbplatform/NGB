---
title: Persistence
description: Persistence abstractions and universal document persistence model in the NGB platform.
---

# Persistence

<div class="doc-badge-row">
  <span class="doc-badge doc-badge--verified">Verified</span>
  <span class="doc-badge doc-badge--inferred">Inferred</span>
</div>

This page describes the persistence boundary that sits between runtime orchestration and provider-specific PostgreSQL implementations.

## Verified anchors

```text
NGB.Persistence/Documents/IDocumentRepository.cs
NGB.Persistence/Documents/Universal/IDocumentReader.cs
NGB.Persistence/Documents/Universal/IDocumentWriter.cs
NGB.Persistence/Documents/Universal/IDocumentPartsReader.cs
NGB.Persistence/Documents/Universal/IDocumentPartsWriter.cs
```

## The persistence split

NGB persistence is intentionally split into two layers:

- **provider-agnostic contracts** in `NGB.Persistence`;
- **provider-specific implementations** in `NGB.PostgreSql`.

That is the boundary that lets `NGB.Runtime` stay free of SQL and Npgsql details.

## Document persistence model

The verified interfaces show the core document persistence shape.

### Common registry row

`IDocumentRepository` owns the shared `documents` registry row.

That registry stores common document header state such as:

- identity;
- type code;
- workflow status;
- timestamps;
- common number/date header values.

### Typed head tables

`IDocumentReader` and `IDocumentWriter` operate on typed document head tables through `DocumentHeadDescriptor`.

That is how metadata-driven head fields are persisted without hard-coding every document in the runtime layer.

### Typed part tables

`IDocumentPartsReader` and `IDocumentPartsWriter` operate on typed part tables (`doc_*__*`).

The verified writer contract explicitly documents **replace-by-document semantics for drafts**.

## Why this design works well

This model gives NGB three strong properties:

- the runtime can stay generic and metadata-driven;
- the database can still keep typed, explicit tables;
- the platform does not need one bespoke CRUD stack per document type.

## Related pages

- [Platform document persistence model](/platform/platform-document-persistence-model)
- [Document Subsystem Dense Source Map](/platform/document-subsystem-dense-source-map)
- [Database Naming and DDL Patterns](/reference/database-naming-and-ddl-patterns)
