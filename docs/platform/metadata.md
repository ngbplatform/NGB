---
title: "Metadata"
description: "Role of NGB.Metadata as the descriptive substrate for platform entities and presentation."
---

# Metadata

<div class="doc-status-row">
  <span class="doc-badge doc-badge-verified">Verified module boundary</span>
  <span class="doc-badge doc-badge-inferred">Descriptive-model interpretation</span>
</div>

> File-level companion page: [Metadata source map](/platform/metadata-source-map)

## Verified anchor

- `NGB.Metadata/NGB.Metadata.csproj`

## What this module is for

`NGB.Metadata` is the descriptive substrate of the platform. It should express business/application structure without turning into execution code.

## Practical meaning

Metadata is where the platform can consistently describe things like:

- fields
- tables
- lookups
- UI/presentation hints
- entity/document/catalog structural shape

The exact object set is documented indirectly right now through runtime/document/report usage and project boundaries, even where individual metadata files were not directly anchor-verified in this session.

## Why this matters

Without a strong metadata layer, a platform like NGB would quickly duplicate the same structure in:

- runtime services
- API DTO logic
- UI rendering rules
- persistence mapping
- report configuration

A stable metadata layer is what lets the platform stay metadata-driven instead of ad hoc.

## Continue with

- [Definitions](/platform/definitions)
- [Definitions and Metadata](/architecture/definitions-and-metadata)
- [Platform Extension Points](/guides/platform-extension-points)
