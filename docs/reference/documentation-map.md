---
title: Documentation Map
description: Complete navigation map of the NGB Platform documentation site.
---

<script setup>
const readingChart = String.raw`flowchart TB
    Home["Documentation home"] --> StartHere["Start Here"]
    Home --> Architecture["Architecture"]
    Home --> Platform["Platform"]
    Home --> Guides["Guides"]
    Home --> Reference["Reference"]

    StartHere --> StartHereDetail["Onboarding, runbooks, reading order"]
    Architecture --> ArchitectureDetail["Layering, execution, business concepts"]
    Platform --> PlatformDetail["Modules, source maps, deep dives, collaborator maps"]
    Guides --> GuidesDetail["Extension workflows and scenario guides"]
    Reference --> ReferenceDetail["Site guide, operational lookup, source indexes"]`
</script>

# Documentation Map

This page is the complete navigation hub for the NGB Platform documentation site.

<div class="doc-status-row">
  <span class="doc-badge doc-badge-verified">Verified: published page inventory</span>
  <span class="doc-badge doc-badge-inferred">Suggested reading routes</span>
</div>

<div class="doc-reading-box">
  <p><strong>How to use this page.</strong> Use this page when you know the kind of question you have but do not remember which section contains the best answer. It mirrors the published sidebar structure so every page in the docs set is reachable from here.</p>
</div>

## Fast routing

| Need | Best page |
|---|---|
| I am new to NGB and need the shortest onboarding path | [Reading Path](/start-here/reading-path) |
| I need local startup instructions | [Run Locally](/start-here/run-locally) |
| I need the high-level architecture | [Architecture Overview](/architecture/overview) |
| I need module boundaries and dependency rules | [Layering and Dependencies](/architecture/layering-and-dependencies) |
| I need to trace runtime orchestration | [Runtime Source Map](/platform/runtime-source-map) |
| I need to trace reporting execution | [Reporting Execution Map](/platform/reporting-execution-map) |
| I need the curated deep-dive set | [Topic Chapters Index](/platform/topic-chapters-index) |
| I need implementation guidance | [Developer Workflows](/guides/developer-workflows) |
| I need configuration keys and environment variables | [Configuration Reference](/reference/configuration-reference) |
| I need source-tracing pages and class-level maps | [Source-Anchored Class Maps](/platform/source-anchored-class-maps) |

## Site topology

<MermaidDiagram :chart="readingChart" />

## Documentation Home

- [Documentation Home](/)

Use the home page for the fastest entry into the site when you want the core links without the full inventory.

## Start Here

- [Platform Overview](/start-here/overview)
- [Reading Path](/start-here/reading-path)
- [Run Locally](/start-here/run-locally)
- [Manual Local Runbook](/start-here/manual-local-runbook)
- [Repository Structure](/start-here/repository-structure)
- [Host Composition](/start-here/host-composition)

Use Start Here before deep-diving into individual modules.

## Architecture

### Core architecture

- [Architecture Overview](/architecture/overview)
- [Layering and Dependencies](/architecture/layering-and-dependencies)
- [Definitions and Metadata](/architecture/definitions-and-metadata)
- [Runtime Request Flow](/architecture/runtime-request-flow)
- [HTTP → Runtime → PostgreSQL Execution Map](/architecture/http-runtime-postgres-execution-map)
- [Host Composition and DI Map](/architecture/host-composition-and-di-map)

### Business model concepts

- [Catalogs](/architecture/catalogs)
- [Documents](/architecture/documents)
- [Document Flow](/architecture/document-flow)
- [Accounting Effects](/architecture/accounting-effects)
- [Reporting: Canonical and Composable](/architecture/reporting)
- [Accounting and Posting](/architecture/accounting-posting)
- [Closing Period](/architecture/closing-period)
- [Operational Registers](/architecture/operational-registers)
- [Reference Registers](/architecture/reference-registers)
- [Derive](/architecture/derive)
- [Append-only and Storno](/architecture/append-only-and-storno)
- [Idempotency and Concurrency](/architecture/idempotency-and-concurrency)

Use Architecture when the question is “how the platform works” rather than “where a file lives”.

## Platform

### Module pages

- [Core and Tools](/platform/core-and-tools)
- [Metadata](/platform/metadata)
- [Definitions](/platform/definitions)
- [Runtime](/platform/runtime)
- [API](/platform/api)
- [Persistence](/platform/persistence)
- [PostgreSQL](/platform/postgresql)
- [Accounting and Registers](/platform/accounting-and-registers)
- [Background Jobs](/platform/background-jobs)
- [Migrator](/platform/migrator)
- [Watchdog](/platform/watchdog)
- [Security and SSO](/platform/security-and-sso)
- [Audit Log](/platform/audit-log)

### Source maps

- [Runtime Source Map](/platform/runtime-source-map)
- [Runtime Execution Map](/platform/runtime-execution-map)
- [Reporting Execution Map](/platform/reporting-execution-map)
- [PostgreSQL Source Map](/platform/postgresql-source-map)
- [API Source Map](/platform/api-source-map)
- [Metadata Source Map](/platform/metadata-source-map)
- [Definitions Source Map](/platform/definitions-source-map)
- [Accounting Source Map](/platform/accounting-source-map)
- [Operational Registers Source Map](/platform/operational-registers-source-map)
- [Reference Registers Source Map](/platform/reference-registers-source-map)
- [Ops Hosts and Bootstraps Source Map](/platform/ops-hosts-and-bootstraps-source-map)
- [Source-Anchored Class Maps](/platform/source-anchored-class-maps)

### Deep dives

