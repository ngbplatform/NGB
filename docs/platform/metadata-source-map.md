---
title: Metadata source map
---

# Metadata source map

This page explains how to read `NGB.Metadata` even when you start from runtime consumers rather than from every metadata type definition file.

## Confirmed source anchors

```text
NGB.Metadata/NGB.Metadata.csproj
NGB.Runtime/Documents/DocumentService.cs
```

## What the project boundary tells you first

`NGB.Metadata/NGB.Metadata.csproj` is intentionally small.

It depends on:

- `NGB.Tools`
- `NGB.Core`

and nothing heavier.

That is a strong design signal: **Metadata is a foundational descriptive model**, not a runtime engine and not an infrastructure provider.

## Why DocumentService is the best consumer-side anchor

`NGB.Runtime/Documents/DocumentService.cs` imports and uses metadata types from namespaces such as:

- `NGB.Metadata.Base`
- `NGB.Metadata.Documents.Hybrid`
- `NGB.Metadata.Documents.Storage`

The file consumes metadata types such as:

- document type metadata;
- table metadata;
- column metadata;
- list filter metadata;
- lookup-source metadata;
- mirrored document relationship metadata.

That makes `DocumentService.cs` the fastest way to understand what metadata is *for*.

## What Metadata is responsible for

The module describes the shape of business artifacts without executing them.

In practical terms, metadata is where the platform models things like:

- which tables belong to a document or catalog;
- which columns exist and what types they have;
- which fields are required;
- which lookups a field uses;
- what the UI label/presentation hints are;
- which relationships are mirrored or special;
- which filters a list exposes.

## What Metadata is not responsible for

Metadata should not:

- run posting logic;
- execute SQL;
- own runtime workflow decisions;
- materialize HTTP controllers;
- hide vertical business logic.

It describes. Other modules execute.

## How to read Metadata through Runtime

In `DocumentService.cs`, watch these moments carefully:

- `GetModel(...)` — runtime resolves a document model from metadata;
- list query building — metadata-driven filters become runtime queries;
- payload parsing and validation — metadata drives required fields and type conversion;
- DTO/form/list shaping — metadata becomes UI-ready contract shape.

That is the clearest proof that metadata is the platform’s descriptive backbone.

## Recommended reading order

```text
1. NGB.Metadata/NGB.Metadata.csproj
2. NGB.Runtime/Documents/DocumentService.cs
```

Then, in your local checkout, continue directly into the metadata type definitions referenced by `DocumentService`.

## Safe change rules for Metadata

### Keep metadata declarative

Do not move orchestration or SQL behavior into metadata classes.

### Keep metadata reusable across verticals

Avoid hard-wiring Property Management or Trade semantics into shared metadata types.

### Favor explicit field/table descriptors

If Runtime needs to reason about a document or catalog shape, express that in metadata rather than in scattered special cases.
