---
title: "Ops and Tooling Subsystem Dense Source Map"
description: "Verified source-anchored chapter for the NGB operational tooling layer: migrator, background jobs, watchdog, and host bootstrap flow."
---

# Ops and Tooling Subsystem Dense Source Map

## What this chapter covers

This page documents the **operational tooling layer** around the reusable NGB platform core:

- schema migration entry points;
- vertical migrator bootstrap flow;
- seed-and-migrate container flow;
- background jobs host composition;
- watchdog host composition;
- how a vertical solution wires runtime + PostgreSQL + operational hosts together.

This is not a full host-by-host code listing. It is a **dense source map** that explains what each verified file contributes to the overall operational bootstrap model.

## Verified source anchors

### Migrator

- `NGB.PropertyManagement.Migrator/Program.cs`
- `NGB.Migrator.Core/PlatformMigratorCli.cs`
- `docker/pm/migrator/seed-and-migrate.sh`

### Operational hosts

- `NGB.PropertyManagement.BackgroundJobs/Program.cs`
- `NGB.PropertyManagement.Watchdog/Program.cs`

### Vertical composition root used for comparison

- `NGB.PropertyManagement.Api/Program.cs`

## Subsystem role in NGB

At a platform level, NGB does not rely on a single monolithic executable. Instead, a vertical solution composes several focused hosts:

- **API host** for HTTP surface;
- **Migrator host** for schema changes and seed workflows;
- **Background Jobs host** for scheduled or operationally triggered work;
- **Watchdog host** for health aggregation and operability UI.

This split keeps production responsibilities explicit and makes deployment topology easier to reason about.

## 1. Migrator entry model

## `NGB.PropertyManagement.Migrator/Program.cs`

This file is the **vertical migrator entry point**. Its job is small but important:

1. force-load the PM PostgreSQL bootstrap assembly so migration pack discovery is deterministic;
2. detect whether the command is:
   - `seed-defaults`
   - `seed-demo`
   - or a general platform migration command;
3. delegate the generic migration workflow to `PlatformMigratorCli`.

### Why this matters

The vertical migrator is intentionally thin. It does not reimplement migration orchestration. Instead, it supplies:

- vertical-specific assembly loading;
- vertical-specific seed commands;
- platform-shared migration execution through `NGB.Migrator.Core`.

That is a strong pattern: **vertical hosts stay thin; platform orchestration stays shared**.

## 2. Shared migration orchestration

## `NGB.Migrator.Core/PlatformMigratorCli.cs`

This file is the real operational core of migration execution.

It is responsible for:

- CLI flag parsing;
- discovering migration packs from loaded assemblies;
- dry-run and info/plan modes;
- module filtering;
- schema-lock behavior;
- optional repair mode;
- k8s-aware execution defaults;
- invoking `SchemaMigrator.MigrateAsync(...)`.

### What it tells us architecturally

This file confirms several important NGB design choices:

#### A. Migration packs are discovered, not hardcoded in one giant switch

The migrator loads assemblies for pack discovery and then asks the platform migration layer to discover packs. This keeps platform and vertical migration packs composable.

#### B. Concurrency is treated as an operational concern

The CLI supports explicit schema-lock strategy:

- wait;
- try;
- skip;

and configurable lock wait timeout. That is a production-oriented design, especially for CI/CD and Kubernetes cron/job scenarios.

#### C. Dry-run and info modes are first-class

The migrator is not just “apply scripts now.” It also supports:

- plan inspection;
- module listing;
- embedded script visibility;
- lock-mode inspection.

That makes it much better suited for safe operational rollout.

## 3. Container bootstrap flow for PM demo/dev environments

## `docker/pm/migrator/seed-and-migrate.sh`

This shell script is the **containerized operational wrapper** around the PM migrator.

It performs the following sequence:

1. build the application connection string from environment variables;
2. run platform migration for module `pm` with `--repair`;
3. run `seed-defaults`;
4. conditionally run `seed-demo` with optional dataset/seed/date-range/scale parameters.

### Why this file is important

This file shows how NGB operationalizes the migrator in practice:

- the **platform migration step** happens first;
- then **baseline application setup**;
- then **optional demo data generation**.

This is a very clean separation of responsibilities:
- schema first,
- reference defaults second,
- scenario/demo data third.

It also confirms that demo seeding is a deliberate layer on top of the platform schema, not part of the schema migration itself.

## 4. Background Jobs host composition

## `NGB.PropertyManagement.BackgroundJobs/Program.cs`

This file is the vertical **Background Jobs host composition root**.

The confirmed flow is:

1. create the web application builder;
2. call `AddNgbBackgroundJobs(...)` to bootstrap host-level background-jobs infrastructure;
3. await `EnsureInfrastructureAsync()`;
4. register:
   - NGB runtime,
   - NGB PostgreSQL provider,
   - Property Management module,
   - Property Management runtime module,
   - Property Management PostgreSQL module,
   - Property Management background jobs module;
