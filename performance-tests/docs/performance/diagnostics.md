# Diagnostic runs

## Prerequisites

Use macOS or Linux, Node.js 22.13 or newer, Python 3.10 or newer for offline analysis,
Docker, k6, and PostgreSQL 17 or newer. Install the locked project dependencies with
`npm ci`. The PostgreSQL version requirement comes from `pg_stat_checkpointer`.
Ordinary Windows workloads remain available through `scripts/run-k6.ps1`; the diagnostic
runner uses POSIX process groups and `ps` and does not support Windows.

Use the existing dedicated local PM environment. API and PostgreSQL containers must
be running and, when a Docker health check is configured, healthy. No k6 process may already be running. The preflight checks
these conditions and read-only sampling before it permits load generation.

## Commands

Run from `performance-tests`:

```bash
# Read-only preflight: no API workload and no k6 test execution.
npm run pm:capacity:diagnostics -- --check

# macOS: prevent idle sleep throughout the benchmark.
caffeinate -i npm run pm:capacity:diagnostics

# Linux, or a host kept awake by another mechanism.
npm run pm:capacity:diagnostics

# Existing write-heavy workload with the same diagnostic collection.
npm run pm:write-heavy:diagnostics

# Other supported diagnostic profiles.
npm run pm:diagnostics -- --profile platform-read-capacity
npm run pm:diagnostics -- --profile platform-breakpoint
```

The wrapper enables **writes and fresh rent-charge posting** for mixed capacity and
write-heavy, and disables them for read capacity and breakpoint. Period close is
disabled in all four. Other workload settings come from the selected env file; the
wrapper does not modify scenario code or thresholds. Ordinary `pm:platform-mixed-capacity`
uses `.env.local` and may have writes disabled: it is not necessarily the same workload.

Mixed capacity uses 80, 160, 240 and 320 concurrent virtual users by default. Each level
has a 5-minute ramp and a 10-minute hold, followed by a final 5-minute ramp down: 65
minutes plus setup and graceful completion. Passing the highest stage establishes a
tested capacity point, not the maximum capacity. Write-heavy uses the duration/rates
already configured in `.env.write.local`; the accepted local profile is 30 minutes.

To change the capacity schedule explicitly:

```bash
NGB_CAPACITY_VUS=80,160,240,320 \
NGB_CAPACITY_RAMP_DURATION=5m \
NGB_CAPACITY_HOLD_DURATION=10m \
NGB_CAPACITY_RAMP_DOWN_DURATION=5m \
npm run pm:capacity:diagnostics
```

## Configuration

Paths passed to the runner are relative to the `performance-tests` workspace, even if
`node /path/to/scripts/diagnostics/run.mjs` is invoked from another directory.

| Setting | Default / behavior |
| --- | --- |
| `--env-file` | PM `.env.write.local` for mixed capacity/write-heavy; `.env.local` otherwise |
| `--database-env-file` | `../.env.pm`; an existing local file, never sourced as shell code |
| `--dataset-id` | An operator-supplied restored snapshot identity; unknown/unrestored otherwise |
| `NGB_DIAGNOSTICS_API_CONTAINER` | `ngb.pm.api` |
| `NGB_DIAGNOSTICS_DB_CONTAINER` | `ngb.pm.postgres` |
| `PGHOST`, `PGPORT`, `PGDATABASE`, `PGUSER`, `PGPASSWORD`, `PGSSLMODE` | Standard PostgreSQL connection settings, supplied by the environment or database env file |
| Existing compose env fallback | `POSTGRES_HOST_PORT`, `PM_DB_NAME`, `POSTGRES_ADMIN_USER`, `POSTGRES_ADMIN_PASSWORD` |

Process environment values override file values. Write/posting/period-close flags and
fresh posting mode are fixed by the selected diagnostic profile. Passwords are passed
only through the MCP child environment, never as command-line arguments. No credentials
are stored in tracked defaults. See the [database env template](../../scripts/diagnostics/.env.example).
The default connection is local without TLS; set `PGSSLMODE` appropriately for a remote
TLS-enabled test database. A restricted monitoring account with the necessary statistics
visibility is preferable to an administrator; account provisioning is outside this tool.

