---
title: Integration and Extension Opportunities
description: Practical ways NGB Platform can be evaluated as a standalone vertical foundation, workflow layer, reporting core, audit model, or integration-oriented architecture.
---

<script setup>
const integrationOpportunitiesChart1 = String.raw`flowchart TD
    A[Business Intent] --> B[Documents]
    B --> C[Posting]
    C --> D[Accounting Effects]
    C --> E[Operational Registers]
    C --> F[Reference Registers]
    C --> G[Audit History]

    D --> H[Financial Reporting]
    E --> I[Operational Reporting]
    F --> J[Effective Reference Views]
    G --> K[Traceability]

    EXT[External Systems] -. integration boundary .-> B
    EXT -. integration boundary .-> C
    EXT -. integration boundary .-> H`

const integrationOpportunitiesChart2 = String.raw`flowchart TD
    NGB[NGB Platform]

    NGB --> A[Standalone Vertical Solution]
    NGB --> B[Workflow Layer Around ERP / Accounting Systems]
    NGB --> C[Document and Audit Layer]
    NGB --> D[Reporting and Reconciliation Core]
    NGB --> E[Prototype / Accelerator Platform]
    NGB --> F[Reference Architecture]`

const integrationOpportunitiesChart3 = String.raw`flowchart LR
    UI[Business Users] --> NGB[NGB Vertical Workflow Layer]
    NGB --> DOC[Domain Documents]
    NGB --> OR[Operational Registers]
    NGB --> AUDIT[Audit History]
    NGB --> EVENTS[Integration Events / Exports]
    EVENTS --> ERP[External ERP / Accounting System]
    ERP --> FIN[Corporate Financial Reporting]`

const integrationOpportunitiesChart4 = String.raw`flowchart LR
    DEF[Definitions and Metadata] --> CAT[Catalog Types]
    DEF --> DOC[Document Types]
    DOC --> POST[Posting Handlers]
    DOC --> FLOW[Document Flow]
    DEF --> UI[Metadata-Driven UI]
    REP[Reporting] --> CAN[Canonical Reports]
    REP --> COMP[Composable Reports]
    POST --> ACC[Accounting Effects]
    POST --> OR[Operational Registers]
    POST --> RR[Reference Registers]`

const integrationOpportunitiesChart5 = String.raw`flowchart LR
    IDP[Identity Provider] --> API[API Hosts]
    CLIENT[Web Client / API Consumers] --> API
    EXT[External Systems] --> API
    API --> RT[Runtime]
    RT --> DB[(PostgreSQL)]
    RT --> REP[Reporting]
    RT --> AUDIT[Audit History]
    RT --> EVENTS[Export / Integration Events]
    EVENTS --> EXT2[ERP / Accounting / Analytics Systems]`

const integrationOpportunitiesChart6 = String.raw`flowchart TD
    NGB[NGB] --> A[Document-Level Integration]
    NGB --> B[Posting/Event-Level Integration]
    NGB --> C[Report-Level Integration]
    NGB --> D[Master Data Synchronization]
    NGB --> E[Audit/Trace Export]

    A --> X[External ERP / Accounting System]
    B --> X
    C --> X
    D --> X
    E --> X`
</script>


# Integration and Extension Opportunities

NGB Platform is designed as an accounting-first foundation for vertical business applications. It can be evaluated in more than one way: as a standalone platform core, a vertical solution accelerator, a document/workflow layer, a reporting and auditability model, or a source of integration patterns around existing business systems.

This page does not claim that every integration path already exists as a finished packaged connector. Instead, it describes practical fit patterns and extension surfaces that can guide architecture discussions.

::: info Current scope
NGB is an open-source platform foundation and reference implementation. Vendor-specific connectors, certified marketplace apps, and production integration packages should be treated as future implementation work unless explicitly present in the repository.
:::

## Why integration and extension matter

Business software rarely lives alone. Even when a system owns a vertical workflow, it may still need to interact with:

