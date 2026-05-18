# NGB Performance Tests

This workspace contains the NGB performance testing stack for Grafana k6 and TypeScript.

```text
performance-tests/
  ngb-performance-tests-framework/   Reusable vertical-neutral k6 framework
  ngb-property-management-perf/      Property Management scenarios
  ngb-trade-perf/                    Trade smoke scaffold
  ngb-agency-billing-perf/           Agency Billing smoke scaffold
  scripts/                           macOS/Linux and PowerShell runners
```

The framework is intentionally independent from the normal backend and frontend builds. It type-checks in CI, while live k6 runs are opt-in and should target dedicated non-production environments.

## Quick Start

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
npm install
npm run typecheck
Copy-Item ngb-property-management-perf/.env.example ngb-property-management-perf/.env.local
./scripts/run-k6.ps1 -EnvFile ngb-property-management-perf/.env.local -TestFile ngb-property-management-perf/src/tests/smoke.ts
```

## Projects

- [Framework README](./ngb-performance-tests-framework/README.md)
- [Property Management README](./ngb-property-management-perf/README.md)
- [Trade README](./ngb-trade-perf/README.md)
- [Agency Billing README](./ngb-agency-billing-perf/README.md)

## Scripts

- `npm run typecheck`
- `npm run pm:smoke`
- `npm run pm:baseline`
- `npm run pm:load`
- `npm run pm:stress`
- `npm run pm:spike`
- `npm run pm:soak`
- `npm run pm:business-day`
- `npm run pm:reporting-regression`
- `npm run pm:platform-read`
- `npm run pm:platform-read-capacity`
- `npm run pm:platform-mixed-capacity`
- `npm run pm:platform-breakpoint`
- `npm run pm:platform-reporting`
- `npm run pm:document-lifecycle`
- `npm run pm:audit`
- `npm run pm:maintenance`
- `npm run pm:concurrency`
- `npm run pm:write-heavy`
- `npm run pm:max`
- `npm run pm:all`

Use `scripts/run-k6.*` when you need `.env` loading, summary export, Grafana Cloud, or Prometheus remote write output.
Both `run-k6.sh` and `run-k6.ps1` treat the env file as defaults: an already-set process
environment variable wins over the value in `.env.local` or `.env.write.local`.

`pm:all` is the standard read-mostly PM validation chain. `pm:write-heavy` is destructive,
uses `ngb-property-management-perf/.env.write.local`, and is intentionally excluded from
`pm:all`.
