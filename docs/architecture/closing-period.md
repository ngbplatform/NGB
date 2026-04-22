---
title: Closing Period
description: How NGB treats period closing as a controlled business boundary rather than a loose reporting label.
---

# Closing Period

Closing period is the platform capability that turns a time boundary into an explicit business invariant.

Typical examples include:

- close month;
- close quarter;
- close fiscal year.

## Why closing period matters

Closing a period is not just a reporting flag.

It protects the platform from:

- late posting into finalized periods;
- accidental back-dating;
- inconsistent operational and accounting history;
- silent mutation after financial review.

In a production system, period boundaries have to be enforceable, auditable, and predictable.

## What closing period interacts with

A real closing model usually touches:

- document posting and reposting rules;
- accounting validation;
- operational and reference side effects when relevant;
- background verification or finalization jobs;
- audit history and support tooling.

That is why closing period belongs to platform architecture, not only to the UI.

## What a production-ready closing model should define

A strong closing model should make all of the following explicit:

- the close target, such as day, month, or fiscal year;
- the command, document, or action that establishes the boundary;
- which operations become blocked after close;
- which adjustment paths remain legal after close;
- how the system records who closed the period and when.

## Deterministic close targets

NGB benefits from deterministic close targets because the same logical period boundary can then be addressed consistently by runtime, persistence, and operational tooling.

That reduces ambiguity and improves idempotent close handling.

## Design checklist

When reviewing period-closing behavior, confirm:

- the boundary is modeled explicitly;
- the blocked operations are clear and testable;
- correction flows do not erase history;
- background processes respect the closed state;
- users can understand why an operation is rejected.

## Related pages

- [Accounting and Posting](/architecture/accounting-posting)
- [Operational Registers](/architecture/operational-registers)
- [Closing Period Deep Dive](/platform/closing-period-deep-dive)
- [Background Jobs Deep Dive](/platform/background-jobs-deep-dive)
