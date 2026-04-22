---
title: Migrator
---

# Migrator

The Migrator is the official schema-deployment entry point for NGB environments.

The platform uses a shared migrator core and vertical-specific migrator hosts.

## Why the Migrator exists

A serious business platform needs more than “run SQL somehow.”

The Migrator exists to give NGB a controlled way to:

- discover migration packs;
- order them deterministically;
- run schema changes safely;
- support repair and dry-run / plan modes;
- support Kubernetes-style lock behavior;
- keep vertical migrators thin and platform-consistent.

## Shared core

The shared CLI logic lives in `NGB.Migrator.Core`.

Its shared runner supports arguments such as:

- `--connection`
- `--modules` / repeated `--module`
- `--repair`
- `--dry-run`
- `--list-modules`
- `--info`
- `--show-scripts`
- `--k8s`
- schema lock options
- execution timeouts

That means vertical migrators can reuse the same operational model.

## Vertical migrator hosts

A vertical migrator host typically does two things:

1. ensure the relevant module assemblies are loaded for pack discovery;
2. add vertical-specific commands such as seed flows.

For example, `NGB.PropertyManagement.Migrator` routes:

- normal migration execution to `PlatformMigratorCli`;
- `seed-defaults` to the defaults seed command;
- `seed-demo` to the demo seed command.

## Typical commands

### List discovered migration packs

```bash
dotnet run --project NGB.PropertyManagement.Migrator -- --list-modules
```

### Show migration plan without touching the database

```bash
dotnet run --project NGB.PropertyManagement.Migrator --   --connection "Host=localhost;Port=5433;Database=ngb_pm;Username=ngb_pm_app;Password=..."   --modules pm   --dry-run   --info
```

### Apply migrations

```bash
dotnet run --project NGB.PropertyManagement.Migrator --   --connection "Host=localhost;Port=5433;Database=ngb_pm;Username=ngb_pm_app;Password=..."   --modules pm   --repair
```

### Seed defaults

```bash
dotnet run --project NGB.PropertyManagement.Migrator --   seed-defaults   --connection "Host=localhost;Port=5433;Database=ngb_pm;Username=ngb_pm_app;Password=..."
```

### Seed demo data

```bash
dotnet run --project NGB.PropertyManagement.Migrator --   seed-demo   --connection "Host=localhost;Port=5433;Database=ngb_pm;Username=ngb_pm_app;Password=..."   --dataset demo   --seed 20260412   --from 2024-01-01   --skip-if-dataset-exists true
```

## Kubernetes and schema locks

The shared CLI supports Kubernetes-aware lock behavior.

That matters because in real environments you do not want overlapping migrator jobs to race each other. The migrator therefore supports:

- application name;
- schema lock mode;
- schema lock wait timeout;
- dry-run inspection;
- deterministic pack planning.

## Migration packs

Migration packs are discovered from the entry assembly and loaded references.

This keeps the migrator generic while allowing the actual schema ownership to stay in the right modules.

## Recommended operational rules

- run migrations through the migrator, not by hand;
- keep migrations deterministic and reviewable;
- use dry-run and info modes before risky environment changes;
- make pack boundaries explicit;
- keep seed flows separate from mandatory schema migration logic;
- do not hide schema behavior in random application startup code.

## Why this design is production-ready

The NGB migrator is designed to work in both developer and orchestrated environments:

- local CLI execution;
- Docker Compose bootstrap;
- CI/CD pipelines;
- Kubernetes jobs or CronJobs.

That portability is one of the reasons the migrator is a standalone concern in the platform.
