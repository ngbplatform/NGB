---
title: Append-only and Storno
---

# Append-only and Storno

Append-only is one of the most important architectural principles in NGB.

The core idea is simple:

> business effects should be added, reversed, or superseded by new facts, not silently rewritten in place.

This principle influences:

- Accounting;
- Operational Registers;
- Reference Registers;
- Audit Log;
- document relationships and explainability.

## Why NGB prefers append-only

Append-only design gives the platform several important properties:

- better auditability;
- easier reasoning about historical truth;
- cleaner correction flows;
- safer concurrency behavior;
- better explainability for users and engineers.

When a user asks “what happened?”, the answer should be reconstructible from durable facts instead of inferred from overwritten rows.

## What storno means in practice

Storno is the reversal pattern used when prior business effects need to be compensated.

Instead of editing the original effect rows directly, the system appends compensating rows that reverse or neutralize the earlier impact.

In practice this may look like:

- reversing journal entries;
- compensating OR movements;
- superseding RR values with later effective facts;
- creating reversal or correction documents linked to the original document.

## Why this matters for business software

Business software often lives longer than the people who first wrote it.

When a future engineer or auditor reads the data, the system should make the timeline understandable:

1. the original event happened;
2. later it was corrected;
3. the correction was applied by a specific user or process;
4. the current net effect is the result of those explicit facts.

That timeline is much harder to trust if the platform mostly edits current-state rows in place.

## Append-only in Accounting

In Accounting, append-only means ledger effects are treated as durable facts.

A correction should normally be expressed by:

- reversal;
- storno;
- compensating repost;
- or a new correcting document.

It should not silently mutate the original financial history.

## Append-only in Operational Registers

In OR, append-only means movement history remains the source of truth.

Balances may be materialized or finalized for performance, but the business meaning is still defined by the movement stream.

## Append-only in Reference Registers

In RR, append-only means the old effective fact remains in history and the new fact becomes effective from a later point.

That is ideal for price history, policy history, and other date-effective reference facts.

## Append-only in Audit Log

Audit Log naturally fits append-only because it records actions and diffs as history rather than trying to preserve a single mutable “last known state.”

## What append-only does not mean

Append-only does **not** mean:

- nothing can ever be corrected;
- users can never reverse mistakes;
- the database can never maintain helper summaries;
- the UI must expose raw movement mechanics directly.

It means the durable source-of-truth history is additive and explicit.

## Practical benefits

### Explainability

Users can inspect document flow, accounting effects, and audit history and see the real chain of business facts.

### Safer operations

Crashes, retries, and concurrent activity are easier to reason about when history is additive instead of overwritten.

### Better debugging

Engineers can inspect what was appended and when, instead of guessing what a previous row looked like before mutation.

### Better reporting

Historical reporting works more reliably when the platform preserves the fact timeline instead of reusing the same mutable row for multiple states.

## Developer guidance

When implementing a new business flow, ask:

- if this must be corrected later, how will we reverse or supersede it?
- can a future engineer reconstruct the story from durable facts?
- are we preserving business meaning, or hiding it behind mutable state?

If the implementation only works while everyone remembers what the code was supposed to do, it is not good enough.

NGB’s append-only / storno philosophy exists to make business truth durable, explicit, and supportable over time.
