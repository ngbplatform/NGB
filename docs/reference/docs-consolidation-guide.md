---
title: "Documentation Consolidation Guide"
description: "How the NGB Platform documentation set is organized across onboarding, architecture, guides, source maps, and reference pages."
---

# Documentation Consolidation Guide

<div class="doc-badge-row">
  <span class="doc-badge doc-badge--verified">Consolidation</span>
  <span class="doc-badge doc-badge--inferred">Editorial</span>
</div>

<div class="doc-callout">
This page explains how the documentation set is structured today. It is an editorial guide for maintainers and readers.
</div>

## What this page is for

Use this page when you need to answer one of these questions:

- Where should a new reader start?
- Which pages are architectural overview pages versus source-anchored pages?
- Which pages are extension guides versus reference pages?
- Where should new documentation be added so the structure stays clean?

## Documentation layers

### 1. Start Here

The **Start Here** layer is for onboarding and orientation.

Use it for:

- what NGB Platform is;
- how the repository is organized;
- how to approach the docs in the right order;
- first-stop navigation for new contributors.

Primary pages:

- [Platform Overview](/start-here/overview)
- [Repository Structure](/start-here/repository-structure)
- [Host composition](/start-here/host-composition)
- [Reading path](/start-here/reading-path)

### 2. Architecture

The **Architecture** layer explains the platform as a system.

Use it for:

- layering and dependency direction;
- metadata/definitions/runtime flow;
- host composition;
- platform execution flow from HTTP to runtime to persistence.

Primary pages:

- [Architecture Overview](/architecture/overview)
- [Layering and Dependencies](/architecture/layering-and-dependencies)
- [Definitions and Metadata](/architecture/definitions-and-metadata)
- [Runtime Request Flow](/architecture/runtime-request-flow)
- [HTTP → Runtime → PostgreSQL Execution Map](/architecture/http-runtime-postgres-execution-map)
- [Host Composition and DI Map](/architecture/host-composition-and-di-map)

### 3. Platform module pages

The **Platform Modules** layer explains each reusable platform project or subsystem in business/architectural terms.

Use it for:

- responsibilities of a module;
- module boundaries;
- extension role in the platform;
- high-level connection to runtime and persistence.

These pages should link to source maps whenever source-anchored pages exist.

### 4. Source maps

The **Source Maps** layer is where documentation becomes explicitly source-anchored.

Use it for:

- verified file anchors;
- execution paths through concrete files;
- composition points;
- source-backed explanations.

Primary pages:

- [Runtime Source Map](/platform/runtime-source-map)
- [PostgreSQL Source Map](/platform/postgresql-source-map)
- [API source map](/platform/api-source-map)
- [Metadata source map](/platform/metadata-source-map)
- [Definitions source map](/platform/definitions-source-map)
- [Accounting source map](/platform/accounting-source-map)
- [Operational Registers source map](/platform/operational-registers-source-map)
- [Reference Registers source map](/platform/reference-registers-source-map)
### 5. Developer workflows and guides

The **Guides** layer is for implementation work.

Use it for:

- how to extend the platform;
- how to add new catalogs/documents/reports;
- what to register and where;
- template guidance and recommended patterns.

Primary pages:

- [Developer Workflows](/guides/developer-workflows)
- [Platform Extension Points](/guides/platform-extension-points)
- [Add a Document with Accounting and Registers](/guides/add-document-with-accounting-and-registers)
- [Add a Canonical Report](/guides/add-canonical-report-workflow)
- [Add a Composable Report](/guides/add-composable-report-workflow)

### 6. Reference

The **Reference** layer is for stable lookup pages and site guidance.

Use it for:

- map/index pages.
- configuration and command lookup;
- narrow source indexes when you need subsystem-specific tracing.

Primary pages:

- [Documentation Map](/reference/documentation-map)
- [Documentation Consolidation Guide](/reference/docs-consolidation-guide)
- [Configuration Reference](/reference/configuration-reference)
- [Platform Projects](/reference/platform-projects)
- [Runtime Execution Core Verified Anchors](/reference/runtime-execution-core-verified-anchors)
## Canonical entry points

To avoid duplication, treat these pages as canonical:

| Question | Canonical page |
| --- | --- |
| Where do I start reading? | [Reading path](/start-here/reading-path) |
| How is the whole docs set organized? | [Documentation Map](/reference/documentation-map) |
| Where do deep dives and dense chapters live? | [Topic Chapters Index](/platform/topic-chapters-index) |
| Where do I trace implementation boundaries next? | [Source-Anchored Class Maps](/platform/source-anchored-class-maps) |
| Where do I find configuration and runtime commands? | [Configuration Reference](/reference/configuration-reference) |
| Which page lists the reusable platform projects? | [Platform Projects](/reference/platform-projects) |

## Rules for future additions

### Add to Start Here when...
- the page is for onboarding;
- the reader should encounter it early;
- it explains how to navigate the docs or repo.

### Add to Architecture when...
- the page explains system structure or execution flow;
- the content is conceptual and platform-wide;
- the page is not primarily about one project file.

### Add to Platform when...
- the page explains one reusable module or subsystem;
- the content is platform-level and stable;
- the page should remain valid even if file paths shift slightly.

### Add to Source Maps when...
- the page must name concrete files;
- the goal is source-traceability;
- you are distinguishing verified anchors from inference.

### Add to Guides when...
- the page explains how to implement or extend something;
- the content is procedural;
- template guidance is acceptable and clearly marked as such.

### Add to Reference when...
- the page is a stable lookup page;
- the content is index-oriented, operational, or lookup-focused.

## Maintainability note

The docs set stays strong only if these layers remain distinct.

The most common ways documentation quality degrades are:

- architecture pages becoming overloaded with procedural details;
- guide pages pretending to be verified source maps;
- source-map pages mixing unverified assumptions with verified anchors;
- navigation/index pages duplicating too much content instead of linking.

## Related pages

- [Reading path](/start-here/reading-path)
- [Documentation Map](/reference/documentation-map)
- [Topic chapters index](/platform/topic-chapters-index)
- [Configuration reference](/reference/configuration-reference)
- [Source-Anchored Class Maps](/platform/source-anchored-class-maps)