The bundled Postgres MCP server exposes only `sample_resources`, with no arguments and
no arbitrary SQL tool. It executes the tracked SELECT inside `BEGIN READ ONLY`, always
rolls back, and sets statement, lock and connection timeouts. A persistent MCP connection
avoids repeatedly loading an SDK from an npm cache. The SDK and driver are exact-version
project dependencies captured in `package-lock.json`.

## Output and interruption

Every invocation creates `artifacts/runs/<profile>-<UTC>-<unique-id>/`. The output path is
printed before the workload starts; concurrent invocations do not overwrite each other.

| File | Purpose |
| --- | --- |
| `run.json` | Lifecycle state, timestamps, process exit code, signal, diagnostic error count |
| `environment.json` | API/DB image identities, selected container resource limits, dataset identity, sample cadence |
| `summary.json`, `summary.md` | k6 aggregate metrics and workload configuration |
| `summary.manifest.json` | Revision, tracked diff hash, test hash, tool versions, non-secret workload settings |
| `samples.json.gz` | Timestamped k6 metric points for per-minute and per-stage analysis |
| `resources.jsonl` | Postgres MCP counters/locks, Docker CPU/memory, k6 CPU/RSS |
| `run.log` | Workload stdout/stderr |

Preflight-only runs contain diagnostic output and run state but no k6 summary or samples.
Files are private to the local user by default. Resource sampling is approximately every
5 seconds; Docker stats are sampled every third cycle. These are observations, not an
exhaustive trace: short waits may be missed. Database transactions are capped at 200 per
sample; query text is classified rather than exported. Docker 100% CPU is approximately
one core. Cluster-wide WAL/checkpoint counters may include other databases.

Ctrl+C or SIGTERM is forwarded to the workload process group; the wrapper waits for
cleanup, with a bounded forced-stop fallback. An interrupted run is not a valid baseline.
The k6 exit code is preserved for ordinary failures. A successful k6 run with incomplete
summary/manifest/time-series output or diagnostic gaps exits 2 and is marked failed for diagnostic completeness;
`run.json` retains the separate k6 exit code. A forced kill or host crash can leave saved
state at `running`; check sample freshness instead of assuming the process is alive.

```bash
python3 -B scripts/analysis/status.py artifacts/runs/<run-directory>
```

## Offline analysis and comparison

Run after the benchmark ends, to avoid competing with the load generator:

```bash
python3 -B scripts/analysis/analyze.py \
  --summary artifacts/runs/<run-directory>/summary.json \
  --samples artifacts/runs/<run-directory>/samples.json.gz \
  --resources artifacts/runs/<run-directory>/resources.jsonl \
  --output-dir artifacts/runs/<run-directory>/analysis

python3 -B scripts/analysis/compare.py \
  artifacts/runs/<old-run>/summary.json \
  artifacts/runs/<new-run>/summary.json \
  --output-dir artifacts/comparisons/<comparison-name>
```

The analyzer also accepts the old flat-file summaries and `.samples.json.gz` paths.
It checks total counts and HTTP percentiles against the summary, reports any mismatch,
keeps missing metrics unavailable, and suppresses counter deltas across resets. Hold
windows are derived from the saved stage configuration; no dates, fixed 30-minute
limit or hard-coded VU schedule are assumed. Stage timing is estimated from completed
iterations, and two seconds at each hold boundary are excluded. Stage alignment is
checked against sampled VUs for ramping-VU profiles. No maximum safe capacity is
inferred automatically from a threshold PASS.

The analyzer retains numeric latency arrays for exact percentiles, so memory use grows
with request count. Compressed raw JSON is streamed rather than loaded wholesale.
The comparison command requires two explicit baselines and emits English JSON/Markdown;
review its comparability warnings before accepting any speedup as a code effect.
