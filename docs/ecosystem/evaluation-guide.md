---
title: Evaluation Guide
description: A short evaluation path for architects, ERP/accounting software teams, partners, contributors, and open-source reviewers.
---

<script setup>
const evaluationGuideChart1 = String.raw`flowchart TD
    A[Website] --> B[Architecture Brief]
    B --> C[Architecture Overview]
    C --> D[Intro Video]
    D --> E[Demo Vertical]
    E --> F[Repository Structure]
    F --> G[Deep Technical Docs]
    G --> H[Feedback / Discussion]`

const evaluationGuideChart2 = String.raw`flowchart TD
    A[Evaluation Question] --> B{Area}
    B -->|Business model| C[Documents, Catalogs, Document Flow]
    B -->|Accounting| D[Accounting and Posting]
    B -->|Operational state| E[Operational Registers]
    B -->|Effective reference state| F[Reference Registers]
    B -->|Reports| G[Reporting]
    B -->|Runtime implementation| H[Runtime Source Maps]
    B -->|Persistence| I[PostgreSQL Source Map]
    B -->|Deployment/ops| J[Background Jobs, Migrator, Watchdog]`

const evaluationGuideChart3 = String.raw`flowchart TD
    A[NGB for ERP and Accounting Software Ecosystem Teams] --> B[Architecture Brief]
    B --> C[Integration and Extension Opportunities]
    C --> D[Demo Vertical]
    D --> E[Reporting and Posting Docs]
    E --> F[Feedback]`

const evaluationGuideChart4 = String.raw`flowchart TD
    A[Architecture Brief] --> B[Layering and Dependencies]
    B --> C[Runtime Request Flow]
    C --> D[Runtime Execution Core Dense Source Map]
    D --> E[PostgreSQL Source Map]
    E --> F[Run Locally]`
</script>


# Evaluation Guide

This guide helps architects, ERP/accounting software teams, consultants, partners, contributors, and open-source reviewers evaluate NGB Platform quickly.

NGB has deep documentation, but a first evaluation should not start with every source map or subsystem detail. Start with the platform idea, then follow the architecture path only as deep as needed.

::: tip Recommended first pass
Use this guide as a 15–30 minute evaluation path. After that, decide whether to go deeper into runtime, documents, posting, registers, reporting, or deployment.
:::

## Who should read this

This guide is useful if you are:

- reviewing NGB as an open-source architecture
- evaluating NGB for an ERP/accounting ecosystem conversation
- exploring vertical SaaS architecture patterns
- looking for .NET + PostgreSQL examples of serious business software
- deciding whether to run the demo locally
- preparing architecture feedback

## What you can understand in 15–30 minutes

In a short first pass, you should be able to answer:

1. What is NGB?
2. Why is it accounting-first?
3. What are the main platform concepts?
4. How do documents become durable effects?
5. How do verticals reuse the same platform core?
6. Where do reporting and auditability fit?
7. Where should deeper evaluation continue?

## Recommended evaluation path

<MermaidDiagram :chart="evaluationGuideChart1" />

### Step 1: Open the website

Start with the public website:

- https://ngbplatform.com

Goal:

- understand the public positioning
- see demo links
- understand the main product message
- confirm that NGB is a platform foundation, not only one vertical app

Suggested time: 2–3 minutes.

### Step 2: Read the architecture brief

Read:

- [NGB Platform Architecture Brief](/architecture/architecture-brief)

Goal:

- understand the platform model
- understand documents, posting, registers, and reporting at a high level
- decide which deeper pages matter for your evaluation

Suggested time: 5–7 minutes.

### Step 3: Read the architecture overview

Read:

- [Architecture Overview](/architecture/overview)

Goal:

- understand the current documentation’s main architecture entry point
- map the platform layers
- understand how the runtime, infrastructure, and vertical modules relate

Suggested time: 5–10 minutes.

### Step 4: Watch the intro video

Watch:

- https://youtu.be/jeZZZaD8OoM

Goal:

- get a quick visual explanation of the platform
- understand the public story and demo orientation

Suggested time: 1–2 minutes.

### Step 5: Open a demo vertical

Use the demo links from the website or README.

Goal:

- see how the platform concepts surface in a real vertical
- look for documents, catalogs, reporting, auditability, and navigation
- evaluate whether the UI reflects the metadata-driven platform idea

Suggested time: 5–10 minutes.

### Step 6: Review repository structure

Read:

- [Repository Structure](/start-here/repository-structure)

Goal:

- understand the solution layout
- identify platform modules
- identify vertical modules
- see where runtime, PostgreSQL, API hosts, migrator, jobs, and UI live

Suggested time: 5 minutes.

### Step 7: Go deeper only where needed

Use the topic map below.

<MermaidDiagram :chart="evaluationGuideChart2" />

## Topic-based reading guide

### If you care about business modeling

Read:

- [Catalogs](/architecture/catalogs)
- [Documents](/architecture/documents)
- [Document Flow](/architecture/document-flow)
- [Definitions and Metadata](/architecture/definitions-and-metadata)

Questions to ask:

- Are documents modeled as business intent?
- Is master data separate from transactional documents?
- Can document relationships be explained to a user?
- Does metadata reduce repeated UI/API work?

### If you care about accounting and posting

Read:

- [Accounting and Posting](/architecture/accounting-posting)
- [Accounting Effects](/architecture/accounting-effects)
- [Append-only and Storno](/architecture/append-only-and-storno)
- [Idempotency and Concurrency](/architecture/idempotency-and-concurrency)

