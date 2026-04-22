---
title: "Accounting and Posting Deep Dive"
description: "How NGB models posting, accounting effects, append-only corrections, and runtime orchestration."
---

# Accounting and Posting Deep Dive

> **Page intent**
> This page turns the accounting/posting topic into a chapter-level explanation. It combines verified source anchors with architecture-level synthesis and extension guidance.

## Trust level

- **Verified anchors:** `NGB.Runtime/Documents/DocumentService.cs`, `NGB.Runtime/NGB.Runtime.csproj`
- **Architecture synthesis:** yes
- **Template guidance:** yes

## Chapter navigation

- [Documents, Flow, Effects, Derive](/platform/documents-flow-effects-deep-dive)
- [Runtime Source Map](/platform/runtime-source-map)
- [Runtime Execution Map](/platform/runtime-execution-map)
- [Add Document with Accounting and Registers](/guides/add-document-with-accounting-and-registers)

## Why posting is a platform concern

In NGB, posting is not an afterthought attached to a form save. It is a platform-level transition that turns a business document from a draft business intention into durable business effects.

At runtime, that separation shows up clearly:

- `DocumentService` owns universal document CRUD and delegates posting/unposting/reposting/mark-for-deletion to the dedicated posting service.
- `NGB.Runtime` depends on `NGB.Accounting`, `NGB.OperationalRegisters`, and `NGB.ReferenceRegisters`, which shows that posting is expected to coordinate several effect engines rather than write a single ledger row.

## Verified source anchors

### 1. Universal document service delegates posting

Confirmed in:

- `NGB.Runtime/Documents/DocumentService.cs`

Key observations from the verified source:

- `PostAsync`, `UnpostAsync`, `RepostAsync`, `MarkForDeletionAsync`, and `UnmarkForDeletionAsync` are exposed through the universal document service.
- CRUD and posting are intentionally separated.
- `RepostAsync` resolves posting actions explicitly and treats repost as a workflow operation, not as a naive overwrite.
- `GetEffectsAsync` reads accounting entries, operational movements, reference writes, and UI capability state as one conceptual "effects" surface.

This is important architecturally: a document is the user-facing unit, but posting is the effect-producing unit.

### 2. Runtime is intentionally wired above accounting/register engines

Confirmed in:

- `NGB.Runtime/NGB.Runtime.csproj`

`NGB.Runtime` references:

- `NGB.Accounting`
- `NGB.OperationalRegisters`
- `NGB.ReferenceRegisters`
- `NGB.Definitions`
- `NGB.Metadata`
- `NGB.Persistence`

That dependency shape strongly supports the platform intent: runtime orchestrates, while the specialized engines implement domain mechanics.

## Recommended mental model

A correct mental model for NGB posting is:

1. user edits a **document draft**;
2. runtime validates draft invariants;
3. posting transition resolves one or more posting actions/handlers;
4. handlers produce **accounting entries**, **operational movements**, and/or **reference writes**;
5. effects become queryable through the document effects surface;
6. corrections happen through append-only reversal/reposting flows, not silent mutation.

## Posting lifecycle in practice

### Draft

A draft is editable and can still replace tabular parts with draft semantics.

### Posted

A posted document has effective business meaning. At this point the system must be able to explain:

- what accounting entries were produced;
- what operational balances changed;
- what reference state changed;
- why those changes belong to this document.

### Repost

Repost is not "save again".

It is a controlled workflow action used when the document is already posted and the platform needs to rebuild its effective effects according to current rules.

### Unpost / reverse / mark-for-deletion

These are correction-oriented operations. In an append-only philosophy, they should not erase the fact that the original posting happened.

## Append-only and storno implications

Although this page is about accounting/posting, it must be read together with the append-only philosophy used across accounting and registers.

The practical consequences are:

- do not design posting handlers around in-place mutation of prior business effects;
- prefer explicit reversal/storno semantics;
- keep correction history observable;
- keep query/read models responsible for computing effective state from durable history.

## What belongs in `NGB.Accounting`

At the platform level, `NGB.Accounting` should own accounting semantics, not document CRUD.

Typical responsibilities include:

- accounting entry model;
- posting invariants for balanced entries;
- validation rules specific to ledger semantics;
- append-only correction model;
- abstractions used by runtime posting orchestration.

What should **not** belong there:

- HTTP concerns;
- generic document CRUD;
- vertical-specific business rules;
- direct UI behavior.

## Extension pattern for a new posted document

When you add a new posted document in a vertical solution, the recommended sequence is:

1. define the document metadata and storage;
2. implement draft validation rules;
3. implement posting handler(s) that translate document intent into accounting/register effects;
4. register the handler in the runtime composition of the vertical;
5. expose document effects in a way that remains explainable in UI;
6. add integration tests for:
   - draft create/update;
   - post;
   - unpost/repost/reverse paths;
   - effects visibility.

## Verification checklist for accounting posting work

Use this checklist when reviewing a new posted document:

- Does draft save avoid producing irreversible business effects?
- Does post produce effects through explicit platform posting flow?
- Can repost be explained and tested separately from draft save?
- Are corrections append-only rather than in-place mutation?
- Can the UI retrieve accounting/register effects from the document effects surface?
- Is the implementation vertical-specific only where business rules require it?

## Related pages

- [Documents, Flow, Effects, Derive](/platform/documents-flow-effects-deep-dive)
- [Operational Registers Deep Dive](/platform/operational-registers-deep-dive)
- [Reference Registers Deep Dive](/platform/reference-registers-deep-dive)
- [Audit Log Deep Dive](/platform/audit-log-deep-dive)
- [Runtime Source Map](/platform/runtime-source-map)
