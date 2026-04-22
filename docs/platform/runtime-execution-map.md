---
title: "Runtime Execution Map"
---

# Runtime Execution Map

This page describes the **verified execution role** of `NGB.Runtime`.

## Verified source anchors

```text
NGB.Runtime/NGB.Runtime.csproj
NGB.Runtime/Documents/DocumentService.cs
NGB.Runtime/Reporting/ReportEngine.cs
```

## Runtime in one sentence

`NGB.Runtime` is the orchestration center that turns platform metadata/definitions plus persistence abstractions into real business operations.

## Dependency shape

The runtime project file directly confirms dependencies on:

- `NGB.Accounting`
- `NGB.Application.Abstractions`
- `NGB.Contracts`
- `NGB.Definitions`
- `NGB.OperationalRegisters`
- `NGB.ReferenceRegisters`
- `NGB.Persistence`
- `NGB.Metadata`

This is the right shape for a runtime core:

- it depends on business engines and abstractions;
- it does **not** depend on a specific provider package like PostgreSQL;
- provider-specific behavior is expected to sit below runtime.

## Document execution center

Verified file:

```text
NGB.Runtime/Documents/DocumentService.cs
```

## What the file explicitly centralizes

From constructor dependencies and public methods, `DocumentService` is the universal runtime façade for:

- document metadata lookup;
- list/page queries;
- single-document reads;
- cross-type lookups by id/query;
- draft create/update/delete;
- post / unpost / repost;
- mark / unmark for deletion;
- derivation actions and draft derivation;
- relationship graph building;
- effects loading;
- payload validation and part validation;
- reference payload enrichment;
- audit write coordination.

## Document execution map

<script setup>
const flowchart1 = String.raw`flowchart TB
    A[document request] --> B[DocumentService]
    B --> C[type metadata + model resolution]
    B --> D[payload and parts validation]
    B --> E[reader / writer / repository abstractions]
    B --> F[posting service]
    B --> G[derivation service]
    B --> H[relationship graph service]
    B --> I[effects query service]
    B --> J[audit log service]`

const flowchart2 = String.raw`flowchart TB
    A[report execute request] --> B[ReportEngine]
    B --> C[definition provider]
    B --> D[layout validator]
    B --> E[execution planner]
    B --> F[plan executor]
    B --> G[sheet builder]
    B --> H[variant resolver]
    B --> I[filter scope expander]
    B --> J[interactive enrichment]
    B --> K[snapshot store]`
</script>

<MermaidDiagram :chart="flowchart1" />

## Important architectural consequence

This file proves that document behavior in NGB is not split randomly across controllers, repositories, and handlers. The runtime service acts as the main coordination surface.

That is one of the core architectural strengths of the platform.

## Reporting execution center

Verified file:

```text
NGB.Runtime/Reporting/ReportEngine.cs
```

## What the file explicitly centralizes

From constructor dependencies and method flow, `ReportEngine` owns:

- definition resolution;
- layout validation;
- execution planning;
- executor invocation;
- sheet building;
- variant resolution;
- filter scope expansion;
- interactive field enrichment;
- rendered-sheet snapshot caching/paging;
- export-sheet execution path.

## Reporting execution map

<MermaidDiagram :chart="flowchart2" />

## What is especially important in this file

### 1. Runtime owns semantics, not SQL

The engine decides:

- the effective request;
- the runtime model;
- the plan;
- whether grouped paging must use rendered-sheet snapshots;
- how diagnostics are surfaced.

### 2. Runtime owns interactive enrichment

The file explicitly enriches document-related report fields with display text and support metadata for UI navigation.

### 3. Runtime owns composable grouped paging behavior

The file has explicit snapshot-based logic for grouped/pivoted composable reports, which is a higher-level concern than raw SQL paging.

## Honest boundary statement

This page does **not** claim that only these two files matter in runtime. It claims that these two files are verified central execution anchors from which the runtime role can be documented confidently.

## Recommended reading order

1. `NGB.Runtime/NGB.Runtime.csproj`
2. `NGB.Runtime/Documents/DocumentService.cs`
3. `NGB.Runtime/Reporting/ReportEngine.cs`

## Continue with

- [PostgreSQL Source Map](/platform/postgresql-source-map)
- [Reporting Execution Map](/platform/reporting-execution-map)
