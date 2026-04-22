---
title: "Ops and Tooling Verified Anchors"
description: "Compact evidence index for the verified operational tooling files used in the NGB docs."
---

# Ops and Tooling Verified Anchors

## Purpose

This page is the compact evidence index for the **ops/tooling chapter**.

Use it together with:

- [Ops and Tooling Dense Source Map](/platform/ops-tooling-dense-source-map)
- [Ops and Tooling Class Collaborators Map](/platform/ops-tooling-class-collaborators-map)

## Verified files

### Migrator flow

- `NGB.PropertyManagement.Migrator/Program.cs`
- `NGB.Migrator.Core/PlatformMigratorCli.cs`
- `docker/pm/migrator/seed-and-migrate.sh`

### Operational hosts

- `NGB.PropertyManagement.BackgroundJobs/Program.cs`
- `NGB.PropertyManagement.Watchdog/Program.cs`

### Comparison composition root

- `NGB.PropertyManagement.Api/Program.cs`

## What these anchors are sufficient to support

These verified files are sufficient to support documentation claims about:

- the existence of a thin vertical migrator entry point;
- delegation to a shared platform migration CLI;
- explicit containerized migrate → seed-defaults → optional seed-demo flow;
- a dedicated background jobs host;
- a dedicated watchdog host;
- runtime + PostgreSQL + vertical module composition reused across multiple hosts.

## What these anchors do **not** fully prove on their own

These files alone do **not** fully prove:

- the full internal implementation of the background jobs hosting abstraction;
- the full internal implementation of the watchdog hosting abstraction;
- the complete Hangfire/job registration catalog;
- the complete health-check aggregation internals.

Those claims should stay either:
- tied to other verified files, or
- clearly marked as inferred.

## Recommended usage rule

When writing about ops/tooling in NGB docs:

- use these anchors for **host role**, **bootstrap sequence**, and **composition shape**;
- avoid overstating internal implementation details unless more files are verified.

## Related pages

- [Ops and Tooling Dense Source Map](/platform/ops-tooling-dense-source-map)
- [Ops and Tooling Class Collaborators Map](/platform/ops-tooling-class-collaborators-map)
