---
title: NGB Platform Architecture Brief
description: A concise architecture overview of NGB Platform as an accounting-first foundation for vertical business applications.
---

<script setup>
const architectureBriefChart1 = String.raw`flowchart LR
    A[User Form] --> B[API Endpoint]
    B --> C[(Table Row)]
    C --> D[Report Query]`

const architectureBriefChart2 = String.raw`flowchart LR
    CRUD[Generic CRUD Frameworks] --> GAP[Architecture Gap]
    ERP[Large ERP Suites] --> GAP
    GAP --> NGB[NGB Platform]
    NGB --> CORE[Reusable Platform Core]
    NGB --> VERTICALS[Vertical Business Applications]
    NGB --> REPORTS[Durable Reporting]
    NGB --> AUDIT[Traceable Business Truth]`

const architectureBriefChart3 = String.raw`flowchart TD
    A[Business Intent] --> B[Document Draft]
    B --> C[Validation]
    C --> D[Posted Document]
    D --> E[Accounting Entries]
    D --> F[Operational Register Movements]
    D --> G[Reference Register Updates]
    D --> H[Audit History]
    E --> I[Canonical Reports]
    F --> I
    G --> I
    H --> I
    I --> J[User Decisions]`

const architectureBriefChart4 = String.raw`flowchart TD
    subgraph Verticals
        PM[Property Management]
        TR[Trade]
        AB[Agency Billing]
    end

    subgraph PlatformCore[NGB Platform Core]
        META[Definitions and Metadata]
        RT[Runtime]
        DOC[Documents]
        CAT[Catalogs]
        POST[Posting]
        REP[Reporting]
        AUDIT[Audit Log]
    end

    subgraph BusinessEffects[Durable Effects]
        ACC[Accounting Entries]
        OR[Operational Registers]
        RR[Reference Registers]
    end

    subgraph Infra[Infrastructure]
        API[API Hosts]
        PG[(PostgreSQL)]
        BG[Background Jobs]
        MIG[Migrator]
        WATCH[Watchdog]
    end

    PM --> PlatformCore
    TR --> PlatformCore
    AB --> PlatformCore

    API --> RT
    RT --> META
    RT --> DOC
    RT --> CAT
    DOC --> POST
    POST --> ACC
    POST --> OR
    POST --> RR
    RT --> REP
    RT --> AUDIT

    DOC --> PG
    CAT --> PG
    ACC --> PG
    OR --> PG
    RR --> PG
    REP --> PG
    AUDIT --> PG

    BG --> PG
    MIG --> PG
    WATCH --> API`

const architectureBriefChart5 = String.raw`sequenceDiagram
    participant User as User / Web Client
    participant API as API Host
    participant Runtime as Runtime Services
    participant Definitions as Definitions / Metadata
    participant Posting as Posting Pipeline
    participant DB as PostgreSQL
    participant Reports as Reporting

    User->>API: Submit command or query
    API->>Runtime: Execute use case
    Runtime->>Definitions: Resolve document/catalog/report metadata
    Runtime->>Runtime: Validate payload and operation
    alt Posting command
        Runtime->>Posting: Build durable effects
        Posting->>DB: Persist document + effects atomically
    else Query/report request
        Runtime->>Reports: Execute canonical/composable report
        Reports->>DB: Read durable state
    end
    DB-->>Runtime: Result
    Runtime-->>API: DTO / response
    API-->>User: UI-ready result`

const architectureBriefChart6 = String.raw`flowchart TD
    A[Metadata Definition] --> B[Generated UI / API Contract]
    B --> C[User Creates or Edits Document]
    C --> D[Runtime Validation]
    D --> E[Business Rule Validation]
    E --> F[Post Command]
    F --> G[Atomic Persistence]
    G --> H[Document State]
    G --> I[Accounting Effects]
    G --> J[Operational Effects]
    G --> K[Reference Effects]
    G --> L[Audit Trail]
    H --> M[Document Flow]
    I --> N[Financial Reports]
    J --> O[Operational Reports]
    K --> P[Effective Reference Views]
    L --> Q[Traceability]`
</script>


