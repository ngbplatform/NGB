---
title: "Runtime"
description: "NGB.Runtime as the orchestration center for documents, workflows, effects, derivations, and reporting."
---

# Runtime

<div class="doc-status-row">
  <span class="doc-badge doc-badge-verified">Verified anchors</span>
  <span class="doc-badge doc-badge-inferred">Module interpretation</span>
</div>

> File-level companion page: [Runtime Source Map](/platform/runtime-source-map)

## What Runtime is

`NGB.Runtime` is the platform’s orchestration center. It sits above metadata/definitions/persistence abstractions and coordinates the execution of platform use cases.

## Verified anchors

- `NGB.Runtime/NGB.Runtime.csproj`
- `NGB.Runtime/Documents/DocumentService.cs`
- `NGB.Runtime/Reporting/ReportEngine.cs`
- `NGB.Runtime/Reporting/ReportExecutionPlanner.cs`

## Responsibilities visible from verified files

### 1. Metadata-driven document orchestration
`DocumentService.cs` shows that runtime owns the universal document CRUD/lifecycle orchestration pattern.

Visible concerns include:

- type metadata resolution
- draft create/update/delete
- posting/unposting/reposting/mark-for-deletion
- derivation entry points
- relationship graph loading
- effective effects/UI-effects shaping

### 2. Reporting orchestration
`ReportEngine.cs` and `ReportExecutionPlanner.cs` show that runtime owns report request normalization, validation, planning, and response shaping.

Visible concerns include:

- report definition resolution
- effective layout generation
- filter/variant expansion
- logical plan building
- executor invocation
- rendered sheet paging and diagnostics

### 3. Provider-agnostic execution center
The `NGB.Runtime.csproj` dependency shape shows runtime depends on definitions/metadata/engines/persistence abstractions rather than only on a concrete PostgreSQL implementation.

## What Runtime should not become

Runtime should not become:

- a bag of SQL
- a duplicate API/controller layer
- a host composition layer
- an infra-specific optimization dump

## Best way to study Runtime

Read in this order:

1. `NGB.Runtime/Documents/DocumentService.cs`
2. `NGB.Runtime/Reporting/ReportEngine.cs`
3. `NGB.Runtime/Reporting/ReportExecutionPlanner.cs`
4. [Runtime Execution Map](/platform/runtime-execution-map)

## Extension mindset

When adding new behavior, prefer these questions:

- Is this a new **definition/descriptor**?  
  Start in metadata/definitions.

- Is this a new **orchestration behavior**?  
  Add it to runtime.

- Is this a new **storage/provider implementation**?  
  Add it to PostgreSQL.

## Continue with

- [Platform Extension Points](/guides/platform-extension-points)
- [Add a Document with Accounting and Registers](/guides/add-document-with-accounting-and-registers)
- [Add a Canonical Report](/guides/add-canonical-report-workflow)
- [Add a Composable Report](/guides/add-composable-report-workflow)