5. build the app;
6. call:
   - `UseNgbBackgroundJobs()`
   - `MapNgbBackgroundJobs()`

### Architectural reading

This tells us that the background-jobs host is not just “Hangfire dashboard plus jobs.” It is a real vertical composition root that loads:

- the shared runtime;
- persistence provider;
- vertical module definitions;
- vertical job registrations.

So the jobs host participates in the same overall platform model as the API host, but with a different operational surface.

## 5. Watchdog host composition

## `NGB.PropertyManagement.Watchdog/Program.cs`

This file is even thinner than the background jobs host.

The confirmed flow is:

1. create the builder;
2. call `AddNgbWatchdog("NGB: Property Management - Health")`;
3. build the app;
4. call:
   - `UseNgbWatchdog()`
   - `MapNgbWatchdog()`

### Architectural reading

This confirms that watchdog is intentionally isolated as a dedicated operational host.

The watchdog host does not appear to load the entire vertical runtime stack directly in its own `Program.cs`. Instead, it is bootstrapped through a specialized watchdog hosting abstraction.

That is a good sign: health aggregation and health UI concerns stay operationally focused instead of becoming entangled with domain orchestration.

## 6. Comparison point: API host composition

## `NGB.PropertyManagement.Api/Program.cs`

This file is not the main topic of this chapter, but it is a useful comparison anchor.

The API host confirms the normal vertical composition pattern:

- add health checks;
- add infrastructure;
- register runtime + PostgreSQL + vertical modules;
- configure controllers, auth, global error handling, report variant context, and other API concerns.

By comparing it with the background-jobs and watchdog hosts, a clear pattern emerges:

- **shared runtime and provider registrations** are reused across hosts where needed;
- **surface-specific concerns** stay isolated per host.

That is exactly what you want in a modular operational architecture.

## Host composition pattern confirmed by verified anchors

<script setup>
const flowchart = String.raw`flowchart TB
    subgraph MigrationFlow["Migration and seed flow"]
        Wrapper["Container wrapper<br/>docker/pm/migrator/seed-and-migrate.sh"] --> Migrator["Vertical migrator<br/>NGB.PropertyManagement.Migrator/Program.cs"]
        Migrator --> Cli["Shared CLI orchestrator<br/>NGB.Migrator.Core/PlatformMigratorCli.cs"]
        Cli --> Packs["Discovered migration packs and seed commands"]
    end

    subgraph HostComposition["Operational hosts"]
        Api["API host<br/>NGB.PropertyManagement.Api/Program.cs"] --> Shared["Runtime, PostgreSQL provider, and PM modules"]
        Jobs["Background Jobs host<br/>NGB.PropertyManagement.BackgroundJobs/Program.cs"] --> Shared
        Watchdog["Watchdog host<br/>NGB.PropertyManagement.Watchdog/Program.cs"] --> WatchdogSurface["Dedicated watchdog hosting surface"]
    end

    Shared --> Runtime["NGB Runtime"]
    Shared --> Provider["NGB PostgreSQL provider"]
    Shared --> Modules["Property Management modules"]`
</script>

<MermaidDiagram :chart="flowchart" />

## 7. Production interpretation

Based on the verified files, the NGB operational model has these strengths:

### Thin vertical entry points

Each host entry point is small and declarative. That is exactly where complexity should stay low.

### Shared platform bootstrap

Migration orchestration is centralized in `NGB.Migrator.Core`, not duplicated per vertical.

### Operational separation of concerns

API, migrator, background jobs, and watchdog are distinct operational roles.

### Strong environment-driven automation

The shell wrapper and the compose flow indicate a pragmatic, automation-friendly setup for local/dev/demo bootstrapping.

## What is directly verified vs inferred here

### Verified directly

- PM migrator delegates to `PlatformMigratorCli`;
- `PlatformMigratorCli` handles module selection, locking, dry-run/info/repair, and migration execution;
- the PM seed wrapper runs migrate → seed-defaults → optional seed-demo;
- PM background jobs host composes runtime + PostgreSQL + PM modules;
- PM watchdog host uses dedicated watchdog hosting abstractions;
- PM API host composes runtime + PostgreSQL + PM modules and exposes HTTP/API concerns.

### Inferred from composition shape

- that the same platform-level abstractions are intended to be reused by other verticals in the same style;
- that background jobs and watchdog hosting abstractions likely encapsulate more operational setup than is visible from the vertical `Program.cs` alone.

## Related pages

- [Migrator Deep Dive](/platform/migrator-deep-dive)
- [Background Jobs Deep Dive](/platform/background-jobs-deep-dive)
- [Watchdog Deep Dive](/platform/watchdog-deep-dive)
- [HTTP → Runtime → PostgreSQL Execution Map](/architecture/http-runtime-postgres-execution-map)
- [Source Anchored Class Maps](/platform/source-anchored-class-maps)
- [Ops and Tooling Verified Anchors](/reference/ops-tooling-verified-anchors)
