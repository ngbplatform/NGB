# Property Management Performance Tests

This package extends the shared NGB performance framework with Property Management business workloads.

Initial coverage focuses on safe read-first flows:

- auth and health
- metadata loading
- property and party browsing
- lease list/open flows
- rent charge and receivable payment read paths
- accounting reports
- PM receivables reports when a stable lease fixture is available
- accounting effects and document flow when stable fixture documents are available

## Environment

```bash
cd performance-tests
cp ngb-property-management-perf/.env.example ngb-property-management-perf/.env.local
```

Fill in local secrets:

```env
KEYCLOAK_TESTER_CLIENT_SECRET=replace-me
NGB_TEST_USERNAME=perf.manager@example.com
NGB_TEST_PASSWORD=replace-me
```

Local defaults match `docker-compose.pm.yml`:

```env
NGB_BASE_URL=http://localhost:5173
NGB_API_BASE_URL=https://localhost:7071
KEYCLOAK_TOKEN_URL=http://pm-keycloak.localhost:7012/realms/ngb-demo/protocol/openid-connect/token
NGB_K6_HOST_ALIASES=pm-keycloak.localhost=127.0.0.1
NGB_K6_INSECURE_SKIP_TLS_VERIFY=true
```

## Running

```bash
cd performance-tests
npm install
npm run typecheck
./scripts/run-k6.sh --env-file ngb-property-management-perf/.env.local --test ngb-property-management-perf/src/tests/smoke.ts
./scripts/run-k6.sh --env-file ngb-property-management-perf/.env.local --test ngb-property-management-perf/src/tests/baseline.ts
```

PowerShell:

```powershell
cd performance-tests
./scripts/run-k6.ps1 -EnvFile ngb-property-management-perf/.env.local -TestFile ngb-property-management-perf/src/tests/smoke.ts
```

## Test Files

- `smoke.ts`: login, health, metadata, document list, report definition, trial balance, occupancy summary
- `baseline.ts`: document browsing, reports, accounting effects, document flow
- `load.ts`, `stress.ts`, `spike.ts`, `soak.ts`: profile-specific read-heavy PM workload
- `business-day.ts`: multi-scenario mix for browsing, reports, posting scaffold, payment/apply scaffold, heavy reads
- `reporting-regression.ts`: trial balance, general journal, account card definition, aging, open items

## Fixtures

Read scenarios resolve seeded demo data from list endpoints. For stable heavy-read or write-enabled flows, provide explicit IDs:

```env
NGB_PM_FIXTURE_LEASE_ID=
NGB_PM_FIXTURE_RENT_CHARGE_ID=
NGB_PM_FIXTURE_RECEIVABLE_PAYMENT_ID=
```

Write-heavy behavior is disabled unless:

```env
NGB_PERF_ENABLE_WRITES=true
```

Enable writes only for disposable non-production data. Shared demo environments should stay read-only.
