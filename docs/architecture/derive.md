---
title: Derive
description: How NGB derives one business document from another while preserving workflow intent and lineage.
---

# Derive

Derive is the NGB platform concept for creating the next business document from an existing one without losing business lineage.

Typical examples include:

- Sales Order to Sales Invoice;
- Sales Invoice to Credit Memo;
- Maintenance Request to Work Order;
- lease charge to downstream settlement document.

## Why derive exists

Derive is not a copy-and-paste shortcut.

It exists so the platform can preserve:

- source document identity;
- workflow intent;
- allowed transition rules;
- prefilled business data;
- durable provenance for later explainability.

That distinction matters because a follow-up document should carry more meaning than “someone created a similar record by hand.”

## What a good derive flow should guarantee

A production-ready derive flow should define:

- which source document types are allowed;
- which target document types can be created;
- which source statuses permit the action;
- which fields and parts are prefilled;
- which business relationships are recorded.

If those rules are not explicit, derive becomes opaque and unreliable.

## Derive and workflow continuity

Derive belongs to the business workflow layer.

It should help a user move through a chain such as:

- order to invoice;
- invoice to correction;
- request to execution document;
- source commercial document to settlement document.

That is why derive should be visible in document actions and later visible in Document Flow.

## Derive and provenance

When one document is derived from another, the system should preserve:

- the source reference;
- the relationship type;
- the fact that the target is a follow-up artifact rather than an unrelated record.

Without that provenance, downstream audit and support work become much harder.

## Design checklist

When defining a derive flow, confirm all of the following:

- the source document has a stable business meaning;
- the target document is a real downstream artifact, not a convenience duplicate;
- status rules prevent invalid derivation;
- the prefill logic is deterministic and explainable;
- resulting document relationships remain visible to users.

## Related pages

- [Documents](/architecture/documents)
- [Document Flow](/architecture/document-flow)
- [Documents, Flow, Effects, Derive](/platform/documents-flow-effects-deep-dive)
- [Add a Document with Accounting and Registers](/guides/add-document-with-accounting-and-registers)
