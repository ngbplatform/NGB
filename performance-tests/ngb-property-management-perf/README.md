# Property Management Performance Tests

This package extends the shared NGB performance framework with Property Management as the representative vertical for platform performance testing.

The intent is to test NGB Platform capabilities through realistic PM data, not to benchmark PM-only business features. Primary workloads cover:

- auth and health
- metadata, menu, catalogs, chart of accounts
- document list/open/lookup/derive-actions
- opt-in document create/update/post lifecycle
- audit log reads
- accounting effects and document relationship graph
- period-closing read surfaces
- canonical accounting reports and the composable Ledger Analysis report

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

Performance periods are reproducible. If unset, `open` defaults to the current UTC month-to-date, `closed` to the previous UTC month, and `long` to the last 12 UTC months:

```env
NGB_PERF_FROM_UTC=2026-05-01
NGB_PERF_TO_UTC=2026-05-14
NGB_PERF_AS_OF_UTC=2026-05-14
NGB_PERF_PERIOD_UTC=2026-05-01
NGB_PERF_CLOSED_FROM_UTC=2026-04-01
NGB_PERF_CLOSED_TO_UTC=2026-04-30
NGB_PERF_LONG_FROM_UTC=2025-06-01
NGB_PERF_LONG_TO_UTC=2026-05-14
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

- `smoke.ts`: login, health, metadata, document list, report definition, trial balance, ledger analysis
- `baseline.ts`: broad platform baseline with reads, reports, effects, graph, audit, and period-closing read surfaces
- `load.ts`, `stress.ts`, `spike.ts`, `soak.ts`: profile-specific platform mixes, not lease-only browsing
- `business-day.ts`: multi-scenario platform mix for reads, reporting, opt-in writes/posting, audit, maintenance reads, and heavy reads
- `platform-read.ts`: high-volume platform read path
- `platform-read-capacity.ts`: fixed-concurrency read-path staircase for finding the sustainable VU ceiling
- `platform-mixed-capacity.ts`: fixed-concurrency mixed workload for finding the user-workload VU ceiling
- `platform-breakpoint.ts`: open-model mixed workload for finding the sustainable throughput ceiling
- `platform-reporting.ts` / `reporting-regression.ts`: canonical accounting reports plus Ledger Analysis across open/closed/long periods
- `document-lifecycle.ts`: opt-in document create/update/read/audit/post lifecycle
- `audit.ts`: audit log read workload
- `maintenance.ts`: period-closing/status/calendar read workload
- `concurrency.ts`: concurrent reads, reports, and opt-in writes
- `write-heavy.ts`: destructive write-heavy workload for create/update/post/read-after-write, audit, accounting effects, and reporting under write pressure

The report workload is platform-focused: canonical accounting reports plus the composable Ledger Analysis report. PM-domain reports such as receivables aging/open-items are intentionally excluded from the primary performance profiles. Report-level summary diagnostics include `Report Execution By Id`; when period profiles are used, rows are labeled like `accounting.ledger.analysis [closed]`.

`platform-read`, `platform-read-capacity`, `baseline`, `load`, `stress`, `spike`, `soak`, and `business-day` also enable bounded diagnostic breakdowns for platform read operations. The exported summaries include `HTTP By Operation` rows for catalog list/open and document list/open/lookup slices such as `platform.documents.list [doc=pm.rent_charge]`. The hottest read operations also materialize status-specific failure buckets for common transport and HTTP statuses, so failed runs can distinguish timeouts/status `0` from `4xx`/`5xx` responses. These rows are diagnostic only; the strict pass/fail gates remain the shared area and reliability thresholds.

Run the capacity/breakpoint tests only when you intentionally want to find the ceiling:

```bash
npm run pm:platform-read-capacity
npm run pm:platform-mixed-capacity
npm run pm:platform-breakpoint
```

For a shorter calibration run:

```bash
NGB_CAPACITY_VUS=80,160 NGB_CAPACITY_RAMP_DURATION=2m NGB_CAPACITY_HOLD_DURATION=5m npm run pm:platform-read-capacity
NGB_BREAKPOINT_RATES=2,4,8 NGB_BREAKPOINT_RAMP_DURATION=1m NGB_BREAKPOINT_HOLD_DURATION=3m npm run pm:platform-breakpoint
```

`pm:platform-read-capacity` isolates platform read path concurrency. `pm:platform-mixed-capacity`
uses a weighted user-workload mix: browsing, heavy read, reports, audit/maintenance, and write
flows gated by `NGB_PERF_ENABLE_WRITES`. `pm:platform-breakpoint` uses the same mixed workload but
ramps scheduled iterations/second until the environment starts dropping iterations or breaching
reliability/latency thresholds.

`pm:write-heavy` is intentionally excluded from `pm:all` and uses `.env.write.local` by default:

```bash
npm run pm:write-heavy
```

It is destructive and should run only against disposable performance data. Defaults create/update/post
maintenance request documents, exercise the rent charge posting/effects/graph path, and keep platform
read/reporting surfaces active while writes are running:

```env
NGB_PM_WRITE_HEAVY_DURATION=15m

NGB_PM_WRITE_HEAVY_LIFECYCLE_RATE=4
NGB_PM_WRITE_HEAVY_LIFECYCLE_TIME_UNIT=1s
NGB_PM_WRITE_HEAVY_LIFECYCLE_PRE_ALLOCATED_VUS=64
NGB_PM_WRITE_HEAVY_LIFECYCLE_MAX_VUS=160