- [Topic Chapters Index](/platform/topic-chapters-index)
- [Accounting and Posting Deep Dive](/platform/accounting-posting-deep-dive)
- [Operational Registers Deep Dive](/platform/operational-registers-deep-dive)
- [Reference Registers Deep Dive](/platform/reference-registers-deep-dive)
- [Documents, Flow, Effects, Derive](/platform/documents-flow-effects-deep-dive)
- [Closing Period Deep Dive](/platform/closing-period-deep-dive)
- [Audit Log Deep Dive](/platform/audit-log-deep-dive)
- [Background Jobs Deep Dive](/platform/background-jobs-deep-dive)
- [Migrator Deep Dive](/platform/migrator-deep-dive)
- [Watchdog Deep Dive](/platform/watchdog-deep-dive)

### Dense chapters

- [Runtime Execution Core Dense Source Map](/platform/runtime-execution-core-dense-source-map)
- [Document Subsystem Dense Source Map](/platform/document-subsystem-dense-source-map)
- [Reporting Subsystem Dense Source Map](/platform/reporting-subsystem-dense-source-map)
- [Ops and Tooling Subsystem Dense Source Map](/platform/ops-tooling-dense-source-map)
- [Definitions and Metadata Boundary Dense Source Map](/platform/definitions-metadata-boundary-dense-source-map)
- [Document + Definitions Integration Dense Source Map](/platform/document-definitions-integration-dense-source-map)
- [Reporting + Definitions Integration Dense Source Map](/platform/reporting-definitions-integration-dense-source-map)
- [Document + Reporting Cross-Cutting Integration](/platform/document-reporting-cross-cutting-dense-source-map)

### Collaborator maps

- [Runtime Execution Core Collaborators Map](/platform/runtime-execution-core-collaborators-map)
- [Document Subsystem Collaborators Map](/platform/document-subsystem-collaborators-map)
- [Reporting Class Collaborators Map](/platform/reporting-class-collaborators-map)
- [Ops and Tooling Class Collaborators Map](/platform/ops-tooling-class-collaborators-map)
- [Definitions and Metadata Collaborators Map](/platform/definitions-metadata-collaborators-map)
- [Document + Definitions Integration Collaborators Map](/platform/document-definitions-integration-collaborators-map)
- [Reporting + Definitions Integration Collaborators Map](/platform/reporting-definitions-integration-collaborators-map)
- [Document + Reporting Cross-Cutting Collaborators](/platform/document-reporting-cross-cutting-collaborators-map)

### Supporting models

- [API runtime PostgreSQL integration](/platform/api-runtime-postgres-integration)
- [Platform document persistence model](/platform/platform-document-persistence-model)

Use Platform when you need responsibility boundaries, verified anchors, execution paths, or deep subsystem context.

## Guides

### Core workflows

- [Developer Workflows](/guides/developer-workflows)
- [Platform Extension Points](/guides/platform-extension-points)
- [Add a Document with Accounting and Registers](/guides/add-document-with-accounting-and-registers)
- [Add a Canonical Report](/guides/add-canonical-report-workflow)
- [Add a Composable Report](/guides/add-composable-report-workflow)

### Scenario guides

- [Guide: Business Partners Catalog](/guides/catalogs/business-partners)
- [Guide: Sales Invoice](/guides/documents/sales-invoice)
- [Guide: Item Price Update](/guides/documents/item-price-update)
- [Guide: Inventory and Receivables Operational Registers](/guides/operational-registers/inventory-and-receivables)
- [Guide: Item Pricing Reference Register](/guides/reference-registers/item-pricing)
- [Guide: Canonical and Composable Reports](/guides/reports/canonical-and-composable)

Use Guides when you are changing code, extending a vertical, or using the docs as an implementation checklist.

## Reference

### Site guide

- [Documentation Map](/reference/documentation-map)
- [Documentation Consolidation Guide](/reference/docs-consolidation-guide)

### Operational reference

- [Platform Projects](/reference/platform-projects)
- [Configuration Reference](/reference/configuration-reference)
- [Background Job Catalog](/reference/background-job-catalog)
- [Migrator CLI](/reference/migrator-cli)
- [Platform API Surface](/reference/platform-api-surface)
- [Layering Rules](/reference/layering-rules)
- [Database Naming Quick Reference](/reference/database-naming)
- [Database Naming and DDL Patterns](/reference/database-naming-and-ddl-patterns)

### Verified anchor sets

- [Definitions/Metadata Boundary Verified Anchors](/reference/definitions-metadata-boundary-verified-anchors)
- [Document Subsystem Verified Anchors](/reference/document-subsystem-verified-anchors)
- [Reporting Subsystem Verified Anchors](/reference/reporting-subsystem-verified-anchors)
- [Runtime Execution Core Verified Anchors](/reference/runtime-execution-core-verified-anchors)
- [Ops and Tooling Verified Anchors](/reference/ops-tooling-verified-anchors)
- [Document + Definitions Integration Verified Anchors](/reference/document-definitions-integration-verified-anchors)
- [Reporting + Definitions Integration Verified Anchors](/reference/reporting-definitions-integration-verified-anchors)
- [Document + Reporting Cross-Cutting Verified Anchors](/reference/document-reporting-cross-cutting-verified-anchors)

Use Reference when you need site guidance, operational lookup pages, or narrow source indexes.

## Trust model for this documentation set

This site intentionally separates:

- **source-tracing pages**;
- **architecture synthesis**;
- **template guidance** for extending the platform.

That separation keeps the site readable for day-to-day engineering work without collapsing overview pages, implementation maps, and extension guides into one layer.
