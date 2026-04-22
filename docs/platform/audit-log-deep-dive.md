---
title: "Audit Log Deep Dive"
description: "How auditability is integrated into universal document flows and platform explainability."
---

# Audit Log Deep Dive

> **Page intent**
> This page explains audit log as a platform capability visible through runtime behavior, even where the dedicated audit implementation files are not all source-anchored in this docs set yet.

## Trust level

- **Verified anchors:** `NGB.Runtime/Documents/DocumentService.cs`
- **Architecture synthesis:** yes
- **Template guidance:** yes

## Chapter navigation

- [Documents, Flow, Effects, Derive](/platform/documents-flow-effects-deep-dive)
- [Accounting and Posting Deep Dive](/platform/accounting-posting-deep-dive)
## Verified source anchors

Confirmed in:

- `NGB.Runtime/Documents/DocumentService.cs`

Verified behavior in the document runtime:

- document draft create/update paths accept optional audit service integration;
- create and update flows build audit changes and write audit entries when audit is present;
- audit metadata includes document type context;
- audit is treated as runtime-integrated infrastructure, not only as a UI layer concern.

## Architectural meaning

Audit log in NGB is not just a table of timestamps. It is part of explainable business behavior.

A good audit model must preserve:

- what changed;
- which entity kind changed;
- which action code was executed;
- enough metadata to reconstruct business context;
- append-only trustworthiness.

## Recommended audit stance

Audit belongs at the boundary where business operations become durable state changes.

That usually means:

- document create/update/posting transitions;
- workflow transitions;
- administrative destructive/corrective operations;
- platform-managed state transitions that must remain explainable later.

## Review checklist

- Is audit written near the durable change boundary?
- Are audit action codes stable and meaningful?
- Can users/operators explain what happened without reading raw SQL rows?
- Does audit preserve append-only trust?

## Related pages

- [Documents, Flow, Effects, Derive](/platform/documents-flow-effects-deep-dive)
- [Accounting and Posting Deep Dive](/platform/accounting-posting-deep-dive)
