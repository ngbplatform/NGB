# Performance Testing

NGB includes a reusable Grafana k6 + TypeScript performance testing workspace under `performance-tests/`.

Performance testing is part of production readiness for NGB because the platform is built around business workflows that can be expensive in real systems: metadata loading, document lifecycle operations, posting, accounting effects, document flow, and report execution.

## Directory Structure

```text
performance-tests/
  ngb-performance-tests-framework/
  ngb-property-management-perf/
  ngb-trade-perf/
  ngb-agency-billing-perf/
  scripts/
```

The framework package is vertical-neutral. It provides auth, typed environment parsing, the HTTP client, metrics, checks, tags, profile builders, scenario helpers, and generic NGB API clients.

Vertical packages define business flows, document codes, catalog codes, report IDs, fixture strategy, and workload mixes.

## Running PM Smoke Tests

Start the Property Management environment, then:

```bash
cd performance-tests
npm install
npm run typecheck
cp ngb-property-management-perf/.env.example ngb-property-management-perf/.env.local
./scripts/run-k6.sh --env-file ngb-property-management-perf/.env.local --test ngb-property-management-perf/src/tests/smoke.ts
```

PowerShell:

```powershell
cd performance-tests
Copy-Item ngb-property-management-perf/.env.example ngb-property-management-perf/.env.local
./scripts/run-k6.ps1 -EnvFile ngb-property-management-perf/.env.local -TestFile ngb-property-management-perf/src/tests/smoke.ts
```

## PM Platform Workloads

PM provides:

- `src/tests/smoke.ts`
- `src/tests/baseline.ts`
- `src/tests/load.ts`
- `src/tests/stress.ts`
- `src/tests/spike.ts`
- `src/tests/soak.ts`
- `src/tests/business-day.ts`
- `src/tests/platform-read.ts`
- `src/tests/platform-read-capacity.ts`
- `src/tests/platform-mixed-capacity.ts`
- `src/tests/platform-breakpoint.ts`
- `src/tests/platform-reporting.ts`
- `src/tests/reporting-regression.ts`
- `src/tests/document-lifecycle.ts`
- `src/tests/audit.ts`
- `src/tests/maintenance.ts`
- `src/tests/concurrency.ts`
- `src/tests/write-heavy.ts`

The default PM validation chain is:

```bash
npm run pm:all
```

`pm:all` runs smoke, baseline, platform-read, platform-reporting, document-lifecycle,
audit, maintenance, load, stress, spike, and business-day. It uses `.env.local` and remains
read-mostly unless write gates are explicitly enabled.

Capacity and ceiling discovery are separate:

```bash
npm run pm:platform-read-capacity
npm run pm:platform-mixed-capacity
npm run pm:platform-breakpoint
npm run pm:max
```

`platform-read-capacity` isolates platform read concurrency. `platform-mixed-capacity`
uses a representative user mix. `platform-breakpoint` raises scheduled throughput until
the environment drops iterations or breaches reliability/latency thresholds.

Run stress, spike, soak, capacity, breakpoint, and write-enabled scenarios only against
dedicated non-production environments. They can distort shared demo data and operational
metrics.

## Write-Heavy Workload

`pm:write-heavy` is destructive and intentionally excluded from `pm:all`:

```bash
npm run pm:write-heavy
```

It uses `ngb-property-management-perf/.env.write.local` by default and aborts unless
`NGB_PERF_ENABLE_WRITES=true`. Set `NGB_PERF_ENABLE_POSTING=true` when the test should
exercise posting, idempotency, accounting effects, and document graph readback.

The workload mixes:

- document create/update/delete-draft lifecycle
- rent charge posting and accounting effects
- read-after-write verification
- audit log reads
- canonical report execution during write pressure

Tune it with `NGB_PM_WRITE_HEAVY_*` variables documented in the PM README. Keep these
values in `.env.write.local`, CI secrets, or the process environment, never in committed
local env files.

## Keycloak Tester Client

Examples use the dedicated Direct Access Grants client:

```txt
KEYCLOAK_TESTER_CLIENT_ID=ngb-tester
KEYCLOAK_TESTER_CLIENT_SECRET=replace-me
```

The real secret belongs in a local `.env` file, CI secrets, or a secret manager. It must not be committed.

## Environment Variables

Minimum PM local configuration:

```txt
NGB_BASE_URL=http://localhost:5173
NGB_API_BASE_URL=https://localhost:7071
NGB_VERTICAL=property-management
KEYCLOAK_TOKEN_URL=http://pm-keycloak.localhost:7012/realms/ngb-demo/protocol/openid-connect/token
KEYCLOAK_TESTER_CLIENT_ID=ngb-tester
KEYCLOAK_TESTER_CLIENT_SECRET=replace-me
NGB_TEST_USERNAME=perf.manager@example.com
NGB_TEST_PASSWORD=replace-me
NGB_K6_ENV=local
NGB_K6_HOST_ALIASES=pm-keycloak.localhost=127.0.0.1
NGB_K6_INSECURE_SKIP_TLS_VERIFY=true
```

