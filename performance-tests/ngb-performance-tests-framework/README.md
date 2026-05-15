# NGB Performance Tests Framework

`ngb-performance-tests-framework` is a vertical-neutral Grafana k6 + TypeScript framework for validating NGB platform behavior under realistic business workloads.

It belongs outside the .NET solution and UI workspace so performance tests can evolve independently, run locally, and be promoted to CI or dedicated environments without slowing normal builds.

## Architecture

The framework contains only shared NGB concepts:

- typed environment parsing
- Keycloak password grant authentication
- per-VU access-token caching
- HTTP client wrapper with safe logging
- standard request tags
- reusable checks
- custom k6 metrics
- profile builders
- scenario helpers
- generic NGB clients for health, metadata, catalogs, documents, reports, accounting effects, and document flow

Vertical-specific projects provide business flows, document type codes, catalog type codes, report IDs, fixture strategy, and scenario mixes.

## k6 and TypeScript Model

The test files are `.ts` files intended for modern k6 direct execution. Type-checking is separate:

```bash
cd performance-tests
npm install
npm run typecheck
```

k6 runtime scripts must not use Node-only APIs. Do not import `fs`, `path`, `axios`, `node-fetch`, or browser-only packages. Static fixture files should be loaded in init context, preferably through `SharedArray` for larger data.

## Environment Variables

Required:

```env
NGB_BASE_URL=https://vertical-web.example.test
NGB_API_BASE_URL=https://vertical-api.example.test
NGB_VERTICAL=example-vertical
KEYCLOAK_TOKEN_URL=https://identity.example.test/realms/ngb-demo/protocol/openid-connect/token
KEYCLOAK_TESTER_CLIENT_ID=ngb-tester
KEYCLOAK_TESTER_CLIENT_SECRET=replace-me
NGB_TEST_USERNAME=perf.manager@example.com
NGB_TEST_PASSWORD=replace-me
```

Optional:

```env
NGB_TEST_TENANT_CODE=demo
NGB_TEST_COMPANY_CODE=demo
NGB_K6_SUMMARY_EXPORT=artifacts/k6-summary.json
NGB_K6_ENV=local
NGB_K6_HOST_ALIASES=identity.localhost=127.0.0.1
NGB_K6_INSECURE_SKIP_TLS_VERIFY=true
NGB_AUTH_INITIAL_JITTER_SECONDS=10
NGB_AUTH_SEED_REFRESH_JITTER_SECONDS=600
NGB_AUTH_TOKEN_MAX_ATTEMPTS=3
NGB_AUTH_TOKEN_RETRY_BACKOFF_SECONDS=1
NGB_PERF_ENABLE_WRITES=false
```

The framework never logs passwords, client secrets, access tokens, or refresh tokens.

For local macOS/Linux runs, a vertical package can map its local identity hostnames to `127.0.0.1` through k6 `hosts` options by setting `NGB_K6_HOST_ALIASES`. Local HTTPS environments that use development certificates can set `NGB_K6_INSECURE_SKIP_TLS_VERIFY=true`.

Profiles that start multiple VUs should avoid synchronized identity-provider bursts.
`NGB_AUTH_INITIAL_JITTER_SECONDS` staggers the first password-grant request per VU; the baseline
profile sets this automatically. Long-running profiles seed VUs from `setup()` and refresh that seed
token through a randomized per-VU window controlled by `NGB_AUTH_SEED_REFRESH_JITTER_SECONDS`.
Transient token endpoint failures are retried up to `NGB_AUTH_TOKEN_MAX_ATTEMPTS` with full-jitter
backoff based on `NGB_AUTH_TOKEN_RETRY_BACKOFF_SECONDS`.

## Authentication

The auth helper uses the dedicated Keycloak tester client:

```env
KEYCLOAK_TESTER_CLIENT_ID=ngb-tester
KEYCLOAK_TESTER_CLIENT_SECRET=replace-me
```

It calls the token endpoint with `grant_type=password`, sends `application/x-www-form-urlencoded`
payloads, seeds VUs with the setup token, and refreshes per VU before expiry with a safety buffer.
Long-running capacity, breakpoint, stress, spike, business-day, and soak profiles must not rely on a
single static setup token.

## Profiles

Built-in profiles:

- `smoke`: 1 VU, short duration, strict thresholds
- `baseline`: stable benchmark for release and optimization comparisons
- `load`: normal traffic with ramping arrival rate
- `capacity`: fixed-concurrency staircase for finding the sustainable VU ceiling
- `breakpoint`: open-model arrival-rate staircase for finding the sustainable throughput ceiling
- `stress`: increasing pressure to identify degradation behavior
- `spike`: sudden burst and recovery
- `soak`: long-running stability profile

Each profile adds `profile=<name>` tags and summary trend stats.

The capacity profile defaults to `80,160,240,320` VU plateaus with `5m` ramps, `10m`
holds, and a `5m` ramp-down. Override it without changing test code:

```env
NGB_CAPACITY_VUS=80,160,240,320
NGB_CAPACITY_RAMP_DURATION=5m
NGB_CAPACITY_HOLD_DURATION=10m
NGB_CAPACITY_RAMP_DOWN_DURATION=5m
```

The breakpoint profile defaults to `2,4,8,12,16,24,32` iterations/second with
`2m` ramps, `5m` holds, `80` pre-allocated VUs, and `500` max VUs. Override it
without changing test code:

```env
NGB_BREAKPOINT_RATES=2,4,8,12,16,24,32
NGB_BREAKPOINT_RAMP_DURATION=2m
NGB_BREAKPOINT_HOLD_DURATION=5m
NGB_BREAKPOINT_RAMP_DOWN_DURATION=3m
NGB_BREAKPOINT_PRE_ALLOCATED_VUS=80
NGB_BREAKPOINT_MAX_VUS=500
```

