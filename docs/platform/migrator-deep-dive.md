---
title: "Migrator Deep Dive"
description: "How NGB uses dedicated migrator hosts, pack discovery, repair, dry-run, and seed flows."
---

# Migrator Deep Dive

> **Page intent**
> This page explains the migrator as an operational chapter rather than a side utility.

## Trust level

- **Verified anchors:** `NGB.PropertyManagement.Migrator/Program.cs`, `NGB.Migrator.Core/PlatformMigratorCli.cs`, `docker/pm/migrator/seed-and-migrate.sh`
- **Architecture synthesis:** yes
- **Template guidance:** yes

## Chapter navigation

- [Migrator CLI](/reference/migrator-cli)
- [Host Composition and DI Map](/architecture/host-composition-and-di-map)
- [Background Jobs Deep Dive](/platform/background-jobs-deep-dive)
## Verified source anchors

### Vertical migrator host delegates to platform migrator core

Confirmed in:

- `NGB.PropertyManagement.Migrator/Program.cs`

Verified behavior:

- PM migrator force-loads the PostgreSQL module assembly for deterministic pack discovery.
- It recognizes `seed-defaults` and `seed-demo` commands.
- Otherwise it delegates to `PlatformMigratorCli.RunAsync(args)`.

### Shared CLI supports operational-grade migration modes

Confirmed in:

- `NGB.Migrator.Core/PlatformMigratorCli.cs`

Verified behavior includes:

- `--k8s`
- `--repair`
- `--dry-run`
- `--list-modules`
- `--info`
- `--show-scripts`
- module filtering
- schema lock options
- environment-variable based connection string/app-name configuration
- deterministic pack discovery and printed migration plan

### Docker compose bootstrap uses migrate → seed-defaults → optional seed-demo

Confirmed in:

- `docker/pm/migrator/seed-and-migrate.sh`

## Why the migrator matters architecturally

NGB treats database schema deployment as a first-class operational concern.

That is the right stance for a platform with:

- embedded migration resources;
- multiple packs/modules;
- vertical-specific hosts;
- Kubernetes-oriented deployment expectations.

## Recommended operational sequence

1. discover migration packs;
2. plan/inspect;
3. apply with locking;
4. seed defaults required for platform behavior;
5. optionally seed demo data;
6. keep hosts dependent on successful migrator completion.

## Review checklist

- Does the vertical have a dedicated migrator host?
- Are packs discoverable deterministically?
- Are dry-run/info paths usable in CI/CD?
- Is schema lock behavior explicit?
- Are defaults/demo seed flows separated?

## Related pages

- [Background Jobs Deep Dive](/platform/background-jobs-deep-dive)
- [Watchdog Deep Dive](/platform/watchdog-deep-dive)
