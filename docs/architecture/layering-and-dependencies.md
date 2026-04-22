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
- `NGB.Metadata/NGB.Metadata.csproj`
- `NGB.Definitions/NGB.Definitions.csproj`
- `NGB.Accounting/NGB.Accounting.csproj`
- `NGB.OperationalRegisters/NGB.OperationalRegisters.csproj`
- `NGB.ReferenceRegisters/NGB.ReferenceRegisters.csproj`

## Core dependency picture

<script setup>
const architectureChart = String.raw`flowchart TB
    classDef host fill:#fff4e5,stroke:#c77d1a,stroke-width:1.5px,color:#111827;
    classDef surface fill:#eef4ff,stroke:#2f5fb3,stroke-width:1.5px,color:#111827;
    classDef runtime fill:#eafaf1,stroke:#1f8f57,stroke-width:1.8px,color:#111827;
    classDef engine fill:#f3ecff,stroke:#7c3aed,stroke-width:1.5px,color:#111827;
    classDef infra fill:#f7f7f7,stroke:#6b7280,stroke-width:1.5px,color:#111827;
    classDef external fill:#ffffff,stroke:#9ca3af,stroke-width:1.2px,color:#111827;

    subgraph HOSTS[Platform Hosts]
        API[NGB.Api<br/>HTTP / API host]
        BG[NGB.BackgroundJobs<br/>Scheduled job host]
        WD[NGB.Watchdog<br/>Health / operability host]
        MIG[NGB.Migrator<br/>Schema deployment host]
    end

    subgraph SURFACE[Contracts, Metadata, and Platform Surface]
        CONTRACTS[NGB.Contracts<br/>Public DTOs and API contracts]
        APPABS[NGB.Application.Abstractions<br/>Application-facing interfaces]
        DEFINITIONS[NGB.Definitions<br/>Catalogs, documents, reports, behaviors]
        METADATA[NGB.Metadata<br/>Metadata model and descriptors]
        CORE[NGB.Core<br/>Common primitives and shared foundation]
    end

    subgraph EXECUTION[Execution Core]
        RUNTIME[NGB.Runtime<br/>Orchestration of catalogs, documents,<br/>posting, reporting, validation, and workflow]
    end

    subgraph ENGINES[Business Engines]
        ACCOUNTING[NGB.Accounting<br/>Ledger and posting semantics]
        OR[NGB.OperationalRegisters<br/>Operational register engine]
        RR[NGB.ReferenceRegisters<br/>Reference register engine]
        AUDIT[Business AuditLog<br/>Append-only audit trail]
    end

    subgraph INFRA[Persistence and Integration]
        PG[NGB.PostgreSql<br/>Persistence, readers, writers, migrations support]
    end

    subgraph EXTERNAL[External Systems]
        DB[(PostgreSQL)]
        KC[Keycloak]
    end

    API --> CONTRACTS
    API --> APPABS
    BG --> APPABS
    WD --> APPABS
    MIG --> PG

    CONTRACTS --> RUNTIME
    APPABS --> RUNTIME
    DEFINITIONS --> RUNTIME
    METADATA --> RUNTIME
    CORE --> RUNTIME

    RUNTIME --> ACCOUNTING
    RUNTIME --> OR
    RUNTIME --> RR
    RUNTIME --> AUDIT
    RUNTIME --> PG

    ACCOUNTING --> PG
    OR --> PG
    RR --> PG
    AUDIT --> PG

    API -. authentication .-> KC
    BG -. authentication .-> KC
    WD -. authentication .-> KC

    PG --> DB

    class API,BG,WD,MIG host;
    class CONTRACTS,APPABS,DEFINITIONS,METADATA,CORE surface;
    class RUNTIME runtime;
    class ACCOUNTING,OR,RR,AUDIT engine;
    class PG infra;
    class DB,KC external;`
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
`NGB.Api` depends on ASP.NET Core concerns plus contracts/application abstractions/runtime.

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
