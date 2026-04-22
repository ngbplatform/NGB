---
title: Background Jobs
---

# Background Jobs

Background Jobs are the platform’s scheduled execution surface.

NGB uses **Hangfire** as the scheduler and keeps scheduling concerns in a dedicated host so scheduled work remains explicit, observable, and operable.

## Why Background Jobs have their own host

A dedicated background-job host keeps scheduling concerns separate from API concerns.

That gives cleaner control over:

- recurring schedules;
- worker process scaling;
- job dashboards;
- retries and visibility;
- operational isolation.

## Platform design

The core design decisions are:

- Hangfire is the scheduler;
- the host is dedicated to background execution;
- schedules come from configuration through a provider;
- jobs should be bounded and safe to rerun;
- important jobs should use business-key locking when needed.

## Platform job catalog

The fixed platform job catalog includes recurring jobs such as:

- `platform.schema.validate`
- `accounting.integrity.scan`
- `audit.health`
- `opreg.finalization.run_dirty_months`
- `opreg.ensure_schema`
- `refreg.ensure_schema`
- `accounting.aggregates.drift_check`

An additional optional frequent monitor may be present for stuck accounting operations.

## Design rules for jobs

A platform or vertical background job should be:

- **bounded** — one run should have a predictable work ceiling;
- **idempotent** — safe to rerun;
- **observable** — emit logs and counters;
- **lock-aware** — serialize by the right business key when needed;
- **no-op friendly** — do nothing safely when there is nothing to process.

## Examples of why this matters

### Dirty-month finalization

Operational register finalization can easily overlap or be retried. A good job design makes that safe through:

- deterministic target selection;
- register/month locking;
- bounded processing per run;
- safe reruns.

### Integrity scans

Accounting and audit integrity scans should be able to run repeatedly and produce diagnostics without corrupting business state.

## Hangfire dashboard

The background-jobs host typically exposes the Hangfire dashboard so operators can inspect:

- recurring jobs;
- recent job runs;
- failures;
- retries;
- execution history.

In the PM local environment, the dashboard is exposed at:

```text
https://localhost:7074/hangfire
```

## Relationship to Runtime

Jobs do not replace the platform runtime. They usually call into Runtime or related platform services to perform the same business-safe operations a foreground path would use.

That is important because scheduled work should not bypass the platform’s rules.

## When to create a background job

Create a background job when work is:

- recurring;
- asynchronous;
- expensive enough not to do in the request path;
- or operational in nature.

Good candidates include:

- integrity scans;
- register finalization;
- drift checks;
- scheduled generation or maintenance flows.

## When not to create a background job

Do not create a background job just to hide slow synchronous design.

If a foreground action must be immediately visible to the user and should complete as one business transaction, it usually belongs in the request path.

## Operational checklist for a new job

Before adding a new job, answer:

1. what is the business key for safe locking?
2. what is the work bound per run?
3. what happens on retry?
4. what should the logs and counters show?
5. how will operators inspect the job in Hangfire?

If those answers are not clear, the job is not ready.
