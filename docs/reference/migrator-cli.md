---
title: Migrator CLI
description: Command-line surface and operating model of NGB.Migrator.Core.
---

# Migrator CLI

<div class="doc-badge-row">
  <span class="doc-badge doc-badge--verified">Verified</span>
</div>

## Verified anchors

```text
NGB.Migrator.Core/PlatformMigratorCli.cs
NGB.PropertyManagement.Migrator/Program.cs
docker/pm/migrator/seed-and-migrate.sh
```

## What the CLI supports directly

The verified `PlatformMigratorCli` supports these main concerns:

- connection string input via command line or environment;
- migration planning and dry run;
- listing discovered migration packs;
- schema repair;
- module selection;
- application name and schema-lock tuning;
- Kubernetes-oriented lock behavior.

## Important flags

- `--connection`
- `--modules`
- `--module`
- `--repair`
- `--dry-run`
- `--list-modules`
- `--info`
- `--show-scripts`
- `--application-name`
- `--schema-lock-mode`
- `--schema-lock-wait-seconds`

## Vertical host pattern

A vertical migrator host may extend the base CLI with commands such as:

- `seed-defaults`
- `seed-demo`

That is exactly what the verified Property Management migrator does.

## Related pages

- [Migrator Deep Dive](/platform/migrator-deep-dive)
- [Ops and Tooling Subsystem Dense Source Map](/platform/ops-tooling-dense-source-map)
- [Manual local runbook](/start-here/manual-local-runbook)