# NGB Platform Architecture Brief

NGB Platform is an open-source, accounting-first foundation for building vertical business applications with durable business documents, catalogs, append-only effects, operational registers, reference registers, audit history, and reporting.

It is designed for systems where business data cannot be treated as simple CRUD rows. In accounting-centric domains, a user action often has long-lived consequences: documents are posted, effects become durable, reports must remain explainable, and later corrections should be modeled as explicit reversals or new business events rather than silent data mutation.

NGB is built around that premise.

It uses .NET and PostgreSQL as the backend foundation, Vue for the web client, and deployment patterns that are intended to fit modern containerized and Kubernetes-oriented environments.

::: tip How to read this page
This page is the short architecture entry point. It is intentionally higher-level than the deep source maps and subsystem chapters. For implementation details, follow the links in [What to read next](#what-to-read-next).
:::

## What NGB is

NGB is a reusable platform core for building accounting-aware vertical business systems.

The platform provides common primitives that many business applications need:

- **Catalogs** for stable master data.
- **Documents** for business intent and workflow.
- **Posting** for durable business effects.
- **Accounting entries** for double-entry financial impact.
- **Operational registers** for non-GL business state.
- **Reference registers** for effective-dated reference state.
- **Audit history** for traceability.
- **Reporting** for canonical and composable analysis.
- **Metadata-driven UI** for dynamic forms, grids, and report surfaces.
- **Vertical modules** for industry-specific behavior.

The goal is not to hide business complexity behind a generic CRUD abstraction. The goal is to provide a reusable foundation where that complexity can be modeled explicitly and consistently.

## Why NGB exists

Many business applications start as CRUD systems:

<MermaidDiagram :chart="architectureBriefChart1" />

That model works for simple administrative data, but it starts to break down when the system needs:

- posted documents
- reversals
- accounting entries
- audit history
- month close rules
- derived operational state
- reporting with traceable source documents
- vertical-specific workflows
- repeatable business invariants

At the other end of the spectrum, established ERP suites provide deep functionality but can be difficult to extend, reason about, or reuse as a clean architecture foundation.

NGB explores the middle path:

<MermaidDiagram :chart="architectureBriefChart2" />

NGB is intended to provide a serious platform architecture for systems that need more than CRUD, without forcing every vertical application to reinvent documents, posting, registers, auditability, and reporting from scratch.

## Core architectural idea

The central idea is simple:

> **Documents represent business intent. Posting turns that intent into durable business effects. Reports read durable truth.**

<MermaidDiagram :chart="architectureBriefChart3" />

This separation is important:

- A draft document can be edited because it still represents intent.
- A posted document creates durable effects.
- A correction should be explicit, traceable, and auditable.
- Reports should explain what happened, not reconstruct meaning from arbitrary table mutations.

## Platform shape at a glance

NGB is organized around a shared platform core and vertical-specific modules.

<MermaidDiagram :chart="architectureBriefChart4" />

The verticals own their business semantics. The platform owns reusable mechanisms.

## Runtime request flow

At runtime, requests usually move through a layered path:

<MermaidDiagram :chart="architectureBriefChart5" />

For deeper implementation details, see:

- [Runtime Request Flow](/architecture/runtime-request-flow)
- [HTTP to Runtime to PostgreSQL Execution Map](/architecture/http-runtime-postgres-execution-map)
- [Runtime Execution Core Dense Source Map](/platform/runtime-execution-core-dense-source-map)

## Core business concepts

### Catalogs

Catalogs model relatively stable master data: accounts, parties, properties, units, items, services, or other vertical-specific entities.

They are not just tables. In NGB, catalogs are metadata-defined business objects with consistent runtime handling, validation, persistence, and UI generation.

Read more: [Catalogs](/architecture/catalogs)

### Documents

Documents model business intent: a lease, invoice, payment, charge, work order, contract, timesheet, or other action that can move through a lifecycle.

Documents are central because they provide the business explanation for later effects.

Read more: [Documents](/architecture/documents)

### Document flow

Business systems rarely consist of isolated documents. A charge may lead to a payment, a payment may be applied to an open item, and a work order may be linked to a request and completion.

Document flow makes those relationships visible and explainable.

Read more: [Document Flow](/architecture/document-flow)

### Posting

Posting is the transition from intent to durable effect.

A posted document may create:

- accounting entries
- operational register movements
- reference register updates
- audit events
- document relationships
- reporting-visible state

Read more: [Accounting and Posting](/architecture/accounting-posting)

### Append-only and storno

NGB favors explicit durable history over silent mutation. Posted effects should be preserved; corrections should be modeled through reversal or new effects where appropriate.

This makes history more explainable and helps reporting remain auditable.

Read more: [Append-only and Storno](/architecture/append-only-and-storno)

### Operational registers

Operational registers model business state that is not necessarily double-entry accounting: open receivables, inventory quantities, document relationships, occupancy facts, maintenance state, or other vertical-specific operational truth.

Read more: [Operational Registers](/architecture/operational-registers)

### Reference registers

Reference registers model effective reference state, such as prices, policies, rates, and settings that change over time and need historically correct lookup semantics.

Read more: [Reference Registers](/architecture/reference-registers)

### Reporting

Reporting is a first-class platform subsystem rather than an afterthought. NGB supports canonical reports for known business views and composable reports for metadata-driven exploration.

Read more:

- [Reporting: Canonical and Composable](/architecture/reporting)
- [Reporting Subsystem Dense Source Map](/platform/reporting-subsystem-dense-source-map)

## From request to durable business truth

The following diagram summarizes the platform’s main business-truth pipeline:

<MermaidDiagram :chart="architectureBriefChart6" />

This model is designed to answer questions that matter in real business systems:

- What was the original business intent?
- Who changed it?
- Was it posted?
- What durable effects did it create?
- What report rows came from it?
- How was it corrected?
- Can the system explain the result later?

## What this architecture optimizes for

NGB optimizes for:

| Goal | How NGB addresses it |
| --- | --- |
| Auditability | Documents, audit history, append-only effects, explicit lifecycle actions |
| Traceability | Document flow, source-linked posting effects, report drilldowns |
| Extensibility | Metadata-defined catalogs/documents/reports and vertical modules |
| Consistency | Shared runtime services and platform-level validation patterns |
| Reporting durability | Canonical and composable reports over durable platform state |
| Vertical reuse | Shared core with PM, Trade, and Agency Billing style verticals |
| Operational correctness | Registers, month/period concepts, idempotency, and concurrency controls |

## What NGB is not

NGB is not a generic CRUD admin generator.

NGB is not positioned as a finished one-size-fits-all replacement for every established ERP suite.

NGB is not only a demo application. The demo verticals exist to prove that the platform core can support different business domains using the same architectural primitives.

## What to read next

For the full architecture path:

1. [Architecture Overview](/architecture/overview)
2. [Layering and Dependencies](/architecture/layering-and-dependencies)
3. [Definitions and Metadata](/architecture/definitions-and-metadata)
4. [Runtime Request Flow](/architecture/runtime-request-flow)
5. [Accounting and Posting](/architecture/accounting-posting)
6. [Operational Registers](/architecture/operational-registers)
7. [Reference Registers](/architecture/reference-registers)
8. [Append-only and Storno](/architecture/append-only-and-storno)
9. [Idempotency and Concurrency](/architecture/idempotency-and-concurrency)
10. [Reporting: Canonical and Composable](/architecture/reporting)

For implementation-oriented readers:

- [Document Subsystem Dense Source Map](/platform/document-subsystem-dense-source-map)
- [Reporting Subsystem Dense Source Map](/platform/reporting-subsystem-dense-source-map)
- [Runtime Execution Core Dense Source Map](/platform/runtime-execution-core-dense-source-map)
- [PostgreSQL Source Map](/platform/postgresql-source-map)

For ecosystem-oriented readers:

- [NGB for ERP and Accounting Software Ecosystem Teams](/ecosystem/erp-accounting-software-teams)
- [Integration and Extension Opportunities](/ecosystem/integration-and-extension-opportunities)
- [Evaluation Guide](/ecosystem/evaluation-guide)