## Metrics and Tags

Custom metrics:

- `ngb_business_operation_duration`
- `ngb_business_operation_failed`
- `ngb_business_operation_count`
- `ngb_auth_duration`
- `ngb_document_post_duration`
- `ngb_report_execution_duration`
- `ngb_accounting_effects_duration`
- `ngb_document_flow_duration`

Standard tags:

- `app=ngb`
- `vertical`
- `profile`
- `area`
- `operation`
- `scenario`
- `documentType`
- `reportId`
- `catalogType`
- `entityKind`
- `periodProfile`
- `status`

Do not add high-cardinality tags such as document IDs, user IDs, random suffixes, or tenant-specific identifiers.

Vertical packages can pass stable report codes to profile builders through `reportBreakdownIds`. The framework then creates diagnostic k6 submetrics for `platform.reports.execute` by `reportId`, so exported summaries show per-report latency without the shared framework knowing vertical-specific report catalogs. When verticals add `periodProfile`, summary rows include that label as well.

Vertical packages can also pass stable tag selectors through `diagnosticBreakdowns`. The framework materializes matching k6 submetrics for HTTP duration, HTTP failures, business operation duration, and business operation failures. Use this for bounded, low-cardinality slices such as `area + operation + documentType`, `area + operation + catalogType`, or a small set of status-specific failure buckets; never include document IDs or user-specific values.

## Thresholds

Common thresholds enforce low HTTP failure rates and high check success rates. Operation thresholds cover auth, health, metadata, catalogs, admin/menu, chart of accounts, document operations, report execution/export, accounting effects, document flow, audit, and period-closing read surfaces.

If a new environment cannot meet a target yet, document the temporary relaxation in the vertical project rather than silently removing the threshold.

## Adding a Vertical

1. Create a new workspace package under `performance-tests`.
2. Add `.env.example`, README, and `src/tests/smoke.ts`.
3. Define vertical document/catalog/report codes in the vertical package.
4. Compose generic framework flows into vertical business scenarios.
5. Keep write-heavy scenarios disabled unless fixtures and environment isolation are explicit.
6. Add package references to `performance-tests/package.json` and `performance-tests/tsconfig.json`.

## Adding a Scenario

1. Start from a business workflow, not a single endpoint.
2. Use generic clients for platform concepts.
3. Add stable `area`, `operation`, and `scenario` tags.
4. Avoid hardcoded IDs. Resolve fixtures by search/code or accept explicit fixture IDs through env vars.
5. Add a profile or include the scenario in an existing workload mix.

## Running Locally

macOS/Linux:

```bash
cd performance-tests
cp <vertical-package>/.env.example <vertical-package>/.env.local
./scripts/run-k6.sh --env-file <vertical-package>/.env.local --test <vertical-package>/src/tests/smoke.ts
```

PowerShell:

```powershell
cd performance-tests
Copy-Item <vertical-package>/.env.example <vertical-package>/.env.local
./scripts/run-k6.ps1 -EnvFile <vertical-package>/.env.local -TestFile <vertical-package>/src/tests/smoke.ts
```

## Summary Export

Set `NGB_K6_SUMMARY_EXPORT` or pass runner options:

```bash
./scripts/run-k6.sh \
  --env-file <vertical-package>/.env.local \
  --test <vertical-package>/src/tests/baseline.ts \
  --summary-export artifacts/baseline.summary.json
```

`artifacts/` and generated summary files are ignored by git.

When a vertical configures `reportBreakdownIds`, Markdown and terminal summaries include `Report Execution By Id` with `avg`, `med`, `p90`, `p95`, `p99`, and `max` per report.

When a vertical configures `diagnosticBreakdowns`, summaries also include `HTTP By Operation`, showing failure count/rate plus `p95`, `p99`, and `max` per configured operation slice. Failed custom business-operation samples include a `status` tag, so status-specific diagnostic selectors can be added when an investigation needs exact failure buckets. Diagnostic materialization thresholds are intentionally omitted from the `Thresholds` section; only real pass/fail gates are shown there.

## Grafana and Prometheus

Local terminal output is the default. Grafana Cloud can be used when the user has already configured k6 cloud authentication:

```bash
./scripts/run-k6.sh --env-file <vertical-package>/.env.local --test <vertical-package>/src/tests/smoke.ts --output cloud
```

Prometheus remote write requires `K6_PROMETHEUS_RW_SERVER_URL`:

```bash
K6_PROMETHEUS_RW_SERVER_URL=http://localhost:9090/api/v1/write \
  ./scripts/run-k6.sh --env-file <vertical-package>/.env.local --test <vertical-package>/src/tests/load.ts --output prometheus-remote-write
```

## Security

Do not commit real `.env` files, client secrets, user passwords, access tokens, refresh tokens, tenant IDs, document IDs, or production URLs. Stress, spike, soak, and write-enabled scenarios must run only against dedicated non-production environments.

## Troubleshooting

- `Missing required environment variables`: copy `.env.example` and fill local values.
- `k6 is not installed`: install Grafana k6 and ensure it is on `PATH`.
- `lookup ...localhost: no such host`: keep `NGB_K6_HOST_ALIASES` in the env file or add the hostname to `/etc/hosts`.
- `401` or `403`: verify the Keycloak token URL, tester client secret, Direct Access Grants, and test user permissions.
- Report execution validation errors usually mean required filters are missing. Provide stable fixture IDs through vertical env vars or keep the scenario definition-only.
