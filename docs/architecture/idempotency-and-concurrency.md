---
title: Idempotency and Concurrency
---

# Idempotency and Concurrency

Idempotency and concurrency are first-order architectural concerns in NGB because business systems do not run in perfect single-user, single-click conditions.

A platform must assume:

- retries happen;
- users double-click;
- background jobs overlap;
- two operators touch related records at the same time;
- network failures occur after partial progress becomes visible to a caller.

NGB is designed so those situations are handled through explicit guards instead of wishful thinking.

## Idempotency in practical terms

In NGB, idempotency means that re-running the same business action should not create duplicate business effects.

Examples:

- posting the same document twice should not append the same ledger entries twice;
- a scheduled finalization job should be safe to rerun;
- a derive action should not create two identical derived documents because a request was retried.

## Concurrency in practical terms

Concurrency means that overlapping operations should either:

- serialize correctly;
- fail clearly;
- or become safe no-ops.

It should never be possible for hidden races to create silent business corruption.

## Main mechanisms used in the platform

### 1. Transaction boundaries

Critical business actions run inside explicit transactional boundaries so the durable result is either committed coherently or not committed at all.

### 2. Posting log / replay guard

Posting-related flows use durable markers so the platform can tell whether the intended posting already happened.

This is the heart of idempotent posting.

### 3. Status and lifecycle checks

A document lifecycle transition is validated against the current durable state. Posting a document that is already posted should not silently create another set of effects.

### 4. Advisory locks for shared resources

Where the architecture requires cross-request coordination, NGB uses lock strategies that fit the domain.

Examples discussed across the platform include locks by:

- document;
- register and month;
- close period entity;
- other deterministic business keys.

This is especially important for background jobs and period-sensitive work.

### 5. Append-only effect storage

Append-only design helps concurrency because prior facts are not updated in place as part of normal business correction flows.

The system mostly adds new facts or reversal facts instead of rewriting history under contention.

### 6. No-op semantics

A healthy production system needs safe no-op behavior.

For example:

- if another worker already finalized a dirty month, a second worker should usually detect that and do nothing;
- if a posting retry arrives after success, the platform should recognize the prior success instead of duplicating effects.

## Why idempotency and append-only fit together

These two ideas reinforce each other.

Append-only storage gives the platform a durable trail of what already happened.

Idempotency then becomes easier to reason about because the platform can verify prior effect creation instead of reconstructing history from overwritten rows.

## Concurrency patterns by subsystem

### Document posting

Typical protection includes:

- lifecycle validation;
- posting log checks;
- transactional append of effects;
- document-scoped locking or status transition guards.

### Operational register finalization

This area is especially sensitive because balances may be derived from movement streams across periods.

Safe processing usually relies on:

- deterministic keys such as register + month;
- advisory locks;
- rerunnable jobs;
- no-op semantics if work is already done.

### Closing period

Closing a month or fiscal year must be serialized by business key. Two users closing the same period should not both succeed independently.

### Background jobs

Recurring jobs must assume overlap can happen. The job design should therefore be bounded, idempotent, and restart-safe.

## Rules for new platform or vertical code

When adding a new action, ask these questions before writing code:

1. what happens if the request is retried?
2. what happens if the action is triggered twice?
3. what happens if two requests hit the same business object at once?
4. what happens if a crash occurs after some effects were produced but before the caller saw success?
5. can the system explain the result after the fact?

If you do not have a clear answer to those questions, the implementation is not production-ready yet.

## Good implementation habits

- use durable business keys instead of UI assumptions;
- keep lock scope narrow but meaningful;
- make duplicate execution detection explicit;
- prefer bounded work per job run;
- emit logs and counters that let operators see when contention or replays happen;
- design retry-safe behavior first, not as an afterthought.

## Anti-patterns to avoid

- relying on the browser not to resubmit;
- using in-memory flags as the main protection;
- assuming only one background worker exists;
- performing critical state transitions outside transactions;
- hiding duplicate execution by mutating the same row without auditability.

Idempotency and concurrency are part of business correctness. In NGB they are treated that way.
