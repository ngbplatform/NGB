---
title: NGB for ERP and Accounting Software Ecosystem Teams
description: How ERP, accounting software, ISV, and ecosystem teams can evaluate NGB Platform as an open-source accounting-first architecture foundation.
---

<script setup>
const ecosystemTeamsChart1 = String.raw`flowchart TD
    A[Business System Requirements] --> B[Business Intent]
    A --> C[Durable Effects]
    A --> D[Auditability]
    A --> E[Reporting]
    A --> F[Vertical Extensibility]

    B --> NGB[NGB Platform]
    C --> NGB
    D --> NGB
    E --> NGB
    F --> NGB

    NGB --> G[Documents]
    NGB --> H[Posting]
    NGB --> I[Registers]
    NGB --> J[Audit History]
    NGB --> K[Canonical and Composable Reports]`

const ecosystemTeamsChart2 = String.raw`flowchart LR
    CRUD[Generic CRUD App Stacks] -->|Fast start but weak business semantics| NGB[NGB Platform]
    ERP[Established ERP Suites] -->|Deep capability but heavy extension model| NGB

    NGB --> CORE[Reusable Platform Core]
    CORE --> DOC[Documents and Catalogs]
    CORE --> POST[Posting and Effects]
    CORE --> REG[Registers]
    CORE --> REP[Reporting]
    CORE --> AUDIT[Auditability]

    NGB --> VERT[Vertical Business Applications]`

const ecosystemTeamsChart3 = String.raw`flowchart TD
    CORE[NGB Platform Core]

    CORE --> META[Definitions and Metadata]
    CORE --> DOC[Documents and Catalogs]
    CORE --> POST[Posting and Effects]
    CORE --> REG[Operational and Reference Registers]
    CORE --> REP[Reporting]
    CORE --> AUDIT[Audit History]

    PM[Property Management] --> CORE
    TR[Trade] --> CORE
    AB[Agency Billing] --> CORE

    PM --> PMB[Leases, Charges, Payments, Receivables]
    TR --> TRB[Trade Documents and Catalogs]
    AB --> ABB[Clients, Projects, Time, Billing]`

const ecosystemTeamsChart4 = String.raw`mindmap
  root((NGB relevance))
    Architecture
      Documents
      Posting
      Registers
      Reporting
    Product
      Vertical reuse
      Faster prototypes
      Better consistency
    Operations
      Auditability
      Traceability
      Durable state
    Ecosystem
      Reference architecture
      Integration opportunities
      Extension patterns`

const ecosystemTeamsChart5 = String.raw`flowchart TD
    A[Read Architecture Brief] --> B[Review Architecture Overview]
    B --> C[Watch Intro Video]
    C --> D[Open Demo Vertical]
    D --> E[Review GitHub Repository]
    E --> F[Read Deep Source Maps]
    F --> G[Provide Architecture Feedback]`
</script>


# NGB for ERP and Accounting Software Ecosystem Teams

This page is for ERP platform teams, accounting software teams, ISV ecosystem teams, consultants, solution architects, and technical product evaluators who want to understand where NGB Platform may fit.

NGB is an open-source, accounting-first platform foundation for building vertical business applications. It focuses on documents, catalogs, durable posting, accounting effects, operational registers, reference registers, audit history, metadata-driven UI, and reporting.

::: info Positioning note
NGB should be read as an open-source platform foundation and reference architecture, not as a finished one-size-fits-all replacement for established ERP products.
:::

## Why this may be relevant

ERP and accounting-centric software has a recurring architectural challenge: serious business systems need more than forms and tables.

They need to preserve business intent, produce durable effects, support auditability, and explain reporting results later.

Common pain points include:

- CRUD-heavy systems that cannot explain business history.
- Business workflows hidden inside ad-hoc service logic.
- Accounting effects that are difficult to trace back to source documents.
- Reporting layers that become disconnected from business meaning.
- Audit history that records changes but not business consequences.
- Vertical extensions that copy patterns instead of reusing a platform core.
- Integration layers that move data but lose context.

NGB is an attempt to model these concerns as first-class platform capabilities.

<MermaidDiagram :chart="ecosystemTeamsChart1" />

## What NGB is

NGB is a reusable architecture and runtime foundation for accounting-aware vertical applications.

It provides platform-level primitives for:

- catalog definitions and runtime catalog handling
- document definitions and document lifecycle
- posting pipelines
- accounting entries
- operational registers
- reference registers
- audit history
- canonical reports
- composable reports
- metadata-driven UI behavior
- vertical-specific modules

It is built with .NET, PostgreSQL, Vue, and deployment patterns intended for modern containerized environments.

## What NGB is not

NGB is not positioned as:

- a finished replacement for every ERP suite
- a vendor-specific marketplace app today
- a generic CRUD scaffolding tool
- only a demo application
- a no-code toy
- a claim of official integration with any ERP vendor ecosystem

The current demo verticals show that the platform concepts can be reused across industries. They are proof points for the platform architecture, not a claim that every vertical is already a complete commercial product.

## Positioning map

NGB sits between generic application frameworks and finished ERP suites.

