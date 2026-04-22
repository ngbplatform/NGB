# NGB Agency Billing

**NGB Agency Billing** is a demo industry solution built on top of **NGB Platform**. It demonstrates how the platform can be used to implement a production-minded agency-services billing application with client contracts, time capture, invoice preparation, receivables tracking, and accounting-aware controls.

This demo is intended to show how NGB can power business applications where service delivery, billable time, project-level commercial setup, invoicing, and collection workflows must remain coherent inside one metadata-driven platform.

## Why this demo exists

The Agency Billing demo exists to show that NGB can support another operationally distinct vertical without changing the platform foundation.

The goal is to showcase:

- reusable platform architecture;
- metadata-driven business definitions;
- document-centric operational workflows;
- accounting-aware agency billing design;
- unified auditability and explainability;
- vertical-specific UI and host composition built on shared infrastructure.

## What the demo covers

NGB Agency Billing includes examples of business flows such as:

- client and commercial master data management;
- team member and project setup;
- rate card and service item management;
- client contracts with line-level billing rules;
- timesheet capture with billable and cost context;
- sales invoices and customer payment application;
- accounting policy setup and control-account wiring.

## Architectural role inside the monorepo

This solution is a **vertical application** built on top of the shared NGB Platform modules.

It typically includes:

- vertical definitions;
- vertical runtime services;
- PostgreSQL integration for the vertical;
- API host composition;
- background job composition;
- migrator composition;
- watchdog composition;
- dedicated web application packages for the Agency Billing experience.

The Agency Billing demo is important because it shows that the platform can support service-delivery and billing-heavy domains without forking the core accounting, metadata, security, or audit foundations.

## Key capabilities demonstrated

### Reuse of the shared platform core
The same platform core that powers other NGB solutions is reused here to model a service-billing vertical with its own language, documents, and workflows.

### Business-document-oriented architecture
Agency billing workflows are represented as first-class business documents such as contracts, timesheets, invoices, and customer payments.

### Accounting integration from the start
Control accounts and operational registers are seeded as part of the setup workflow so the vertical is accounting-aware from day one.

### Metadata-driven extensibility
Catalogs, documents, forms, lists, and lookup behavior remain definition-driven and can evolve without abandoning the platform conventions.

### Operational clarity and auditability
The solution keeps setup, execution, and financial consequences inside one auditable experience, including document flow, effects, and audit-log navigation.

## Typical domain areas in the demo

The exact scope can evolve, but the Agency Billing demo is intended to cover areas such as:

- clients and payment terms;
- projects and project ownership;
- team members and rate cards;
- client contracts and service definitions;
- time capture and billable-hour valuation;
- invoice preparation and receivables collection;
- accounting policy and financial control setup.

## Local run experience

Agency Billing follows the same local infrastructure shape as the other NGB industry solutions.

Typical local assets include:

- `.env.ab`
- `docker-compose.ab.yml`
- the Agency Billing API, Background Jobs, Migrator, Watchdog, and Web hosts
- Keycloak, PostgreSQL, Seq, and pgAdmin composition

This keeps the vertical easy to run in isolation while still using the shared platform tooling.

## Demo seed workflow

The Agency Billing migrator now supports both baseline defaults and a realistic demo dataset workflow:

- `seed-defaults` ensures accounts, operational registers, accounting policy, and default payment terms.
- `seed-demo` seeds production-like master data plus posted client contracts, timesheets, sales invoices, and customer payments.

Typical direct CLI usage:

```bash
dotnet run --project NGB.AgencyBilling.Migrator -- \
  seed-demo \
  --connection "Host=localhost;Port=5434;Database=ngb_ab;Username=ngb_ab_app;Password=Password(55)60-stronG-ab" \
  --skip-if-activity-exists true
```

The local Docker bootstrap uses the same command automatically through `.env.ab` variables such as `AB_DEMO_SEED_ENABLED`, `AB_DEMO_SEED_FROM`, `AB_DEMO_TIMESHEETS`, `AB_DEMO_SALES_INVOICES`, and `AB_DEMO_CUSTOMER_PAYMENTS`.

## Repository context

This demo lives inside the NGB monorepo together with the shared platform core and other industry demos.

At a high level, the repository contains:

- **NGB Platform Core** — shared platform modules and infrastructure;
- **NGB Agency Billing** — this demo industry solution;
- other NGB industry demos;
- shared UI packages and supporting tooling.

## Who this demo is for

This demo is intended for:

- software architects evaluating whether NGB can support another service-oriented vertical;
- developers who want to study how a billing-heavy domain is modeled on the platform;
- teams exploring agency-services, consulting, or project-billing scenarios;
- prospective customers or partners who want to see reusable platform architecture across multiple industries.

## What this demo is not

This demo is not intended to be presented as a finished commercial product. It is a reference implementation and platform showcase for a production-minded vertical slice.

Its purpose is to demonstrate how NGB supports multi-vertical development with one shared architecture, not to claim complete end-state business coverage.

## Getting started

Refer to the root repository README for:

- prerequisites;
- local setup;
- repository structure;
- platform architecture;
- build and run instructions.

For local containerized startup of this vertical specifically, use the Agency Billing compose assets in the repository root.

## License

This demo is distributed as part of **NGB Platform** under the repository license.
