---
title: "Layering and Dependencies"
description: "Dependency direction and layering rules across NGB Platform modules."
---

# Layering and Dependencies

<div class="doc-status-row">
  <span class="doc-badge doc-badge-verified">Verified project references</span>
  <span class="doc-badge doc-badge-inferred">Dependency interpretation</span>
</div>

> Cross-reference:
> - [Layering rules](/reference/layering-rules)
## How to read this page

This page answers two questions:

- **What is allowed to depend on what?**
- **Why is that dependency direction useful in practice?**

## Verified project-boundary anchors

The following `.csproj` files establish the currently verified dependency skeleton:

- `NGB.Runtime/NGB.Runtime.csproj`
- `NGB.PostgreSql/NGB.PostgreSql.csproj`
- `NGB.Api/NGB.Api.csproj`
- `NGB.Hosting.AspNetCore/NGB.Hosting.AspNetCore.csproj`
- `NGB.Runtime.Hosting/NGB.Runtime.Hosting.csproj`
- `NGB.PostgreSql.AspNetCore/NGB.PostgreSql.AspNetCore.csproj`
- `NGB.BackgroundJobs.PostgreSql/NGB.BackgroundJobs.PostgreSql.csproj`
- `NGB.Metadata/NGB.Metadata.csproj`
- `NGB.Definitions/NGB.Definitions.csproj`
- `NGB.Accounting/NGB.Accounting.csproj`
- `NGB.OperationalRegisters/NGB.OperationalRegisters.csproj`
- `NGB.ReferenceRegisters/NGB.ReferenceRegisters.csproj`

## Core dependency picture

Arrows below mean **project dependency**, not request/data flow. This is a selected view;
vertical executable hosts compose these libraries and adapters.

<script setup>
const architectureChart = String.raw`flowchart TB
    subgraph HOSTS[Reusable hosting libraries]
        API[NGB.Api]
        BG[NGB.BackgroundJobs]
        WD[NGB.Watchdog]
        MIG[NGB.Migrator.Core]
        WEB[NGB.Hosting.AspNetCore]
        RTH[NGB.Runtime.Hosting]
    end
    subgraph EXECUTION[Provider-neutral execution and contracts]
        RUNTIME[NGB.Runtime]
        APPABS[NGB.Application.Abstractions]
        CONTRACTS[NGB.Contracts]
        DEFINITIONS[NGB.Definitions]
        METADATA[NGB.Metadata]
        PERSIST[NGB.Persistence]
        ACCOUNTING[NGB.Accounting]
        OR[NGB.OperationalRegisters]
        RR[NGB.ReferenceRegisters]
    end
    subgraph PROVIDERS[PostgreSQL provider and adapters]
        PG[NGB.PostgreSql]
        PGWEB[NGB.PostgreSql.AspNetCore]
        BGPG[NGB.BackgroundJobs.PostgreSql]
    end
    API --> RUNTIME
    API --> APPABS
    API --> CONTRACTS
    API --> WEB
    BG --> RUNTIME
    BG --> PERSIST
    BG --> WEB
    WD --> WEB
    MIG --> PG
    MIG --> PERSIST
    RTH --> RUNTIME
    RUNTIME --> APPABS
    RUNTIME --> CONTRACTS
    RUNTIME --> DEFINITIONS
    RUNTIME --> METADATA
    RUNTIME --> PERSIST
    RUNTIME --> ACCOUNTING
    RUNTIME --> OR
    RUNTIME --> RR
    PG --> PERSIST
    PG --> APPABS
    PG --> CONTRACTS
    PG --> ACCOUNTING
    PG --> OR
    PG --> RR
    PGWEB --> PG
    PGWEB --> WEB
    BGPG --> PERSIST`
</script>

NGB Platform is organized as a layered set of reusable projects.

<MermaidDiagram :chart="architectureChart" />

### Metadata
`NGB.Metadata` is intentionally close to the bottom of the business-model stack. It references only the shared foundation it needs.

**Meaning:** metadata is meant to stay description-oriented, not orchestration-oriented.

### Definitions
`NGB.Definitions` sits above metadata and composes reusable platform description with accounting/register/persistence concepts.

**Meaning:** definitions are where business/application shape becomes concrete enough for runtime consumption.

### Runtime
`NGB.Runtime` depends on contracts, definitions, metadata, persistence abstractions, and the specialized business engines.

**Meaning:** runtime is the orchestration center, not the low-level storage implementation.

### PostgreSQL
`NGB.PostgreSql` depends on persistence abstractions and business modules, plus Dapper/Npgsql/Evolve.

**Meaning:** it is the infrastructure realization of platform abstractions, not the place where high-level workflow policy should live.

### API
`NGB.Api` depends on contracts/application abstractions/runtime and `NGB.Hosting.AspNetCore`.
The host registers PostgreSQL web adapters from `NGB.PostgreSql.AspNetCore` separately.
`NGB.Runtime.Hosting` supplies explicit generic-host startup validation; Runtime does not depend
on its hosting adapter or on the concrete PostgreSQL provider.

**Meaning:** API surface should expose the platform, not become the platform.

## Practical rules

### Rule 1 — runtime should stay provider-agnostic
Runtime can coordinate persistence abstractions, but concrete SQL/Dapper/Npgsql details belong in `NGB.PostgreSql`.

### Rule 2 — definitions describe, runtime executes
If a concern is primarily “what exists” or “how it is described,” it belongs closer to metadata/definitions. If it is “how requests are executed,” it belongs in runtime.

### Rule 3 — hosts compose, they do not own platform logic
Vertical API/background/watchdog hosts should wire the platform together through dependency injection, configuration, and module registration.

### Rule 4 — PostgreSQL should not become a second runtime
Infrastructure can enrich performance and execution, but business workflow semantics should not be split randomly between runtime and storage provider layers.

## Where this matters most

These layering rules are most visible in:

- document CRUD and lifecycle orchestration
- posting/effects execution
- report planning vs SQL execution
- migration discovery and host composition

## Next pages

- [Definitions and Metadata](/architecture/definitions-and-metadata)
- [Runtime Request Flow](/architecture/runtime-request-flow)
- [Runtime Source Map](/platform/runtime-source-map)
- [PostgreSQL Source Map](/platform/postgresql-source-map)
