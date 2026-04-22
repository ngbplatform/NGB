---
title: Audit Log
---

# Audit Log

Audit Log is the platform’s append-only record of business actions.

It exists so the system can explain:

- what changed;
- who changed it;
- when it changed;
- which business object changed;
- what the old and new values were where relevant.

## Why Audit Log is a platform concern

Auditability should not be reimplemented differently in every vertical.

In NGB, Audit Log is part of the platform because users expect the same level of explainability across:

- catalogs;
- documents;
- workflow actions;
- posting-related transitions;
- administrative changes.

## Main audit design ideas

The platform audit model is append-only and action-oriented.

That means it records business actions rather than only storing a vague “last modified at” field.

Useful audit data typically includes:

- entity type;
- entity id;
- action code;
- actor;
- timestamp;
- old values;
- new values;
- related context.

## Actor identity

The platform uses `platform_users` as the durable application-side projection of authenticated users.

That allows the system to record platform-aware actor identity without making the business storage layer depend directly on live Keycloak queries.

## Why old/new diffs matter

Old/new diffs make the audit log actually useful.

Without diffs, an operator can often see that something changed but still cannot answer what changed.

Diff-based audit history closes that gap and is especially valuable for:

- documents with business payloads;
- catalogs with sensitive operational meaning;
- policy or account setup changes;
- deletion / restoration flows.

## Example action codes

Representative action families include:

- document create / post / unpost / repost;
- catalog create / update / mark deleted;
- chart of accounts changes;
- period close actions.

These action codes are important because they make the audit log readable as business behavior, not just as generic storage churn.

## Audit Log and append-only philosophy

Audit Log fits naturally with the broader NGB philosophy:

- do not silently rewrite business history;
- prefer explicit actions and corrections;
- preserve the timeline of business intent and consequences.

## Audit Log and UI explainability

A good business UI should allow the user to open a business object and inspect its audit history directly.

That is what makes Audit Log useful outside the database.

## What should be audited

As a rule, audit actions should be recorded for changes that matter to the business or to supportability.

Good candidates include:

- lifecycle transitions;
- payload changes;
- deletions and restorations;
- policy or setup changes;
- administrative force operations.

## What audit should not become

Audit should not turn into noisy low-value storage of every incidental technical event.

The best audit log is:

- explicit;
- business-readable;
- complete for important actions;
- sparse enough to stay useful.

That balance is what makes the platform audit model practical in real systems.
