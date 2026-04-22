---
title: Documents
---

# Documents

Documents are the center of business behavior in NGB.

A document is not merely a record with a status column. It is the unit through which the platform expresses business intent, lifecycle actions, posting, derive flows, relationships, explainability, and user-facing workflow.

## Why documents are central

Most serious business actions are better modeled as documents than as ad hoc endpoint logic.

Examples:

- Sales Invoice
- Purchase Receipt
- Lease
- Maintenance Request
- Work Order
- Item Price Update
- Customer Payment
- Period Close

Documents give the platform a durable, auditable, explainable unit of business behavior.

## What a document usually contains

A typical NGB document includes:

- a document type code;
- head data such as number, date, status, business references, and totals;
- payload fields;
- line items or parts where relevant;
- allowed actions;
- workflow or status transition rules;
- optional posting behavior;
- optional derive behavior;
- relationships to upstream or downstream documents.

## Document lifecycle

The exact lifecycle depends on the document type, but common ideas include:

- Draft;
- Posted;
- Marked for deletion / Deleted in business UI terms;
- reversal or correction flows where allowed.

What matters is that lifecycle transitions are explicit and validated.

## Why documents are better than generic row mutation

A document-centered design makes it possible to answer:

- who created the business event;
- what the business meaning was;
- which actions were performed;
- what effects were produced;
- what later documents reversed or followed from it.

That level of explainability is difficult to achieve when behavior is scattered across generic endpoints.

## Posting and derive as document capabilities

Two important capabilities often attach to documents:

### Posting

The document produces accounting or register effects.

### Derive

The document becomes the source for another document, with durable provenance tracked in document relationships.

Those capabilities are why documents are much more powerful than CRUD objects.

## Document relationships

Documents are linked to each other so users and developers can inspect business chains such as:

- based on;
- derived from;
- reversed by;
- applies to;
- settled by.

This is the basis for the Document Flow surface.

## UI implications

The platform wants document UX to be coherent across verticals.

That means the UI can rely on stable concepts such as:

- display text;
- status;
- actions;
- audit view;
- accounting effects view;
- document flow view.

The user should not need a different mental model for every document type.

## Good document boundaries

A good document boundary answers:

- what business event does this document represent?
- is it operational, accounting-producing, or both?
- what is the source-of-truth intent?
- can corrections be expressed cleanly?
- will users understand the document in a workflow?

Examples of good document boundaries:

- Sales Invoice
- Customer Payment
- Item Price Update
- Inventory Transfer
- Close Month

Examples of weak boundaries:

- “Update Everything”
- “Save Current Totals”
- “Quick Adjustment” with no durable business meaning

## Developer rule of thumb

If the thing you are modeling has:

- user intent,
- lifecycle,
- explainability needs,
- accounting or register consequences,
- or downstream workflow,

it should usually be a document, not a random mutation endpoint.