<MermaidDiagram :chart="ecosystemTeamsChart2" />

This positioning is useful for ecosystem conversations because it frames NGB as:

- a reference architecture
- a reusable runtime foundation
- a vertical solution accelerator
- an integration and extension pattern source

rather than as a simplistic “replacement ERP” claim.

## Current demonstration verticals

NGB currently demonstrates the platform through multiple vertical solutions:

| Vertical | Purpose |
| --- | --- |
| Property Management | Demonstrates leases, charges, receivables, payments, operational state, and accounting/reporting patterns. |
| Trade | Demonstrates commerce-style catalogs, documents, and business flows on the shared platform core. |
| Agency Billing | Demonstrates service/project-style workflows such as clients, projects, time, billing, and related accounting concepts. |

The important point is not that these are identical businesses. The important point is that they reuse the same platform concepts:

<MermaidDiagram :chart="ecosystemTeamsChart3" />

## Core ideas that may be interesting to ERP and accounting teams

### Documents as business intent

A document is not just a row in a table. It is the recorded business intent behind an action: an invoice, payment, lease, charge, timesheet, work order, or other operation.

This gives downstream effects a business explanation.

Read more: [Documents](/architecture/documents)

### Posting as durable effect

Posting turns a document into durable business consequences.

A posted document may create accounting entries, operational register movements, reference register updates, audit events, and reporting-visible state.

Read more: [Accounting and Posting](/architecture/accounting-posting)

### Append-only correction model

Accounting-centric systems need durable history. NGB favors explicit reversals or new effects over silent mutation of posted state.

Read more: [Append-only and Storno](/architecture/append-only-and-storno)

### Operational and reference registers

Not all business truth is GL accounting. NGB uses operational registers for derived operational state and reference registers for effective-dated reference state.

Read more:

- [Operational Registers](/architecture/operational-registers)
- [Reference Registers](/architecture/reference-registers)

### Metadata-driven extensibility

NGB uses definitions and metadata to describe platform objects, support dynamic UI, and reduce vertical-specific duplication.

Read more: [Definitions and Metadata](/architecture/definitions-and-metadata)

### Reporting as a platform subsystem

NGB treats reporting as a first-class subsystem, with canonical and composable paths.

Read more: [Reporting: Canonical and Composable](/architecture/reporting)

## Why this matters in ecosystem conversations

NGB may be relevant to teams exploring:

<MermaidDiagram :chart="ecosystemTeamsChart4" />

Possible use cases include:

- evaluating architecture patterns for accounting-centric systems
- exploring vertical business application foundations
- prototyping new industry solutions
- studying document-to-effect flows
- comparing reporting and auditability approaches
- considering integration patterns around existing accounting or ERP products

## Potential ecosystem angles

NGB may be useful as:

1. **Reference architecture**  
   A concrete open-source example of document-driven, accounting-aware business software architecture.

2. **Vertical solution accelerator**  
   A foundation for quickly modeling a new vertical with catalogs, documents, posting, registers, and reports.

3. **Workflow layer pattern**  
   A way to think about domain-specific document workflows around accounting-aware systems.

4. **Reporting and auditability pattern**  
   A model for making reports traceable to posted business events and source documents.

5. **Integration conversation starter**  
   A foundation for discussing where domain workflows, reporting, and auditability could connect to existing ERP/accounting systems.

## What feedback is valuable

The most valuable feedback would come from people who have worked on:

- ERP platforms
- accounting software
- vertical SaaS
- financial reporting
- audit-heavy business systems
- document-driven enterprise workflows
- ISV ecosystems
- platform extension models

Useful feedback areas:

| Area | Questions |
| --- | --- |
| Architecture | Are the platform boundaries clear? Are the primitives right? |
| Accounting | Is the posting/effect model understandable and extensible? |
| Registers | Are operational/reference registers useful as separate concepts? |
| Reporting | Does the reporting model support real analytical and audit needs? |
| Vertical design | Are the demo verticals good proof points? |
| Integration | Where would NGB fit around existing ERP/accounting systems? |
| Ecosystem | What partner or ISV angle would be credible? |

## Suggested evaluation path

<MermaidDiagram :chart="ecosystemTeamsChart5" />

Start here:

- [NGB Platform Architecture Brief](/architecture/architecture-brief)
- [Evaluation Guide](/ecosystem/evaluation-guide)
- [Architecture Overview](/architecture/overview)
- [Repository Structure](/start-here/repository-structure)
- [Run Locally](/start-here/run-locally)

External links:

- GitHub repository: https://github.com/ngbplatform/NGB
- Documentation: https://docs.ngbplatform.com
- Website: https://ngbplatform.com
- Intro video: https://youtu.be/jeZZZaD8OoM

## Practical next step

If you are evaluating NGB from an ERP, accounting software, or ISV ecosystem perspective, the best next step is to read:

1. [NGB Platform Architecture Brief](/architecture/architecture-brief)
2. [Integration and Extension Opportunities](/ecosystem/integration-and-extension-opportunities)
3. [Evaluation Guide](/ecosystem/evaluation-guide)

Then review the deeper implementation chapters only for the parts most relevant to your evaluation.
