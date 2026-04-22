---
title: Document Flow
description: How NGB exposes document relationships as a user-facing business chain instead of isolated records.
---

# Document Flow

Document Flow is the NGB surface that lets users and developers traverse the business chain around a document.

Typical relationships include:

- based on;
- derived from;
- reversed by;
- applied to;
- settled by;
- operational follow-up document.

## Why document flow matters

Without document flow, a business system becomes a collection of disconnected records.

With document flow, users can answer questions such as:

- where did this document come from;
- what did it generate;
- what reversed it;
- what settled it;
- what is the full business chain for this case.

That makes the platform easier to operate, support, and audit.

## Typical business chains

Examples of document flow in practice:

- Sales Order to Sales Invoice to Customer Payment;
- Lease to Rent Charge to Receivable Payment to Apply;
- Maintenance Request to Work Order to Completion;
- original invoice to correction or credit memo.

## Document flow is not just a foreign key

Production document flow should behave like a reusable relationship graph.

That means the platform can present:

- upstream context;
- downstream outcomes;
- cross-document lineage;
- consistent navigation between related records.

## Relationship quality rules

A strong document flow model should make relationship semantics explicit.

The relationship should tell the reader not just that two records are connected, but why they are connected.

## UI expectation

Users should be able to open a document and understand:

- its current status;
- its related business chain;
- whether later documents came from it;
- whether it was corrected, reversed, or settled.

That consistency is one of the platform’s main explainability wins.

## Related pages

- [Documents](/architecture/documents)
- [Derive](/architecture/derive)
- [Accounting Effects](/architecture/accounting-effects)
- [Documents, Flow, Effects, Derive](/platform/documents-flow-effects-deep-dive)
