# Background jobs and register projections

Target: NGB 3.4.0. Read the public
[BackgroundJobs](contracts/NGB.Platform.BackgroundJobs.json),
[PostgreSQL adapter](contracts/NGB.Platform.BackgroundJobs.PostgreSql.json) and
[Runtime](contracts/NGB.Platform.Runtime.json) contracts.

## Host and ownership

Compose `AddNgbBackgroundJobs(PostgresHangfireJobStorageFactory.Create)`, the
runtime, PostgreSQL provider and `AddNgbPostgresBackgroundJobsAdapter`. Share the
application's definitions and runtime/persistence registrations with its API.
Use `UseNgbBackgroundJobs` and `MapNgbBackgroundJobs` for the protected Hangfire
dashboard, account endpoints and health checks. The example uses two workers.
Size worker concurrency for the application's database and workload.

Supply `ConnectionStrings:DefaultConnection`, optional separate
`ConnectionStrings:Hangfire` and `KeycloakSettings` through deployment configuration.
Absent a separate Hangfire connection, the SDK uses the application database with
its Hangfire schema. Never point a test worker at a deployment database. Apply
application migrations and explicit register/account Setup before starting workers;
the worker is not an alternative application migration runner. Hangfire initializes
its own storage schema through the selected provider.

Register the worker's HTTPS `/signin-oidc` callback with its Keycloak client.
The admin console uses cookie/OpenID Connect authentication; its callback is
distinct from the Vue application's redirect URL. Keep the public origin and
trusted reverse-proxy configuration consistent with that registered callback.

## Catalog and scheduling

`IPlatformBackgroundJob` supplies `JobId` and `RunAsync(CancellationToken)`.
Implement custom work in the runtime/module layer, register the implementation
in DI and contribute the ID with `IBackgroundJobCatalogContributor`. Catalog IDs
must be unique across contributors. `IJobScheduleProvider` chooses schedules;
`AddNgbBackgroundJobs` binds the `BackgroundJobs` configuration section through
the SDK's configuration provider.

`BackgroundJobs.Enabled=false` removes recurring registrations owned by that host's
catalog at startup. Per-job `Enabled`, `Cron` and `TimeZoneId` override the global
settings. No schedule means the job is unscheduled. `NightlyCron` can schedule
other catalog jobs, so the equipment example omits that broad default and enables
only `opreg.finalization.run_dirty_months`, every five minutes in UTC. Keep every
replica sharing storage on the same schedule configuration. Disabling recurring
registration does not cancel work already queued or running.

`IBackgroundJobsHealthReporter.GetReportAsync` compares desired schedules to
actual storage, reporting mismatches, errors, last state and next execution.
Registration and a healthy process alone do not prove the business result.

## Execution, failure and retry

`PlatformHangfireJobRunner` resolves a fresh scope per run and acquires a
distributed lock keyed by JobId. A concurrent invocation that cannot acquire it
reports `SkippedOverlap`. This is not a durable deduplication key for a business
effect, nor a global ordering guarantee across different job IDs.

The standard PostgreSQL adapter also registers `IBackgroundJobExecutionLockProvider`.
It holds a separate database execution lock for the whole run, even after the
Hangfire lease expires, using the scheduler storage's actual connection and
namespace. `DistributedLockTimeoutSeconds` is an acquisition wait, not a maximum
job duration. Do not replace the standard adapter with a bare storage registration
and assume that this protection remains configured.

Detected execution-lock loss cancels the token passed to `RunAsync`. Jobs must
honor it and preserve transaction/business idempotency; a lock-loss signal is
cooperative cancellation, not fencing of a job that ignores cancellation. A custom
provider must supply equivalent ownership semantics. Test contention after lease
expiry and release on cancellation/completion, not just two immediate invocations.

The runner has `AutomaticRetry(Attempts = 0)`. Failures propagate into Hangfire's
Failed state and invoke the optional `IPlatformJobNotifier`; cancellation is
propagated separately. A configured next recurrence is a new invocation. An
explicit operator retry must be safe against completed and partially completed
work. Use stable business replay keys and the platform transaction boundaries;
never promise exactly-once delivery from the scheduler alone.

Use `IJobRunMetrics` for bounded-work counters. The notifier receives problem
results; the default implementation sends nothing. An application may supply a
notification adapter when requested. Do not expose raw exceptions or deployment
credentials to ordinary users or send messages from an example test.

## Operational-register finalization

The existing `OperationalRegistersFinalizeDirtyMonthsJob` processes at most fifty
dirty register-months through `IOperationalRegisterAdminMaintenanceService`.
Finalization uses the platform's register/month locks and rebuilds projections
from movements. Posted effects can therefore precede their refreshed monthly
balance/turnover projection; expose that timing honestly when the UI needs it.

The [equipment worker](../examples/equipment-inspection/README.md#background-worker)
uses this job. Its acceptance triggers the stored recurring definition through
Hangfire, waits for the queued execution and compares real balances/turnovers
to 6.25 hours, then zero after Unpost. It also repeats execution, restarts the
worker on the same disposable storage and disables its recurring schedule.
Separate test-only jobs exercise failure/requeue and cancellation/overlap. These
probes do not introduce a production business operation or certify arbitrary
external integrations.

## Built-in maintenance jobs

Use explicit schedules for each maintenance operation. `platform.schema.validate`
checks Documents, Accounting, OR, RR and application catalog/document metadata
against the database. It diagnoses drift; it does not apply missing application
migrations. `opreg.ensure_schema` and `refreg.ensure_schema` invoke the respective
register maintenance services and require every configured register to be healthy
after ensure. These operations can issue DDL and belong to trusted maintenance
composition, not an arbitrary interactive endpoint.

`audit.health` checks append-only triggers, orphan changes and audit volume/freshness.
`accounting.integrity.scan` verifies the current and previous UTC months;
`accounting.aggregates.drift_check` compares stored turnovers against ledger
aggregation for the same window. The jobs fail when their diagnostics detect an
invariant violation; they do not repair accounting records. A healthy empty ledger
is a useful startup check, not evidence for a production workload or historical
reconciliation across every period.

`accounting.operations.stuck_monitor` reads one page of at most 25 stale operations,
using a 10-minute stale threshold and a 30-day lookback. It logs warnings and
counters without taking over or mutating operations. An absence of warnings in
that window does not establish that the complete posting history is healthy.
Keep operational diagnostics server-side; `IPostingStateReader`, integrity
checkers and audit-health readers are trusted provider ports, not authorized UI
services.

`BuiltInMaintenanceJobsTests` executes those seven jobs twice against the migrated
independent application's real PostgreSQL provider. It checks initialized schema,
audit guards, empty-ledger integrity/drift and bounded monitor metrics. The
existing worker scenario separately verifies scheduling, overlap and failure
handling. The test does not schedule every maintenance job or certify automatic
General Journal Entry reversal.

The default catalog, configuration schedule provider, startup synchronizer and
PostgreSQL recurring-hash reader are standard implementations of these public
workflows. Extend the documented contributor/provider interfaces when required.
The dashboard authentication filter alone is insufficient as an administrator
policy; keep `MapNgbBackgroundJobs` and its admin-console endpoint authorization.

