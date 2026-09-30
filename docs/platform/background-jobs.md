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

In 3.0, `NGB.BackgroundJobs` is provider-neutral. The host selects PostgreSQL storage through
`NGB.BackgroundJobs.PostgreSql` and passes `PostgresHangfireJobStorageFactory.Create` to
`AddNgbBackgroundJobs`. Infrastructure provisioning uses `PostgresDatabaseProvisioner` from
`NGB.PostgreSql`; HTTP error/health integration uses `NGB.PostgreSql.AspNetCore`.
See [Host composition](/start-here/host-composition) for the complete registration order.

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

The PostgreSQL default projector uses an owned `ReadCommitted` transaction and a
separate register finalizer lock (`ORF`). It briefly acquires the ordinary register
and operational-month locks (`ORR`/`ORP`) to capture the maximum committed movement
ID, then rolls back a savepoint to release those locks while retaining `ORF`.
The movement store takes `ORR` before allocating IDs for append and storno. The
ascending, non-cycling sequence must use `CACHE 1`; unsafe settings fail explicitly.
Direct SQL movement writes or sequence resets violate this contract.

The immutable movement prefix is aggregated outside the movement locks. Turnovers
cover the selected month; cumulative balances include all earlier movements, so
backdated writes between months do not depend on an already dirty predecessor.
Projection rows remain uncommitted and invisible to other readers. Publication
queues for the ordinary locks using PostgreSQL blocking advisory locks, applies the
committed movement tail, marks the month Finalized, and commits atomically. Normal
concurrent writes no longer discard the entire calculation. Unchanged projection
rows are preserved to avoid unnecessary WAL. Database-visible waits also let
PostgreSQL detect cycles with external transactions or schema maintenance.

`PostgresOptions.OperationalRegisterPublicationTimeoutSeconds` defaults to 5 seconds
and bounds each boundary capture and each publication lock/catch-up attempt. It is
not a deadline for the full aggregation or transaction commit. Conflicts roll back
the owned month and retry at most three times (100/200 ms backoff). Exhaustion throws
`OperationalRegisterFinalizationBusyException` and fails the job; it must not be
reported as successful `finalized_count=0`. Caller cancellation rolls back without
retry. A write after publication can legitimately mark the month Dirty again.
Monitor job failures, duration, finalized count, and dirty age under sustained load.

Custom projectors, providers without preparation support, external transactions,
and registers without immutable movement metadata retain the serialized behavior.
Update API and background-worker hosts together. No schema migration is needed;
all movement writers must obey the lock/sequence contract before enabling the new
finalizer. The full-history aggregate may increase read/CPU cost; validate it with
the same capacity profile and data volume used for the original incident.

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
