# Controlled posting/finalization and report admission experiments

Run from `performance-tests` on the local PM stack. The runner uses the existing
`.env.write.local` workload configuration and `../.env.pm` database configuration.
It requires Docker, k6, Node 22.13+, Python 3 and the .NET 10 SDK. The first build
restores pinned NuGet packages for a small diagnostic helper.

## Main experiment

```sh
caffeinate -i npm run pm:contention:diagnostics
```

Default schedule: 5 minutes ramp to **320 VUs**, 20 minutes at 320, 5 minutes ramp
down, plus setup, graceful completion and offline analysis. At minute 10 of the
actual k6 scenario, the runner triggers the registered
`opreg.finalization.run_dirty_months` Hangfire job once. Trigger timing has the
resource sampler's polling resolution (normally several seconds). The existing
background worker executes the job with its normal bounded batch of 50 months.

The workload is the same mixed flow used by `pm:capacity:diagnostics`, including
fresh rent-charge posting, maintenance writes and the shared accounting reports.
It uses the configured report dates/fixtures; it does not silently replace them
with new PM reports. It creates real documents and leaves them in the test database.
Period closing is disabled. Database snapshots are not restored automatically.

Optional preflight, without k6 load or a finalization trigger:

```sh
npm run pm:contention:diagnostics -- --check
```

Preflight checks PostgreSQL through MCP, a live Hangfire worker, the deployed job
type, clock alignment and live report admission gauges. The helper is copied into
a unique temporary directory in each container, then removed. Application images,
authentication, job registration and report limits are unchanged. No metrics HTTP
endpoint is added. Do not run another diagnostic session against the same API at
the same time. An API that has never executed a report may not have instantiated
its admission budget yet; open one existing report before preflight in that case.

## Control and report-pressure runs

Run the same 320-VU profile without the deliberate job trigger:

```sh
NGB_CONTENTION_MODE=control caffeinate -i npm run pm:contention:diagnostics
```

For the report rejection bursts observed at 800 VUs:

```sh
NGB_CONTENTION_MODE=reports NGB_CAPACITY_VUS=800 \
caffeinate -i npm run pm:contention:diagnostics
```

Each command runs a separate 30-minute workload. `reports` retains the mixed
workload and collects admission metrics; it does not trigger finalization. For a
controlled before/after comparison, restore the same database snapshot before
each run and supply `--dataset-id <snapshot-id>`. Consecutive runs against growing
data are observational comparisons. Avoid the scheduled finalization window: an
independent finalization run invalidates control and report-pressure experiments.

Overrides are `NGB_CAPACITY_VUS` (one target), `NGB_CAPACITY_RAMP_DURATION`,
`NGB_CAPACITY_HOLD_DURATION`, `NGB_CAPACITY_RAMP_DOWN_DURATION`, and
`NGB_CONTENTION_TRIGGER_AFTER`. The trigger must fall strictly inside the hold.

## Evidence and acceptance

Each run prints its unique `artifacts/runs/platform-contention-...` directory:

- `summary.json`, `summary.manifest.json`, `samples.json.gz`: original k6 evidence.
- `resources.jsonl`: PostgreSQL counters/locks through a fixed read-only MCP tool,
  API/database/worker container resources and load-generator resource samples.
- `contention.jsonl`: exact enqueue ID, sampled Hangfire state, register month
  statuses and filtered worker start/completion events. Raw job arguments and
  exception messages are not exported.
- `report-admission.jsonl`: `NGB.Reporting` counters, wait histograms and active/
  queued gauges, collected once per second using .NET EventPipe.
- `report-admission-timeline.jsonl`: report HTTP statuses, latency and report IDs
  alongside admission metrics by second. Histogram quantiles are per interval;
  do not average them to obtain a whole-run percentile.
- `contention-analysis.json`: acceptance and before/during/after latency windows.

The runner fails on missing diagnostics, an empty or failed finalization, another
finalization trigger, an unfinished job at the end of the hold, missing successful
fresh posting before/during/after the job, any posting HTTP error, or posting p95
at/above 2500 ms in a measured window. k6 thresholds continue to apply, including
report error rate below 1%. A 429 burst is recorded even when the aggregate gate
passes. No 429 is automatically required: absence of rejections is a valid result.

A job shorter than the overlap clock guard, or without posting within the guarded
interval, yields insufficient evidence rather than a green result. The test shows
temporal coexistence of real posting and real finalization; it does not prove all
ledger/projection invariants or guarantee throughput on another dataset/machine.
Use the existing deterministic integration tests for those correctness properties.

Ctrl-C stops k6 and metric collection and prevents a trigger that has not yet
started. A job already enqueued belongs to Hangfire and can finish normally; the
runner does not cancel or delete it. An ambiguous enqueue is never retried.

## Verification

```sh
npm run typecheck
npm run test:tooling
dotnet build scripts/diagnostics/control/Ngb.Perf.Control.csproj -c Release
```

The tooling tests cover configuration, enqueue timing, empty work, duplicate and
ambiguous triggers, cancellation, MCP argument validation, missing metrics,
window latency gates, foreign runs and synthetic evidence with known counts.
The EventPipe implementation follows the runtime's
[MetricsEventSource protocol](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Diagnostics.DiagnosticSource/src/System/Diagnostics/Metrics/MetricsEventSource.cs).
