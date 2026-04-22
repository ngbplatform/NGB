---
title: "Background Jobs Deep Dive"
description: "How NGB uses dedicated Background Jobs hosts and runtime composition for scheduled platform work."
---

# Background Jobs Deep Dive

> **Page intent**
> This page explains background jobs as a platform host and composition pattern.

## Trust level

- **Verified anchors:** `NGB.PropertyManagement.BackgroundJobs/Program.cs`
- **Architecture synthesis:** yes
- **Template guidance:** yes

## Chapter navigation

- [Host Composition and DI Map](/architecture/host-composition-and-di-map)
- [Watchdog Deep Dive](/platform/watchdog-deep-dive)
- [Migrator Deep Dive](/platform/migrator-deep-dive)
- [Documentation Map](/reference/documentation-map)

## Verified source anchors

Confirmed in:

- `NGB.PropertyManagement.BackgroundJobs/Program.cs`

Verified behavior:

- the vertical creates a dedicated background jobs web host;
- host bootstrap comes from `AddNgbBackgroundJobs(...)`;
- infrastructure is ensured before host composition continues;
- the host composes `AddNgbRuntime()` and `AddNgbPostgres(...)` plus vertical/runtime/postgres/background-jobs modules;
- the app uses and maps NGB background jobs middleware/endpoints.

## What this means architecturally

Background processing in NGB is not just “run Hangfire somewhere”. It is modeled as a dedicated host that composes the same runtime and provider stack as the API host, but with a different execution surface.

That is the correct model for:

- deterministic scheduled work;
- controlled DI/composition;
- host-specific observability;
- job dashboards and operational endpoints.

## Recommended responsibilities

Background jobs host should own:

- scheduled execution;
- job dashboard exposure;
- operational health for job infrastructure;
- bounded, idempotent platform jobs;
- vertical registration of job catalog extensions.

## What should not live there

- user-facing HTTP CRUD controllers;
- direct UI concerns;
- vertical business logic that is only meaningful in synchronous request flow and has no background execution need.

## Related pages

- [Host Composition and DI Map](/architecture/host-composition-and-di-map)
- [Watchdog Deep Dive](/platform/watchdog-deep-dive)
- [Migrator Deep Dive](/platform/migrator-deep-dive)