NGB_PM_WRITE_HEAVY_POSTING_RATE=1
NGB_PM_WRITE_HEAVY_POSTING_TIME_UNIT=2s
NGB_PM_WRITE_HEAVY_POSTING_PRE_ALLOCATED_VUS=16
NGB_PM_WRITE_HEAVY_POSTING_MAX_VUS=64

NGB_PM_WRITE_HEAVY_READBACK_RATE=2
NGB_PM_WRITE_HEAVY_READBACK_TIME_UNIT=1s
NGB_PM_WRITE_HEAVY_READBACK_PRE_ALLOCATED_VUS=48
NGB_PM_WRITE_HEAVY_READBACK_MAX_VUS=128

NGB_PM_WRITE_HEAVY_REPORTING_RATE=1
NGB_PM_WRITE_HEAVY_REPORTING_TIME_UNIT=10s
NGB_PM_WRITE_HEAVY_REPORTING_PRE_ALLOCATED_VUS=8
NGB_PM_WRITE_HEAVY_REPORTING_MAX_VUS=32
```

The test aborts unless `NGB_PERF_ENABLE_WRITES=true`. Set `NGB_PERF_ENABLE_POSTING=true` when the
goal is to include the platform posting/idempotency/accounting-effects path. A passing write-heavy
run should have zero business failures, zero dropped iterations, no duplicate/invalid posting side
effects, and document post/accounting/audit p95 inside the shared platform thresholds.

`pm:business-day` is a multi-scenario arrival-rate workload. Defaults intentionally reserve extra
VUs so a healthy backend is measured without synthetic k6 scheduler drops:

```env
NGB_PM_BUSINESS_DAY_DURATION=10m

NGB_PM_BUSINESS_DAY_BROWSING_RATE=3
NGB_PM_BUSINESS_DAY_BROWSING_TIME_UNIT=1s
NGB_PM_BUSINESS_DAY_BROWSING_PRE_ALLOCATED_VUS=48
NGB_PM_BUSINESS_DAY_BROWSING_MAX_VUS=96

NGB_PM_BUSINESS_DAY_REPORTS_RATE=1
NGB_PM_BUSINESS_DAY_REPORTS_TIME_UNIT=10s
NGB_PM_BUSINESS_DAY_REPORTS_PRE_ALLOCATED_VUS=8
NGB_PM_BUSINESS_DAY_REPORTS_MAX_VUS=30

NGB_PM_BUSINESS_DAY_POSTING_RATE=1
NGB_PM_BUSINESS_DAY_POSTING_TIME_UNIT=30s
NGB_PM_BUSINESS_DAY_POSTING_PRE_ALLOCATED_VUS=4
NGB_PM_BUSINESS_DAY_POSTING_MAX_VUS=20

NGB_PM_BUSINESS_DAY_PAYMENT_APPLY_RATE=1
NGB_PM_BUSINESS_DAY_PAYMENT_APPLY_TIME_UNIT=30s
NGB_PM_BUSINESS_DAY_PAYMENT_APPLY_PRE_ALLOCATED_VUS=4
NGB_PM_BUSINESS_DAY_PAYMENT_APPLY_MAX_VUS=20

NGB_PM_BUSINESS_DAY_HEAVY_READ_RATE=1
NGB_PM_BUSINESS_DAY_HEAVY_READ_TIME_UNIT=20s
NGB_PM_BUSINESS_DAY_HEAVY_READ_PRE_ALLOCATED_VUS=4
NGB_PM_BUSINESS_DAY_HEAVY_READ_MAX_VUS=20
```

If a business-day run reports dropped iterations while HTTP failures remain zero, increase the
affected `*_PRE_ALLOCATED_VUS` first and keep `*_MAX_VUS` as the emergency ceiling. That means k6
could not schedule enough warmed workers for the requested arrival rate, not necessarily that NGB
hit a backend error.

## Fixtures

Read scenarios resolve seeded demo data from list endpoints. For stable heavy-read or write-enabled flows, provide explicit IDs:

```env
NGB_PM_FIXTURE_LEASE_ID=
NGB_PM_FIXTURE_RENT_CHARGE_ID=
NGB_PM_FIXTURE_RECEIVABLE_PAYMENT_ID=
NGB_PM_FIXTURE_AUDIT_DOCUMENT_ID=
NGB_PM_FIXTURE_PROPERTY_ID=
NGB_PM_FIXTURE_PARTY_ID=
NGB_PM_FIXTURE_MAINTENANCE_CATEGORY_ID=
NGB_ACCOUNT_FIXTURE_ACCOUNT_ID=
```

`NGB_ACCOUNT_FIXTURE_ACCOUNT_ID` enables execution of account-specific platform reports such as Account Card and General Ledger. Without it, those reports are checked definition-only.

Write-heavy behavior is disabled unless:

```env
NGB_PERF_ENABLE_WRITES=true
```

Posting and period close are separate higher-risk gates:

```env
NGB_PERF_ENABLE_POSTING=true
NGB_PERF_ENABLE_PERIOD_CLOSE=true
```

`accounting.cash_flow_statement_indirect` is reconciliation-sensitive. Keep it disabled for generic
performance runs unless the selected seed window is known to reconcile:

```env
NGB_PERF_ENABLE_CASH_FLOW=true
```

`NGB_PERF_ENABLE_EXTENDED_CASH_FLOW=true` is retained for backward compatibility with older
environments that relied on executing cash flow for non-open period profiles.

Enable writes/posting/period close only for disposable non-production data. Shared demo environments should stay read-only.
