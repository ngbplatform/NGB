# NGB Trade

**NGB Trade** is a demo industry solution built on top of **NGB Platform**. It demonstrates how the platform can be used to implement a production-minded trade and distribution application with accounting integration, business documents, reporting, and auditability.

This demo is intended to show how NGB can power business applications where inventory-adjacent flows, sales and purchasing documents, operational visibility, and accounting effects must work together within a single platform.

## Why this demo exists

The Trade demo exists to show that NGB is not limited to one vertical. It demonstrates how the same platform core can be reused to build a second industry solution with different business semantics while keeping the same architectural approach.

The goal is to showcase:

- reusable platform architecture;
- metadata-driven business definitions;
- business documents and lifecycle actions;
- accounting-aware domain modeling;
- unified reporting and drilldown behavior;
- auditable platform behavior across different domains.

## What the demo covers

NGB Trade includes examples of business flows such as:

- master data management;
- customer- and supplier-related processes;
- sales documents;
- purchasing documents;
- pricing-related flows;
- accounting posting and reporting scenarios;
- operational business reporting;
- auditability and explainability.

## Architectural role inside the monorepo

This solution is a **vertical application** built on top of the shared NGB Platform modules.

It typically includes:

- vertical definitions;
- vertical runtime services;
- PostgreSQL integration for the vertical;
- API host composition;
- background job composition;
- migrator composition;
- web application packages for the Trade experience.

The Trade demo is important because it shows that the platform can support multiple verticals without duplicating the platform foundation.

## Key capabilities demonstrated

### Reuse of the shared platform core
The same platform foundation can be used to build a different business application with its own definitions, documents, reports, and workflows.

### Business-document-oriented architecture
Trade workflows are modeled through first-class business documents rather than isolated endpoint logic.

### Accounting integration from the start
The demo highlights NGB’s accounting-centric approach by showing how operational transactions and financial consequences are designed together.

### Metadata-driven extensibility
Definitions, metadata, and runtime orchestration make it possible to extend the solution in a structured and reusable way.

### Reporting and explainability
The solution demonstrates how business reporting, accounting views, and drilldowns can remain coherent across a vertical application.

## Typical domain areas in the demo

The exact scope can evolve, but the Trade demo is intended to cover areas such as:

- products and product-related master data;
- customers and suppliers;
- sales flows;
- purchasing flows;
- pricing scenarios;
- accounting and reporting;
- audit and operational visibility.

## Live demo

- **Live Demo:** https://trade-demo.ngbplatform.com
- **Platform Website:** https://ngbplatform.com

## Repository context

This demo lives inside the NGB monorepo together with the shared platform core and other industry demos.

At a high level, the repository contains:

- **NGB Platform Core** — shared platform modules and infrastructure;
- **NGB Trade** — this demo industry solution;
- shared UI packages and supporting tooling.

## Who this demo is for

This demo is intended for:

- software architects evaluating the breadth of the NGB platform;
- developers who want to understand how a second vertical can be built on the same platform;
- teams considering NGB for business systems in trade, distribution, or adjacent domains;
- prospective customers or partners who want to see platform reuse across multiple industry solutions.

## What this demo is not

This demo is not intended to be presented as a finished commercial product. It is a reference implementation and product showcase for the platform.

Its purpose is to demonstrate how NGB supports multi-vertical development through one shared architectural foundation.

## Getting started

Refer to the root repository README for:

- prerequisites;
- local setup;
- repository structure;
- platform architecture;
- build and run instructions.

## License

This demo is distributed as part of **NGB Platform** under the repository license.
