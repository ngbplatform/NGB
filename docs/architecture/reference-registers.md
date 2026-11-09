---
title: Reference Registers
---

# Reference Registers

Reference Registers store append-only reference facts that define effective business state over time.

They are useful when the system needs durable, time-aware reference values such as:

- price lists;
- item cost policies;
- default tax behavior;
- exchange or factor tables;
- contract terms that are read as effective state rather than as operational balances.

## Why Reference Registers exist

Not all business facts are transactional quantities or ledger entries.

Some facts behave more like “effective configuration with history”:

- the price of an item for a specific price type;
- the default service rate for a project role;
- a commission percentage effective from a given date.

Those facts matter operationally and analytically, but they are not a good fit for current-state overwrite tables when auditability and historical explainability matter.

Reference Registers solve that problem by storing append-only movements and resolving effective state for reads.

## Core idea

A Reference Register movement states that a reference value becomes effective for a certain slice of business meaning.

Example:

- Item A
- Retail price
- USD
- effective from 2026-04-01
- price = 39.99

A later movement can change that price starting on a later date. The older fact remains in history.

## How RR differs from OR

Operational Registers are about **balances and turnovers**.

Reference Registers are about **effective reference state**.

That means OR asks questions like:

- how much?
- what balance?
- what turnover?

RR asks questions like:

- what value is currently effective?
- what value was effective on a specific date?
- how did the reference value change over time?

## Why RR differs from plain master data

A catalog row usually holds the identity of a business entity.

A Reference Register holds effective facts about that entity over time.

For example:

- `Item` is a catalog entity.
- `Item Retail Price` over time is a reference-register concern.

This separation keeps the entity stable while allowing auditable time-based reference change.

## Typical RR dimensions

A Reference Register usually keys its movements by dimensions such as:

- entity id;
- price type;
- currency;
- business unit;
- validity period start.

The exact dimensions depend on the business question.

## Example: item pricing

A production-minded pricing RR may use these dimensions:

- item;
- price type;
- currency;
- optional location or channel.

Its resource may be:

- price amount.

A later Item Price Update document appends a new movement rather than editing a current-price row in place.

## Append-only and effective reads

RR movements are append-only. Reads then resolve the best applicable fact for a given context.

For example:

- latest by effective date up to an “as of” date;
- latest within a dimension slice;
- full change history for audit or troubleshooting.

This is why RR fits well with pricing, policy history, and other date-effective reference semantics.

## Recorder cancellation in 3.4.0

For periodic registers in `SubordinateToRecorder` mode, the PostgreSQL provider
marks automatic recorder cancellations with internal column
`__is_recorder_cancellation`. They remain append-only rows with `is_deleted=true`.
An intentional deletion emitted by a handler retains the ordinary deletion
semantics and has the internal flag set to `false`.

Readers first select the latest recorded version of each exact effective point,
within the requested recorded-time boundary. Cancelled points do not hide another
active point or reveal their own superseded live version. If no active point
remains, the current value is absent; `includeDeleted` can still expose a tombstone.
Raw history retains all previous rows and values.

For example, replacing a value of 100 effective December 20 with 130 effective
December 10 must return 130 on December 25. Cancelling the obsolete December 20
point must not erase that replacement. An explicitly scheduled deletion on
December 20 still ends the preceding value. Repeated Repost keeps its existing
idempotent behavior; this correction does not turn it into an unlimited new action.

Post supersedes prior recorder points, including unmarked deletions left by an
older platform when a Draft was unposted. Unpost cancels all effective points,
including future ones. The generic Unpost fallback traverses the recorder's full
paged history when the optimized writer is unavailable. Custom persistence
providers remain responsible for their own cancellation storage and read semantics.
Independent and nonperiodic register semantics are unchanged.

The internal flag is not a business field, a handler argument or a new public DTO
member. Use the standard readers for effective state rather than reconstructing it
from raw history. Existing data and deployment requirements are described in the
[3.4.0 storage upgrade procedure](external-app-upgrades.md#reference-register-storage-update).

## Developer design checklist

When creating a new RR, decide:

1. what business value the register represents;
2. what dimensions identify one reference slice;
3. what resource values are stored;
4. what “effective from” semantics apply;
5. whether future-dated values are allowed;
6. how a reversal or supersession is represented.

## When RR is the right choice

Use a Reference Register when the business fact is:

- reference-like rather than transactional;
- time-effective;
- auditable;
- read frequently as “current as of date X”;
- likely to change over time without losing history.

That is why pricing is one of the best example use cases for RR in NGB.
