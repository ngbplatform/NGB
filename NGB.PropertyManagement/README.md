# NGB Property Management

**NGB Property Management** is a demo industry solution built on top of **NGB Platform**. It demonstrates how the platform can be used to implement a production-minded property management application with accounting, operational workflows, auditability, reporting, and metadata-driven business documents.

This solution is designed to showcase how NGB supports real-world business domains where documents, lifecycle actions, accounting effects, reporting, and audit trails must work together as one coherent system.

## Why this demo exists

The goal of this demo is not to simulate a toy CRUD application. It exists to show how NGB can model a realistic property management domain with:

- metadata-driven catalogs and documents;
- document lifecycle and posting semantics;
- accounting integration;
- operational and reference registers;
- append-only audit logging;
- report definitions, execution, and drilldowns;
- a web client generated from shared platform conventions.

## What the demo covers

NGB Property Management includes examples of:

- portfolio and master data management;
- leases as business source-of-truth documents;
- receivables workflows such as charges, payments, returned payments, applies, and credit memos;
- payables workflows;
- accounting posting and reporting scenarios;
- operational visibility through reports and document flow;
- explainability through audit trails and accounting effects.

## Architectural role inside the monorepo

This solution is a **vertical application** built on top of the shared NGB Platform modules.

It typically includes:

- vertical definitions;
- vertical runtime services;
- PostgreSQL integration for the vertical;
- API host composition;
- background job composition;
- migrator composition;
- web application packages for the Property Management experience.

The Property Management demo shows how a business-specific solution can extend the platform without changing the platform’s core architectural principles.

## Key capabilities demonstrated

### Metadata-driven business application design
The demo uses platform metadata and definitions to describe catalogs, documents, reports, and behavior in a consistent way.

### Document-centered workflows
The solution models business processes through documents instead of ad hoc tables and services. This makes lifecycle, posting, audit, and user-facing explainability much more coherent.

### Accounting-centric architecture
The demo highlights one of the core strengths of NGB: building operational applications that are tightly integrated with accounting rather than bolting accounting on later.

### Reporting and drilldowns
Reports are treated as first-class platform capabilities. The demo shows how users can move from report output into business documents and accounting views.

### Append-only and auditable behavior
Business actions are designed to be explainable and traceable through an append-only audit model.

## Typical domain areas in the demo

The exact scope can evolve, but the Property Management demo is intended to cover areas such as:

- properties and units;
- parties and contacts;
- leases and occupancy-related state;
- receivables;
- payables;
- accounting policy and accounting effects;
- operational visibility and reporting.

## Live demo

- **Live Demo:** https://pm-demo.ngbplatform.com
- **Platform Website:** https://ngbplatform.com

## Repository context

This demo lives inside the NGB monorepo together with the shared platform core and other industry demos.

At a high level, the repository contains:

- **NGB Platform Core** — shared platform modules and infrastructure;
- **NGB Property Management** — this demo industry solution;
- shared UI packages and supporting tooling.

## Who this demo is for

This demo is intended for:

- software architects evaluating NGB as a platform;
- developers who want to understand how to build vertical solutions on top of NGB;
- product teams exploring accounting-centric business software architecture;
- prospective customers or partners who want to see a realistic example of the platform in action.

## What this demo is not

This demo is not intended to be presented as a finished commercial product. It is a reference implementation and product showcase for the platform itself.

Its purpose is to demonstrate architectural patterns, platform capabilities, and domain implementation style.

## Getting started

Refer to the root repository README for:

- prerequisites;
- local setup;
- repository structure;
- platform architecture;
- build and run instructions.

## License

This demo is distributed as part of **NGB Platform** under the repository license.
