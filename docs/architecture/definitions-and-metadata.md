---
title: "Definitions and Metadata"
description: "How metadata and definitions shape catalogs, documents, reports, and platform behavior."
---

# Definitions and Metadata

<div class="doc-status-row">
  <span class="doc-badge doc-badge-verified">Verified module boundaries</span>
  <span class="doc-badge doc-badge-inferred">Platform model interpretation</span>
  <span class="doc-badge doc-badge-template">Extension guidance</span>
</div>

> Deep links:
> - [Metadata](/platform/metadata)
> - [Definitions](/platform/definitions)
> - [Platform Extension Points](/guides/platform-extension-points)

## Why these two modules matter

In NGB, metadata and definitions are the platform’s descriptive language.

- **Metadata** answers: what fields/tables/lookups/presentation elements exist?
- **Definitions** answers: what catalog/document/report/register behavior is part of the platform and how is it assembled for execution?

## Verified anchors

- `NGB.Metadata/NGB.Metadata.csproj`
- `NGB.Definitions/NGB.Definitions.csproj`

## Working model

### Metadata layer
Metadata is the neutral descriptive substrate. It should be stable, reusable, and free from request-execution concerns.

Typical responsibilities include:

- entity and field descriptors
- table/head/part structure
- presentation metadata
- lookup metadata
- typed declarative shape that runtime can consume

### Definitions layer
Definitions package metadata into executable business features.

Typical responsibilities include:

- registering catalogs/documents/reports
- connecting definitions to accounting/register semantics
- exposing reusable definition sets to runtime and hosts
- acting as the bridge from “description” to “platform feature”

## Why the split is useful

Without this split, platforms tend to mix:

- DTO contracts
- UI hints
- business descriptors
- request-time behavior
- storage details

NGB’s split makes it easier to:

- keep the platform extensible
- reuse the same descriptive model in multiple verticals
- avoid binding API/storage decisions into the definition language too early

## How to use this in extension work

When adding a new business capability, ask:

1. Is this **descriptive shape**?  
   Put it closer to metadata.

2. Is this **registered platform feature**?  
   Put it closer to definitions.

3. Is this **execution/orchestration**?  
   Put it in runtime.

4. Is this **SQL/storage implementation**?  
   Put it in PostgreSQL infrastructure.

## Continue with

- [Metadata source map](/platform/metadata-source-map)
- [Definitions source map](/platform/definitions-source-map)
- [Developer Workflows](/guides/developer-workflows)
