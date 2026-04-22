---
title: "Documents, Flow, Effects, Derive"
description: "How universal document CRUD, lifecycle, effects, document flow, and derivation fit together in NGB."
---

# Documents, Flow, Effects, Derive

> **Page intent**
> This page is the chapter-level explanation of the universal document runtime: CRUD, lifecycle, effects surface, document relationship graph, and derivation workflow.

## Trust level

- **Verified anchors:** `NGB.Runtime/Documents/DocumentService.cs`
- **Architecture synthesis:** yes
- **Template guidance:** yes

## Chapter navigation

- [Runtime](/platform/runtime)
- [Runtime Source Map](/platform/runtime-source-map)
- [API Source Map](/platform/api-source-map)
- [Add Document with Accounting and Registers](/guides/add-document-with-accounting-and-registers)

## Verified source anchors

Confirmed in:

- `NGB.Runtime/Documents/DocumentService.cs`

The verified source shows that `DocumentService` is much more than a CRUD handler. It already centralizes or coordinates:

- document type metadata access;
- generic page/list reads;
- document lookup across types;
- draft create/update/delete;
- post/unpost/repost/mark-for-deletion/unmark-for-deletion;
- document relationship graph loading;
- document effects loading;
- derivation actions and draft derivation creation.

## What this tells us architecturally

NGB documents are not mere tables with controllers. The platform treats a document as:

- a metadata-defined type;
- a lifecycle-bearing business artifact;
- a source of downstream business effects;
- a node in a document flow graph;
- a possible derivation source for new business documents.

## Universal document CRUD

The verified source confirms several important design choices:

### Head + parts model

`DocumentService` comments explicitly describe:

- scalar head fields in a `doc_*` head table;
- tabular parts in `doc_*__*` tables;
- common registry + typed head table persistence model.

### Draft-first editing

The service supports create/update/delete of drafts before posting is involved.

### Metadata-driven validation

Field and part validation are driven by metadata and draft validators rather than controller-specific hardcoding.

## Document flow

The verified `GetRelationshipGraphAsync` implementation confirms that document flow is treated as a graph problem, not as a single foreign-key lookup.

Important confirmed behavior:

- backend loads relationship graph in a single API request;
- backend avoids N+1 head fetches by loading graph head rows across types in bulk;
- graph nodes are enriched with display, status, date, and optional amount.

That is a strong platform statement: document flow is a reusable explainability feature.

## Document effects

The verified `GetEffectsAsync` implementation confirms that effects are a cross-engine concept.

Effects surface includes:

- accounting entries;
- operational register movements;
- reference register writes;
- UI capability/effect state.

This is one of the most important platform UX contracts in NGB.

## Derive

The verified source confirms that derivation is a first-class runtime concept:

- list derivation actions for a source document;
- derive a new draft from a source document;
- optionally fall back to scaffold-like draft creation only when payload is supplied and explicit derivation config is absent.

Architecturally, derive is the right place for:

- based-on document chains;
- follow-up documents;
- business workflows that should preserve lineage.

## Recommended extension checklist

When adding a new document type, review all of these, not only CRUD:

- metadata and storage;
- draft validation;
- posting/effects;
- relationship graph behavior;
- derivation actions;
- UI effects availability;
- audit visibility.

## Related pages

- [Accounting and Posting Deep Dive](/platform/accounting-posting-deep-dive)
- [Closing Period Deep Dive](/platform/closing-period-deep-dive)
- [Audit Log Deep Dive](/platform/audit-log-deep-dive)
- [Runtime Source Map](/platform/runtime-source-map)
