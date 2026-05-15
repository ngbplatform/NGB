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

## Load, Stress, Spike, and Soak

PM provides:

- `src/tests/load.ts`
- `src/tests/stress.ts`
- `src/tests/spike.ts`
- `src/tests/soak.ts`
- `src/tests/business-day.ts`

Run these only against dedicated non-production environments. Stress, spike, soak, and write-enabled scenarios can distort shared demo data and operational metrics.

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
```

For local runs, each vertical `.env.example` can provide k6 `hosts` aliases for the Keycloak `*.localhost` names used by that vertical Docker Compose setup. Set `NGB_K6_HOST_ALIASES=none` to disable aliases, or provide comma-separated overrides such as `pm-keycloak.localhost=127.0.0.1`. Local HTTPS uses development certificates, so local profiles set `NGB_K6_INSECURE_SKIP_TLS_VERIFY=true`.

## Metrics and Thresholds

The framework emits custom metrics:

- `ngb_business_operation_duration`
- `ngb_business_operation_failed`
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

Grafana Cloud can be used when k6 cloud auth is already configured:

```bash
./scripts/run-k6.sh --env-file ngb-property-management-perf/.env.local --test ngb-property-management-perf/src/tests/load.ts --output cloud
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