- identity providers
- accounting systems
- ERP suites
- reporting tools
- import/export pipelines
- document storage
- external APIs
- operational data sources
- analytics platforms

NGB is useful to evaluate because its architecture separates business intent, durable effects, registers, and reporting.

That separation makes it easier to discuss where integration should happen.

<MermaidDiagram :chart="integrationOpportunitiesChart1" />

## Possible fit patterns

NGB can be evaluated through several fit patterns.

<MermaidDiagram :chart="integrationOpportunitiesChart2" />

### Pattern 1: Standalone vertical solution

In this pattern, NGB owns the core vertical workflow.

Examples:

- Property Management style vertical
- Trade style vertical
- Agency Billing style vertical
- Other industry-specific business systems

NGB provides:

- catalogs
- documents
- posting logic
- operational registers
- reference registers
- reporting
- audit history
- UI metadata
- background operations

This pattern is the most direct use of the platform.

### Pattern 2: Workflow layer around an accounting system

In this pattern, NGB owns domain-specific workflows while another system remains the official accounting or ERP system.

NGB may handle:

- vertical documents
- approvals
- operational state
- audit trail
- workflow-specific reporting
- integration-ready accounting events

The external system may handle:

- general ledger
- statutory accounting
- tax
- payroll
- enterprise-wide master data
- financial consolidation

<MermaidDiagram :chart="integrationOpportunitiesChart3" />

### Pattern 3: Document and audit layer

In this pattern, NGB is evaluated as a strong model for document-driven workflows and auditability.

The key question is:

> Can business intent and business effects be made traceable enough that users and auditors can understand what happened?

NGB’s architecture provides a useful reference for:

- document lifecycle
- document flow
- posted effects
- audit trails
- source-linked reports
- explicit reversal semantics

### Pattern 4: Reporting and reconciliation core

In this pattern, NGB is evaluated for its reporting architecture.

NGB separates:

- canonical reports for known business views
- composable reports for metadata-driven analysis
- durable effects as the reporting source
- drilldown paths back to documents/effects

This is useful where reporting must be explainable, not only visually flexible.

### Pattern 5: Prototype and accelerator platform

NGB may also be useful as a prototype foundation for new vertical ideas.

Instead of starting with a blank CRUD stack, a team can evaluate whether the platform primitives already provide:

- document lifecycle
- catalog management
- posting behavior
- reporting
- auditability
- background jobs
- migration discipline
- PostgreSQL system-of-record patterns

## Extension model

NGB is intended to be extended by adding vertical-specific definitions and behavior on top of shared platform primitives.

<MermaidDiagram :chart="integrationOpportunitiesChart4" />

### Catalog extension

A vertical can add catalog types for business master data.

Examples:

- customers
- tenants
- vendors
- properties
- units
- items
- services
- employees
- projects

Catalogs should carry stable business identity and be exposed consistently through metadata and runtime services.

### Document extension

A vertical can add document types for business processes.

Examples:

- invoice
- payment
- lease
- charge
- work order
- timesheet
- contract
- credit memo

Documents are the primary place where business intent enters the system.

### Posting extension

A vertical can add posting handlers to turn document intent into durable effects.

Posting handlers are where business rules become platform-visible consequences:

- GL entries
- operational register movements
- reference register updates
- relationships
- audit events
- reporting-visible state

### Reporting extension

A vertical can add reports in two broad categories:

- **Canonical reports** for business-critical, known views.
- **Composable reports** for metadata-driven exploration.

Read more:

- [Reporting: Canonical and Composable](/architecture/reporting)
- [Add a Canonical Report](/guides/add-canonical-report-workflow)
- [Add a Composable Report](/guides/add-composable-report-workflow)

## Integration model

NGB can be integrated at different architectural boundaries.

<MermaidDiagram :chart="integrationOpportunitiesChart5" />

### Identity and access

NGB can be evaluated with external identity and SSO scenarios in mind.

Typical concerns include:

