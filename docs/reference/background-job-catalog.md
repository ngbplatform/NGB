---
title: Background job catalog
description: Concrete background-job ids, schedules, and host-level scheduling settings visible in the verified NGB Property Management setup.
---

# Background job catalog

<div class="doc-badge-row">
  <span class="doc-badge doc-badge--verified">Verified</span>
  <span class="doc-badge doc-badge--inferred">Operational interpretation</span>
</div>

## Verified anchors

```text
NGB.PropertyManagement.BackgroundJobs/Program.cs
NGB.PropertyManagement.BackgroundJobs/appsettings.Development.json
NGB.PropertyManagement.BackgroundJobs/Catalog/PropertyManagementBackgroundJobCatalog.cs
NGB.PropertyManagement.BackgroundJobs/Jobs/GenerateMonthlyRentChargesJob.cs
```

## What this page covers

This page documents the concrete background-job ids and scheduler settings that are visible in the verified Property Management host. It is a practical lookup page, not a generic background-job design essay.

## Host-level scheduler settings

The PM background-jobs host exposes a structured `BackgroundJobs` section with the following controls:

| Key | Meaning | PM development value |
|---|---|---|
| `BackgroundJobs:Enabled` | Master switch for job scheduling | `true` |
| `BackgroundJobs:DefaultTimeZoneId` | Default schedule time zone | `UTC` |
| `BackgroundJobs:NightlyCron` | Shared nightly maintenance schedule | `0 3 * * *` |
| `BackgroundJobs:Jobs.<job-id>.Cron` | Per-job cron expression | varies by job |
| `BackgroundJobs:Jobs.<job-id>.Enabled` | Per-job enable switch | `true` for all visible PM jobs |
| `BackgroundJobs:Jobs.<job-id>.TimeZoneId` | Per-job time zone override | `UTC` for all visible PM jobs |

## Visible scheduled jobs in PM development config

| Job id | Default cron | What the name clearly signals |
|---|---|---|
| `accounting.operations.stuck_monitor` | `*/5 * * * *` | Monitoring and recovery visibility for stuck accounting operations |
| `accounting.general_journal_entry.auto_reverse.post_due` | `*/15 * * * *` | Processing due auto-reversals for general journal entries |
| `pm.rent_charge.generate_monthly` | `0 5 * * *` | Monthly rent-charge generation in the PM vertical |

## Job that is explicitly catalogued in PM code

The verified PM module code explicitly catalogues one vertical job id:

| Job id | Verified source |
|---|---|
| `pm.rent_charge.generate_monthly` | `PropertyManagementBackgroundJobCatalog.GenerateMonthlyRentCharges` |

The matching job runner is `GenerateMonthlyRentChargesJob`, which delegates execution to `GenerateMonthlyRentChargesService`.

## Host composition rule

The verified PM background-jobs host composes:

- the shared NGB background-jobs hosting infrastructure;
- the shared runtime and PostgreSQL stack;
- the PM module, PM runtime module, PM PostgreSQL module, and PM background-jobs module.

That means scheduled work is executed on the same business/runtime foundation as the API host instead of using a separate alternate stack.

## Operational expectations for new jobs

The verified configuration layout implies a stable pattern for new jobs:

1. Give the job a stable id.
2. Register it in the vertical or platform job catalog.
3. Add a concrete runner class.
4. Expose schedule, enable flag, and optional time-zone override in `BackgroundJobs:Jobs`.

## Related pages

- [Background Jobs](/platform/background-jobs)
- [Background Jobs Deep Dive](/platform/background-jobs-deep-dive)
- [Configuration reference](/reference/configuration-reference)
