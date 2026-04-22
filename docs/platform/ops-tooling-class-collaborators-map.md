---
title: "Ops and Tooling Class Collaborators Map"
description: "A collaborator-oriented map of the verified NGB operational tooling anchors."
---

# Ops and Tooling Class Collaborators Map

## Purpose

This page complements the dense source map by focusing on **responsibility boundaries** and **collaboration edges** between the verified operational tooling anchors.

## Verified anchors used here

- `NGB.PropertyManagement.Migrator/Program.cs`
- `NGB.Migrator.Core/PlatformMigratorCli.cs`
- `docker/pm/migrator/seed-and-migrate.sh`
- `NGB.PropertyManagement.BackgroundJobs/Program.cs`
- `NGB.PropertyManagement.Watchdog/Program.cs`
- `NGB.PropertyManagement.Api/Program.cs`

## Collaborator map

### 1. `NGB.PropertyManagement.Migrator/Program.cs`

**Role**
- vertical migrator entry point

**Collaborates with**
- `PropertyManagementDatabaseBootstrapper` assembly loading
- `PropertyManagementSeedDefaultsCli`
- `PropertyManagementSeedDemoCli`
- `PlatformMigratorCli`

**Architectural meaning**
- vertical bootstrap and vertical seed commands live here;
- generic migration orchestration does not.

### 2. `NGB.Migrator.Core/PlatformMigratorCli.cs`

**Role**
- shared migration orchestrator

**Collaborates with**
- migration assembly discovery
- schema pack discovery
- schema migrator execution
- schema migration lock behavior
- CLI/environment-derived execution options

**Architectural meaning**
- this is the platform-level migration brain;
- vertical migrators should delegate here instead of reinventing migration flow.

### 3. `docker/pm/migrator/seed-and-migrate.sh`

**Role**
- container automation wrapper for the PM migrator

**Collaborates with**
- PM application connection settings
- `NGB.PropertyManagement.Migrator.dll`
- `PlatformMigratorCli` command surface through CLI arguments
- PM seed-defaults and seed-demo flows

**Architectural meaning**
- operational container bootstrap is explicit and scriptable;
- schema and seed responsibilities are kept ordered.

### 4. `NGB.PropertyManagement.BackgroundJobs/Program.cs`

**Role**
- background jobs host composition root

**Collaborates with**
- `AddNgbBackgroundJobs(...)`
- `EnsureInfrastructureAsync()`
- `AddNgbRuntime()`
- `AddNgbPostgres(...)`
- PM module registrations
- `UseNgbBackgroundJobs()`
- `MapNgbBackgroundJobs()`

**Architectural meaning**
- jobs host is a first-class application host, not a helper utility.

### 5. `NGB.PropertyManagement.Watchdog/Program.cs`

**Role**
- watchdog host composition root

**Collaborates with**
- `AddNgbWatchdog(...)`
- `UseNgbWatchdog()`
- `MapNgbWatchdog()`

**Architectural meaning**
- watchdog remains a dedicated operational surface.

### 6. `NGB.PropertyManagement.Api/Program.cs`

**Role**
- comparison point for full API host composition

**Collaborates with**
- infrastructure setup
- health checks
- runtime registration
- PostgreSQL provider registration
- PM module registration
- controller/auth/error-handling/reporting HTTP concerns

**Architectural meaning**
- confirms the shared composition pattern across operational hosts.

## Simplified collaboration diagram

<script setup>
const flowchart = String.raw`flowchart TB
    Seed["seed-and-migrate.sh"] --> Migrator["PM Migrator Program.cs"]
    Migrator --> Cli["PlatformMigratorCli.cs"]

    Api["PM API Program.cs"] --> Runtime["AddNgbRuntime()"]
    Api --> Postgres["AddNgbPostgres(...)"]
    Api --> Modules["PM module registrations"]

    Jobs["PM Background Jobs Program.cs"] --> Runtime
    Jobs --> Postgres
    Jobs --> Modules
    Jobs --> JobsSurface["Add/Use/MapNgbBackgroundJobs()"]

    Watchdog["PM Watchdog Program.cs"] --> WatchdogSurface["Add/Use/MapNgbWatchdog()"]`
</script>

<MermaidDiagram :chart="flowchart" />

## Key takeaway

The verified anchors show a consistent NGB operational pattern:

- **vertical entry points are thin**;
- **platform orchestration is centralized**;
- **surface-specific operational hosts stay focused**;
- **runtime + provider + vertical module composition is reusable across hosts**.

## Related pages

- [Ops and Tooling Dense Source Map](/platform/ops-tooling-dense-source-map)
- [Ops and Tooling Verified Anchors](/reference/ops-tooling-verified-anchors)
- [Host Composition and DI Map](/architecture/host-composition-and-di-map)
