---
title: "Definitions and Metadata Boundary Dense Source Map"
description: "Verified source-anchored chapter for how NGB Metadata and Definitions feed Runtime behaviors."
---

# Definitions and Metadata Boundary Dense Source Map

<div class="doc-callout">
  <strong>Scope.</strong> This chapter documents the boundary between <code>NGB.Metadata</code>, <code>NGB.Definitions</code>, and <code>NGB.Runtime</code> using only verified source anchors. It explains how declarative shape and definition registries become executable runtime behavior.
</div>

<div class="doc-badges">
  <span class="doc-badge doc-badge-verified">Verified anchors only</span>
  <span class="doc-badge doc-badge-inferred">Runtime synthesis included</span>
</div>

## Verified anchors used in this chapter

- `NGB.Metadata/NGB.Metadata.csproj`
- `NGB.Definitions/NGB.Definitions.csproj`
- `NGB.Definitions/DefinitionsRegistry.cs`
- `NGB.Definitions/Documents/Derivations/DocumentDerivationDefinition.cs`
- `NGB.Metadata/Documents/Hybrid/DocumentTableMetadata.cs`
- `NGB.Metadata/Documents/Hybrid/DocumentColumnMetadata.cs`
- `NGB.Runtime/Documents/DocumentService.cs`
- `NGB.Runtime/Documents/Derivations/IDocumentDerivationService.cs`
- `NGB.Runtime/NGB.Runtime.csproj`

## Why this boundary matters

At the platform level, NGB separates three concerns:

1. **Metadata shape** — what a catalog or document looks like structurally.
2. **Definitions registry** — what business types and derivation behaviors are registered.
3. **Runtime execution** — how those registered definitions and metadata are interpreted to serve CRUD, derivation, posting-adjacent actions, and document flow.

That split is visible directly in project dependencies and in the runtime collaborators verified for this documentation set.

## 1. Metadata: structural shape, not execution

The `NGB.Metadata` project references only `NGB.Tools` and `NGB.Core`, which confirms it sits low in the stack and does not depend on Runtime or PostgreSQL.

### Verified structural record: `DocumentTableMetadata`

`NGB.Metadata/Documents/Hybrid/DocumentTableMetadata.cs` defines document table metadata as:

- physical table name
- table kind
- columns
- indexes
- optional `PartCode`

This is the platform’s structural description of a head table or part table.

### Verified structural record: `DocumentColumnMetadata`

`NGB.Metadata/Documents/Hybrid/DocumentColumnMetadata.cs` defines column-level shape:

- `ColumnName`
- `Type`
- `Required`
- `MaxLength`
- `UiLabel`
- `Lookup`
- `MirroredRelationship`
- `Options`

This is important because `DocumentService` later consumes these fields to:

- validate payload shape
- generate metadata DTOs
- interpret lookup and option metadata
- apply special handling for mirrored relationships and presentation hints

## 2. Definitions: immutable business registry above metadata

`NGB.Definitions/NGB.Definitions.csproj` references:

- `NGB.Metadata`
- `NGB.Core`
- `NGB.Accounting`
- `NGB.OperationalRegisters`
- `NGB.ReferenceRegisters`
- `NGB.Persistence`

That dependency shape shows `Definitions` is not just UI description. It is the declarative business registry layer that can talk about accounting, registers, persistence-facing concepts, and metadata-backed types.

### Verified registry: `DefinitionsRegistry`

`NGB.Definitions/DefinitionsRegistry.cs` is the clearest anchor for the module boundary.

It is an immutable snapshot that contains four definition families:

- documents
- catalogs
- document relationship types
- document derivations

The registry exposes:

- `Documents`
- `Catalogs`
- `DocumentRelationshipTypes`
- `DocumentDerivations`

and explicit getters/try-getters such as:

- `GetDocument`
- `GetCatalog`
- `GetDocumentRelationshipType`
- `GetDocumentDerivation`

This matters architecturally because it confirms that Runtime does not discover these things ad hoc from the database or from the Web layer. It resolves them from an immutable, normalized definition snapshot.

## 3. Derivations: declarative business action definitions

`NGB.Definitions/Documents/Derivations/DocumentDerivationDefinition.cs` provides a very strong verified anchor for the "Create based on" / "Enter based on" platform feature.

