---
title: Platform Overview
---

# Platform Overview

NGB Platform is a reusable foundation for building accounting-centric business applications on .NET and PostgreSQL.

It is not a generic CRUD scaffold and not a boxed ERP product. The platform is designed for applications where the difficult parts are first-class concerns:

- business documents and lifecycle actions;
- accounting posting;
- operational and reference state projection through registers;
- auditability and explainability;
- metadata-driven UI and APIs;
- report execution, drilldowns, and exports;
- long-lived vertical solutions that share one platform core.

## Why the platform exists

Most business systems fall into one of two traps:

1. they start from a generic web framework and rebuild the same business architecture again and again;
2. they adopt a large ERP product and inherit a heavy customization model that is difficult to keep clean.

NGB exists to give teams a third option: a modular platform with strong business conventions, extensibility points, and an architecture that treats accounting, documents, registers, reporting, and auditability as platform concerns rather than late add-ons.

## What is already present in the platform

At the time of writing, the platform already provides a mature foundation for:

- shared contracts, metadata, and definitions;
- universal catalog and document patterns;
- runtime orchestration for CRUD, lifecycle, posting, reporting, and drilldowns;
- append-only accounting, operational registers, reference registers, and audit history;
- generic API hosts;
- PostgreSQL persistence and migrations;
- Hangfire-based background jobs;
- Watchdog / health aggregation;
- Keycloak-based authentication and SSO;
- a shared Vue-based UI framework;
- multiple vertical demos proving reuse across domains.

## Architectural north star

The platform architecture is opinionated in a way that helps large systems stay coherent:

- shared cross-cutting capabilities belong in the platform;
- vertical business semantics belong in vertical modules;
- Runtime coordinates behavior, but platform code avoids vertical leakage;
- PostgreSQL is the system of record;
- business effects should be explainable after the fact;
- append-only data flow is preferred over hidden mutation;
- corrections should be explicit;
- performance-sensitive reads must be shaped in backend services, not left to UI-side fanout.

## Platform outcomes

A team using NGB should be able to add a new industry solution or a new vertical feature by following recognizable patterns instead of inventing a new architecture every time.

That is the reason the platform is organized around reusable building blocks such as:

- Definitions;
- Metadata;
- Runtime;
- Accounting;
- Operational Registers;
- Reference Registers;
- PostgreSQL infrastructure;
- Migrator;
- Background Jobs;
- Watchdog;
- UI framework.

Those blocks are described in the architecture pages and then made concrete in the implementation guides.

## What this documentation is trying to achieve

The goal of this documentation is not only to describe what projects exist. It is to make NGB understandable enough that an engineer can safely extend it without accidentally breaking its architecture.

That means every important topic is covered from three angles:

1. **what the platform does;**
2. **why it is designed that way;**
3. **how to add or change functionality without violating the architecture.**

The rest of this site follows that structure.
