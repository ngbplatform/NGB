---
title: Host composition
description: How an NGB vertical host composes NGB.Api, NGB.Runtime, NGB.PostgreSql, vertical modules, and operational hosts.
---

# Host composition

<div class="doc-badge-row">
  <span class="doc-badge doc-badge--verified">Verified</span>
  <span class="doc-badge doc-badge--inferred">Inferred</span>
</div>

This page explains how a vertical application composes the reusable platform. It is the practical companion to the host composition execution map.

## Verified anchors

```text
NGB.PropertyManagement.Api/Program.cs
NGB.PropertyManagement.BackgroundJobs/Program.cs
NGB.PropertyManagement.Watchdog/Program.cs
NGB.PropertyManagement.Migrator/Program.cs
NGB.Migrator.Core/PlatformMigratorCli.cs
```

## What host composition means in NGB

NGB keeps the reusable platform in library projects and lets each vertical host compose those projects explicitly.

That gives you three important properties:

- the platform core stays reusable;
- each vertical decides which modules it brings in;
- operational hosts such as API, migrator, background jobs, and watchdog stay small and focused.

## The API host pattern

The verified `NGB.PropertyManagement.Api/Program.cs` file shows the canonical pattern:

1. build a standard ASP.NET Core host;
2. add observability and health checks;
3. validate the application connection string;
4. register `AddNgbRuntime()`;
5. register `AddNgbPostgres(connectionString)`;
6. register vertical modules and runtime/postgres extensions;
7. add controllers, auth, error handling, and app-specific services;
8. build the app and map controllers.

That is the most important composition rule in the platform: **hosts compose, libraries execute**.

## The background jobs host pattern

`NGB.PropertyManagement.BackgroundJobs/Program.cs` shows a second canonical host shape.

It uses the platform background-jobs bootstrap first, ensures infrastructure, then composes the same runtime and PostgreSQL modules as the API host plus a background-jobs module for the vertical.

That means background processing is not a special side system. It runs on the same platform execution core.

## The watchdog host pattern

`NGB.PropertyManagement.Watchdog/Program.cs` shows the health/operability host.

It is intentionally thin:

- add NGB watchdog hosting;
- build the app;
- use watchdog middleware;
- map watchdog endpoints.

This keeps health UI and service status concerns separate from the main business API.

## The migrator host pattern

`NGB.PropertyManagement.Migrator/Program.cs` shows the migrator host pattern:

- force-load vertical PostgreSQL assembly for deterministic migration-pack discovery;
- branch into `seed-defaults` or `seed-demo` commands if requested;
- otherwise delegate to `PlatformMigratorCli`.

That makes the migrator a proper application host, not just a shell script.

## Why this pattern matters

Host composition is one of the strongest NGB architectural choices.

It lets the platform stay modular without turning everything into distributed services. The result is a modular monolith with distinct operational entry points.

## Related pages

- [Host Composition and DI Map](/architecture/host-composition-and-di-map)
- [Ops Hosts and Bootstraps Source Map](/platform/ops-hosts-and-bootstraps-source-map)
- [Ops and Tooling Subsystem Dense Source Map](/platform/ops-tooling-dense-source-map)
- [Manual local runbook](/start-here/manual-local-runbook)