Optional stable PM fixtures:

```txt
NGB_PM_FIXTURE_LEASE_ID=
NGB_PM_FIXTURE_RENT_CHARGE_ID=
NGB_PM_FIXTURE_RECEIVABLE_PAYMENT_ID=
NGB_PERF_ENABLE_WRITES=false
NGB_PERF_ENABLE_POSTING=false
NGB_PERF_ENABLE_PERIOD_CLOSE=false
NGB_PERF_ENABLE_CASH_FLOW=false
```

For local runs, each vertical `.env.example` can provide k6 `hosts` aliases for the Keycloak `*.localhost` names used by that vertical Docker Compose setup. Set `NGB_K6_HOST_ALIASES=none` to disable aliases, or provide comma-separated overrides such as `pm-keycloak.localhost=127.0.0.1`. Local HTTPS uses development certificates, so local profiles set `NGB_K6_INSECURE_SKIP_TLS_VERIFY=true`.

Both `run-k6.sh` and `run-k6.ps1` load env files as defaults. If a variable already exists
in the process environment, the runner preserves it. This keeps macOS/Linux and PowerShell
behavior aligned for one-off capacity overrides:

```bash
NGB_CAPACITY_VUS=800,1000,1200,1400 npm run pm:platform-read-capacity
```

```powershell
$env:NGB_CAPACITY_VUS = '800,1000,1200,1400'
npm run pm:platform-read-capacity
```

## Metrics and Thresholds

The framework emits custom metrics:

- `ngb_business_operation_duration`
- `ngb_business_operation_failed`
- `ngb_business_operation_count`
- `ngb_auth_duration`
- `ngb_document_post_duration`
- `ngb_report_execution_duration`
- `ngb_accounting_effects_duration`
- `ngb_document_flow_duration`

Standard tags include `app`, `vertical`, `profile`, `area`, `operation`, `scenario`, `documentType`, `reportId`, and `catalogType`.

Thresholds include common reliability checks and operation-specific latency budgets for auth, health, document reads/posts, reports, accounting effects, and document flow.

Vertical suites can opt into per-report diagnostics by passing stable report codes as `reportBreakdownIds` to a profile builder. This creates low-risk diagnostic submetrics for `platform.reports.execute` and adds `Report Execution By Id` to exported summaries, while keeping the shared framework vertical-neutral.

## Grafana Integration

Local terminal output is the default. To export a summary:

```bash
./scripts/run-k6.sh \
  --env-file ngb-property-management-perf/.env.local \
  --test ngb-property-management-perf/src/tests/baseline.ts \
  --summary-export artifacts/pm-baseline.summary.json
```

Grafana Cloud can be used when k6 cloud auth is already configured. The runner keeps execution on the current machine and uploads results with `k6 cloud run --local-execution --include-system-env-vars`, so variables loaded from the env file are visible to the k6 script.

Set `K6_CLOUD_PROJECT_ID` to route the run to a specific Grafana Cloud k6 project. `K6_CLOUD_NAME` is optional, but recommended so the run is easy to find:

```bash
K6_CLOUD_PROJECT_ID=<grafana-cloud-k6-project-id> \
K6_CLOUD_NAME="NGB PM baseline $(date -u +%Y-%m-%dT%H:%M:%SZ)" \
./scripts/run-k6.sh \
  --env-file ngb-property-management-perf/.env.local \
  --test ngb-property-management-perf/src/tests/baseline.ts \
  --output cloud
```

Prometheus remote write requires `K6_PROMETHEUS_RW_SERVER_URL`:

```bash
K6_PROMETHEUS_RW_SERVER_URL=http://localhost:9090/api/v1/write \
  ./scripts/run-k6.sh --env-file ngb-property-management-perf/.env.local --test ngb-property-management-perf/src/tests/load.ts --output prometheus-remote-write
```

## CI Strategy

CI type-checks the TypeScript performance workspace. Live k6 runs are manual and should be skipped unless target URLs and secrets are configured. Normal pull requests should not run live load tests.

## Adding Scenarios

Add a scenario when it represents a real business workflow, not just a raw endpoint. Use stable operation names, avoid high-cardinality tags, resolve test data from seeded fixtures where possible, and keep destructive operations opt-in.

## Security

Never commit real `.env` files, passwords, client secrets, tokens, tenant IDs, production URLs, or document IDs. Tester users should have only the permissions required for the scenario. Production stress testing is out of scope for the repository workflow.
