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
- generic NGB clients for health, metadata, catalogs, documents, reports, accounting effects, document flow, and command palette search

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
NGB_PERF_ENABLE_WRITES=false
```

The framework never logs passwords, client secrets, access tokens, or refresh tokens.

For local macOS/Linux runs, a vertical package can map its local identity hostnames to `127.0.0.1` through k6 `hosts` options by setting `NGB_K6_HOST_ALIASES`. Local HTTPS environments that use development certificates can set `NGB_K6_INSECURE_SKIP_TLS_VERIFY=true`.

Profiles that start multiple VUs should avoid a synchronized identity-provider burst. `NGB_AUTH_INITIAL_JITTER_SECONDS` staggers the first password-grant request per VU; the baseline profile sets this automatically.

## Authentication

The auth helper uses the dedicated Keycloak tester client:

```env
KEYCLOAK_TESTER_CLIENT_ID=ngb-tester
KEYCLOAK_TESTER_CLIENT_SECRET=replace-me
```

It calls the token endpoint with `grant_type=password`, sends `application/x-www-form-urlencoded` payloads, caches the access token per VU, and refreshes before expiry with a safety buffer.

## Profiles

Built-in profiles:

- `smoke`: 1 VU, short duration, strict thresholds
- `baseline`: stable benchmark for release and optimization comparisons
- `load`: normal traffic with ramping arrival rate
- `stress`: increasing pressure to identify degradation behavior
- `spike`: sudden burst and recovery
- `soak`: long-running stability profile

Each profile adds `profile=<name>` tags and summary trend stats.

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
- `ngb_command_palette_duration`

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

Do not add high-cardinality tags such as document IDs, user IDs, random suffixes, or tenant-specific identifiers.

## Thresholds

Common thresholds enforce low HTTP failure rates and high check success rates. Operation thresholds cover auth, health, dashboard reads, document operations, report execution, accounting effects, document flow, and command palette search.

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