- authentication
- authorization
- user identity projection
- tenant/company context
- audit attribution

Read more: [Security and SSO](/platform/security-and-sso)

### Data import

Potential import scenarios include:

- opening balances
- catalog/master data import
- source documents
- historical operational state
- reference data
- configuration and policies

Import strategy should preserve business meaning. For accounting-centric systems, importing “just rows” is often not enough.

### Data export

Potential export scenarios include:

- accounting events
- report extracts
- reconciliation data
- audit extracts
- document summaries
- operational register snapshots

Export design should be explicit about whether it exports:

- source documents
- posted effects
- current derived state
- report output
- audit history

### ERP/accounting integration

An ERP/accounting integration can happen at several levels:

<MermaidDiagram :chart="integrationOpportunitiesChart6" />

Each level has different trade-offs.

| Level | Typical use |
| --- | --- |
| Document-level | Send domain documents or document summaries to another system. |
| Posting/event-level | Send durable accounting or operational effects. |
| Report-level | Export reconciled views or statements. |
| Master data sync | Keep catalogs aligned with external systems. |
| Audit export | Provide traceability and compliance evidence. |

## What NGB does best

NGB is strongest when the problem needs:

- document-driven business workflows
- durable posted effects
- accounting-aware design
- explicit auditability
- operational state derived from posted business activity
- reporting tied to business truth
- reusable platform primitives across verticals
- PostgreSQL-first persistence
- .NET backend architecture

## Where custom integration is still required

Custom integration would still be required for:

- specific ERP/accounting vendor APIs
- certified marketplace packaging
- vendor-specific authentication flows
- external tax systems
- payroll integrations
- banking integrations
- payment processor integrations
- data migration from legacy systems
- enterprise reporting/data warehouse integrations
- customer-specific business rules

NGB provides the platform foundation. It does not eliminate the need for domain-specific integration design.

## Suggested exploration paths

### For architects

Start with:

1. [NGB Platform Architecture Brief](/architecture/architecture-brief)
2. [Architecture Overview](/architecture/overview)
3. [Layering and Dependencies](/architecture/layering-and-dependencies)
4. [Runtime Request Flow](/architecture/runtime-request-flow)

### For ERP/accounting ecosystem teams

Start with:

1. [NGB for ERP and Accounting Software Ecosystem Teams](/ecosystem/erp-accounting-software-teams)
2. [Evaluation Guide](/ecosystem/evaluation-guide)
3. [Accounting and Posting](/architecture/accounting-posting)
4. [Reporting: Canonical and Composable](/architecture/reporting)

### For vertical solution builders

Start with:

1. [Platform Extension Points](/guides/platform-extension-points)
2. [Add a Document with Accounting and Registers](/guides/add-document-with-accounting-and-registers)
3. [Add a Canonical Report](/guides/add-canonical-report-workflow)
4. [Add a Composable Report](/guides/add-composable-report-workflow)

## Evaluation questions

When evaluating an integration or extension opportunity, ask:

- What system owns the official accounting truth?
- What system owns vertical workflow intent?
- Which effects must be append-only?
- Which reports must be explainable and traceable?
- Which data should be synchronized vs derived?
- Which business actions need audit attribution?
- Which parts are generic platform behavior and which are vertical-specific?
- Is the goal a standalone vertical, an extension layer, or a reference architecture?

## Related pages

- [NGB Platform Architecture Brief](/architecture/architecture-brief)
- [NGB for ERP and Accounting Software Ecosystem Teams](/ecosystem/erp-accounting-software-teams)
- [Evaluation Guide](/ecosystem/evaluation-guide)
- [Architecture Overview](/architecture/overview)
- [Accounting and Posting](/architecture/accounting-posting)
- [Operational Registers](/architecture/operational-registers)
- [Reference Registers](/architecture/reference-registers)
- [Reporting: Canonical and Composable](/architecture/reporting)
- [Platform Extension Points](/guides/platform-extension-points)
