---
title: "Definitions and Metadata Collaborators Map"
description: "Collaborator-oriented view of how Metadata, Definitions, Derivations, and Runtime interact in NGB."
---

# Definitions and Metadata Collaborators Map

<div class="doc-badges">
  <span class="doc-badge doc-badge-verified">Verified anchors</span>
  <span class="doc-badge doc-badge-template">Template interpretation for extension work</span>
</div>

## Collaborator map

```text
NGB.Metadata
  ├─ DocumentTableMetadata
  └─ DocumentColumnMetadata
          ↓
NGB.Definitions
  ├─ DefinitionsRegistry
  └─ DocumentDerivationDefinition
          ↓
NGB.Runtime
  ├─ DocumentService
  └─ IDocumentDerivationService
```

## Verified collaborator roles

### `DocumentTableMetadata`
Structural table description used by Runtime to understand head and part tables.

### `DocumentColumnMetadata`
Structural column description used by Runtime for:
- allowed field checks
- required-field validation
- lookup and options metadata
- DTO metadata construction

### `DefinitionsRegistry`
Immutable registry for:
- document definitions
- catalog definitions
- relationship type definitions
- derivation definitions

### `DocumentDerivationDefinition`
Declarative configuration for platform-level draft derivation:
- source type
- target type
- relationship codes
- optional handler type

### `DocumentService`
Runtime orchestrator that:
- resolves type metadata
- builds effective document models
- validates payloads against metadata
- exposes derivation actions
- delegates draft derivation to derivation service

### `IDocumentDerivationService`
Runtime boundary for:
- listing derivation actions
- creating derived drafts from registered derivation definitions

## Execution-oriented reading

The verified-source interpretation is:

1. metadata records describe the **shape**
2. definitions registry describes the **registered business universe**
3. runtime services execute against that universe
4. derivation service is a runtime action engine driven by definitions

## Extension checklist

This is template guidance inferred from verified anchors.

### Add a new structural field
Touch Metadata first.

### Add a new business type
Touch Definitions and whatever registry/module wiring feeds Runtime.

### Add a new “create based on” action
Touch Definitions first via derivation definition, then supply handler behavior if needed.

## Related pages

- [Definitions and Metadata Boundary Dense Source Map](/platform/definitions-metadata-boundary-dense-source-map)
- [Definitions/Metadata Verified Anchors](/reference/definitions-metadata-boundary-verified-anchors)
