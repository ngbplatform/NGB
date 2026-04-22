---
title: Accounting Effects
description: How NGB exposes the business and accounting consequences produced by posted documents.
---

# Accounting Effects

Accounting Effects is the surface that answers the question, “What did this document do?”

For a posted document, the platform should be able to expose:

- ledger entries;
- debit and credit impact;
- operational or reference side effects when relevant;
- related statuses and explainability context.

## Why accounting effects deserve a separate page

Document Flow and Accounting Effects are related, but they answer different questions.

- Document Flow explains how documents relate to each other.
- Accounting Effects explains what a document produced in accounting or registers.

That distinction is important both for users and for system design.

## Why effects visibility matters

A production system should not force users to infer posting results from database tables or hidden implementation details.

Effects should be inspectable so that finance, support, and engineering teams can confirm:

- which entries were created;
- why balances changed;
- which document caused the change;
- whether related operational or reference movements also happened.

## Effects and explainability

Accounting Effects is one of the core explainability surfaces in NGB.

Together with status, audit history, and document flow, it helps users trust the platform’s business behavior.

## Typical design rules

When exposing accounting effects, the system should preserve:

- clear linkage back to the source document;
- durable historical view rather than mutable side notes;
- readable debit and credit presentation;
- consistent naming and ordering across document types.

## When to use this surface

Users reach for accounting effects when they need to understand consequences, not lineage.

That makes it especially important for:

- posted commercial documents;
- corrections and reversals;
- period-close-related documents;
- support and reconciliation work.

## Related pages

- [Accounting and Posting](/architecture/accounting-posting)
- [Document Flow](/architecture/document-flow)
- [Accounting and Posting Deep Dive](/platform/accounting-posting-deep-dive)
- [Documents, Flow, Effects, Derive](/platform/documents-flow-effects-deep-dive)
