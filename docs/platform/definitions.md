---
title: "Definitions"
description: "Role of NGB.Definitions in packaging reusable platform features for runtime consumption."
---

# Definitions

<div class="doc-status-row">
  <span class="doc-badge doc-badge-verified">Verified module boundary</span>
  <span class="doc-badge doc-badge-inferred">Feature-pack interpretation</span>
</div>

> File-level companion page: [Definitions source map](/platform/definitions-source-map)

## Verified anchor

- `NGB.Definitions/NGB.Definitions.csproj`

## Why this module matters

`NGB.Definitions` is the place where descriptive model and platform feature packaging come together.

Its verified dependency shape tells us it connects:

- metadata
- accounting
- operational registers
- reference registers
- persistence abstractions

That is exactly what you would expect from the module that packages reusable platform features for runtime.

## Working interpretation

Definitions are where the platform answers questions such as:

- what catalogs/documents/reports exist?
- which business engines participate?
- how should runtime see these features?
- what definitions are reusable across verticals?

## Use this page together with

- [Definitions and Metadata](/architecture/definitions-and-metadata)
- [Metadata](/platform/metadata)
- [Platform Extension Points](/guides/platform-extension-points)

## Important boundary

Definitions should stay closer to **describing and packaging** platform features than to executing them. Execution belongs in runtime; storage realization belongs in PostgreSQL.
