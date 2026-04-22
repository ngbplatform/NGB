---
title: "Ops Hosts and Bootstraps Source Map"
description: "Verified source map for migrator, seeding, background jobs, and watchdog host bootstraps in NGB Platform."
---

# Ops Hosts and Bootstraps Source Map

## Verified anchors covered here

- `NGB.PropertyManagement.Migrator/Program.cs`
- `NGB.Migrator.Core/PlatformMigratorCli.cs`
- `docker/pm/migrator/seed-and-migrate.sh`
- `NGB.PropertyManagement.BackgroundJobs/Program.cs`
- `NGB.PropertyManagement.Watchdog/Program.cs`

## Migrator host

### `NGB.PropertyManagement.Migrator/Program.cs`

This file is the verified entry point for the Property Management migrator host.

It confirms three important things:

- the vertical migrator force-loads the PM PostgreSQL module assembly so migration pack discovery is deterministic;
- the host supports **specialized seed commands** such as `seed-defaults` and `seed-demo`;
- ordinary migration execution is delegated to the shared `PlatformMigratorCli`.

That means the vertical migrator is intentionally thin. The shared migrator logic lives in platform code, while the vertical host adds pack discovery and vertical seed commands.

## Shared migrator runner

### `NGB.Migrator.Core/PlatformMigratorCli.cs`

This file is the strongest verified platform anchor for schema migration orchestration.

It confirms support for:

- dry run and info/plan modes;
- module selection;
- repair mode;
- pack discovery across loaded assemblies;
- schema lock mode and wait behavior;
- k8s-oriented execution options;
- embedded migration script inspection;
- structured exit codes.

This is more than a thin wrapper around Evolve. It is a platform-grade migration runner with deterministic pack discovery and operational controls.

## Seed-and-migrate script

### `docker/pm/migrator/seed-and-migrate.sh`

This file confirms the Docker Compose bootstrap order used by the PM demo environment.

The script does the following in sequence:

1. run migrator with `--modules pm --repair`;
2. run `seed-defaults`;
3. optionally run `seed-demo` if demo seeding is enabled.

This is valuable because it shows the practical distinction between:

- schema migration;
- platform/vertical default data bootstrap;
- optional demo dataset seeding.

That separation is part of the platform’s operational maturity.

## Background jobs host

### `NGB.PropertyManagement.BackgroundJobs/Program.cs`

This file confirms that the background-jobs host is a platform composition root rather than a standalone background-job engine.

The verified flow is:

1. create builder;
2. bootstrap NGB background jobs hosting;
3. ensure infrastructure;
4. compose runtime + PostgreSQL + vertical modules + vertical background jobs;
5. use and map background jobs pipeline;
6. run.

This tells us that scheduled work is expected to run on the same runtime foundation as the API host, not on a separate alternate execution model.

## Watchdog host

### `NGB.PropertyManagement.Watchdog/Program.cs`

This file confirms the minimal role of Watchdog.

The verified flow is:

1. create builder;
2. add NGB Watchdog services;
3. build app;
4. use Watchdog;
5. map Watchdog endpoints;
6. run.

That reinforces the idea that Watchdog is intentionally operational and lightweight.

## What these verified files tell us together

Taken together, these files show an important NGB pattern:

- **business execution** lives in runtime and provider layers;
- **operational surfaces** are exposed through thin hosts;
- **vertical applications** compose the same core into different executable surfaces;
- **migrator, background jobs, and watchdog** are first-class deployment surfaces, not ad hoc utilities.

## Practical developer takeaway

When you add a new vertical, you should expect to think in terms of multiple hosts:

- API host;
- migrator host;
- background-jobs host;
- watchdog host.

Each host is thin, but each one is part of the production shape of the solution.

## Related pages

- [Migrator Deep Dive](/platform/migrator-deep-dive)
- [Background Jobs Deep Dive](/platform/background-jobs-deep-dive)
- [Watchdog Deep Dive](/platform/watchdog-deep-dive)
- [Source-Anchored Class Maps](/platform/source-anchored-class-maps)