Questions to ask:

- What happens when a document is posted?
- Are durable effects explicit?
- How are corrections represented?
- How does the architecture protect against duplicate or inconsistent posting?

### If you care about operational business state

Read:

- [Operational Registers](/architecture/operational-registers)
- [Reference Registers](/architecture/reference-registers)
- [Closing Period](/architecture/closing-period)

Questions to ask:

- Which state is accounting state and which state is operational?
- How is derived business truth persisted?
- How are effective-dated values represented?
- What does period closing protect?

### If you care about reporting

Read:

- [Reporting: Canonical and Composable](/architecture/reporting)
- [Reporting Execution Map](/platform/reporting-execution-map)
- [Reporting Subsystem Dense Source Map](/platform/reporting-subsystem-dense-source-map)

Questions to ask:

- Which reports are canonical?
- Which reports are composable?
- Can report rows be traced to documents or effects?
- Does reporting read durable state instead of reconstructing business meaning ad hoc?

### If you care about implementation depth

Read:

- [Runtime Execution Core Dense Source Map](/platform/runtime-execution-core-dense-source-map)
- [Document Subsystem Dense Source Map](/platform/document-subsystem-dense-source-map)
- [Definitions and Metadata Boundary Dense Source Map](/platform/definitions-metadata-boundary-dense-source-map)
- [Document + Reporting Cross-Cutting Integration](/platform/document-reporting-cross-cutting-dense-source-map)

Questions to ask:

- Are platform responsibilities separated cleanly?
- Are vertical concerns isolated from platform concerns?
- Are the extension points explicit?
- Is PostgreSQL used as the system of record?

## Evaluation checklist

Use this checklist during a first serious review.

| Area | What to check |
| --- | --- |
| Positioning | Is NGB clearly a platform foundation rather than only one app? |
| Architecture | Are layers and responsibilities understandable? |
| Documents | Are business actions modeled as documents? |
| Posting | Are durable effects explicit and traceable? |
| Accounting | Is double-entry impact modeled through platform concepts? |
| Registers | Are non-GL operational states modeled separately? |
| Reporting | Are reports first-class and source-linked? |
| Metadata | Does metadata drive UI/API behavior consistently? |
| Vertical reuse | Do multiple verticals reuse the same core concepts? |
| Persistence | Is PostgreSQL the durable system of record? |
| Operations | Are migrator, background jobs, and watchdog concerns visible? |
| Open source readiness | Are license, contribution, security, and docs present? |

## Suggested questions for architecture feedback

If you are preparing feedback, these questions are especially useful:

1. Is the document/posting/register model clear?
2. Is the separation between intent and effect useful?
3. Are operational registers and reference registers understandable as distinct concepts?
4. Does the reporting architecture match real accounting/ERP needs?
5. Is the vertical extension model credible?
6. What would make NGB easier to evaluate?
7. What integration angle would be most credible?
8. What is missing for an ERP/accounting ecosystem conversation?
9. What should be simplified?
10. What should be made more explicit?

## Fast path for ERP/accounting ecosystem readers

If your evaluation is specifically from an ERP, accounting software, ISV, or partner ecosystem perspective, use this shorter path:

<MermaidDiagram :chart="evaluationGuideChart3" />

Read:

1. [NGB for ERP and Accounting Software Ecosystem Teams](/ecosystem/erp-accounting-software-teams)
2. [NGB Platform Architecture Brief](/architecture/architecture-brief)
3. [Integration and Extension Opportunities](/ecosystem/integration-and-extension-opportunities)
4. [Accounting and Posting](/architecture/accounting-posting)
5. [Reporting: Canonical and Composable](/architecture/reporting)

## Fast path for .NET architects

If your evaluation is mostly technical, use this path:

<MermaidDiagram :chart="evaluationGuideChart4" />

Read:

1. [NGB Platform Architecture Brief](/architecture/architecture-brief)
2. [Layering and Dependencies](/architecture/layering-and-dependencies)
3. [Runtime Request Flow](/architecture/runtime-request-flow)
4. [Runtime Execution Core Dense Source Map](/platform/runtime-execution-core-dense-source-map)
5. [PostgreSQL Source Map](/platform/postgresql-source-map)
6. [Run Locally](/start-here/run-locally)

## Run locally

If the first evaluation is positive, run the project locally:

- [Run Locally](/start-here/run-locally)
- [Manual Local Runbook](/start-here/manual-local-runbook)
- [Host Composition](/start-here/host-composition)

Local evaluation should focus on:

- API startup
- migrations
- demo data
- UI navigation
- catalog/document flows
- posting behavior
- reporting pages
- operational tooling

## External links

- GitHub repository: https://github.com/ngbplatform/NGB
- Documentation: https://docs.ngbplatform.com
- Website: https://ngbplatform.com
- Intro video: https://youtu.be/jeZZZaD8OoM

## Related documentation

- [NGB Platform Architecture Brief](/architecture/architecture-brief)
- [NGB for ERP and Accounting Software Ecosystem Teams](/ecosystem/erp-accounting-software-teams)
- [Integration and Extension Opportunities](/ecosystem/integration-and-extension-opportunities)
- [Architecture Overview](/architecture/overview)
- [Repository Structure](/start-here/repository-structure)
- [Documentation Map](/reference/documentation-map)