The definition contains:

- `Code`
- `Name`
- `FromTypeCode`
- `ToTypeCode`
- `RelationshipCodes`
- `HandlerType`

The comments in that file are especially important:

- derivation creates a new **draft**
- the derived document is linked to the source via relationship codes
- `HandlerType` can perform domain-specific prefilling inside the same transaction
- the feature is platform-only and not tied to Web/API

That gives us a verified architectural conclusion: derivation is not a UI shortcut. It is a first-class platform definition concept.

## 4. Runtime consumes metadata and definitions together

The strongest runtime anchor for this boundary is `NGB.Runtime/Documents/DocumentService.cs`.

Verified facts from that file:

- it resolves document type metadata through `IDocumentTypeRegistry`
- it constructs an internal `DocumentModel`
- that model uses head-table metadata and scalar columns from document metadata
- it interprets list filters, amount field, form metadata, part metadata, and presentation metadata
- it calls `IDocumentDerivationService` for derivation actions and draft creation
- it uses metadata to convert payloads into typed head values and typed part rows
- it uses definitions-backed type metadata to build `DocumentTypeMetadataDto`

### What Runtime is doing at this boundary

From verified code, Runtime is not merely persisting JSON-like payloads. It is translating declarative metadata and definition state into executable behavior:

- validating which fields are allowed
- deciding which columns are required
- deciding which part tables exist
- building DTO metadata returned to the client
- resolving derivation actions available for a source document
- choosing amount field semantics for relationship graph presentation

## 5. Verified derivation boundary into Runtime

`NGB.Runtime/Documents/Derivations/IDocumentDerivationService.cs` explicitly documents that the service implements the platform feature “Create based on” and that it uses `DocumentDerivationDefinition` registered in `DefinitionsRegistry`.

That single verified sentence is load-bearing:

- `DefinitionsRegistry` is the authoritative source of derivation declarations
- Runtime derivation behavior is driven from those definitions
- the service creates a draft and writes relationships according to the definition rules

This directly links `NGB.Definitions` to `NGB.Runtime` without any speculation.

## 6. Practical synthesis: what belongs where

### Metadata owns

- table and column shape
- UI labels and options
- lookup metadata
- part structure
- mirrored relationship metadata

### Definitions owns

- what business types exist
- catalog/document registration
- relationship-type registration
- derivation registration
- declarative business mappings that are broader than a single table shape

### Runtime owns

- interpreting metadata and definitions
- validating request payloads
- building metadata DTOs for clients
- orchestrating derivation and graph/effects calls
- turning declarative shape into execution

## 7. What this boundary prevents

This split avoids a common ERP/platform anti-pattern where:

- metadata becomes an overpowered executable runtime container, or
- runtime hardcodes type-specific behavior without a declarative registry.

Instead, NGB keeps:

- **shape** in Metadata
- **registration and business declarations** in Definitions
- **execution** in Runtime

That is a strong production-oriented separation because it lets vertical modules add new types and derivations without rewriting runtime orchestration.

## 8. Extension guidance from verified anchors

This section is template guidance built from verified anchors.

### When adding a new document type

You should expect work in at least three layers:

1. **Metadata layer**
   - define head table / part table structure
   - define columns, required flags, lookups, options

2. **Definitions layer**
   - register the document type in the definitions snapshot
   - register any relationship types or derivations connected to it

3. **Runtime-facing integration**
   - ensure the type registry sees the new type
   - ensure payload validation and universal readers/writers can interpret its metadata
   - optionally add derivation handlers or UI effect contributors

### When adding a derivation

Verified anchors strongly suggest the workflow is:

1. declare `DocumentDerivationDefinition`
2. point it to `FromTypeCode`, `ToTypeCode`, and relationship codes
3. optionally provide a `HandlerType`
4. let `IDocumentDerivationService` create the derived draft and relationships

## Related pages

- [Document Subsystem Dense Source Map](/platform/document-subsystem-dense-source-map)
- [Runtime Execution Core Dense Source Map](/platform/runtime-execution-core-dense-source-map)
- [Definitions and Metadata](/architecture/definitions-and-metadata)
- [Definitions/Metadata Verified Anchors](/reference/definitions-metadata-boundary-verified-anchors)
