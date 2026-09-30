# Performance operations

Reusable tools, tests and operating instructions belong in Git. Run-specific reports,
investigation notes and generated evidence belong in ignored `artifacts/`, which can
be removed when no run or analysis is active.
No maintained tool reads its code, configuration defaults, or SQL from `artifacts/`.

- [Diagnostic runs and configuration](diagnostics.md)
- [Comparison methodology](comparison-methodology.md)

## Storage contract

| Location | Contents | Retention |
| --- | --- | --- |
| `scripts/diagnostics/` | Launchers, Postgres MCP sampler, fixed monitoring SQL, local metric probe | Git |
| `scripts/analysis/` | Offline analysis, explicit before/after comparison, saved run status | Git |
| `docs/performance/` | Reusable instructions, configuration reference and comparison methodology | Git |
| `artifacts/runs/` | Logs, summaries, manifests, compressed time series, resource samples, run state | Disposable local output |
| `artifacts/reports/` | Dated investigation reports, run comparisons, test-run records and evidence indexes | Disposable local output |
| `artifacts/probes/` | Results of the opt-in loopback metric probe | Disposable local output |
| `.env.local`, `.env.write.local`, database env files | Local configuration and credentials | Local only; never commit |

Deleting `artifacts/` removes local reports and the ability to reanalyze their raw
observations. To retain a conclusion or reproduce its analysis, archive the report and
its supporting run directories externally before cleanup. An evidence index containing
source names and SHA-256 hashes is not a backup of the measurements.
Do not delete output while a run or analyzer is writing it. No automatic cleanup,
Docker pruning, database restoration, or Seq retention policy is installed by these tools.

## Investigation material

Temporary investigation scripts and fixture-specific queries are not supported entry
points. Use the commands in the diagnostic runbook. Promote a tool into `scripts/` only
after removing run-specific assumptions and adding appropriate validation. Preserve
lasting implementation contracts near the owning component and in regression tests;
keep dated test results and incident narratives with the disposable reports.

## Tool validation

From `performance-tests`:

```bash
npm ci
npm run typecheck
npm run test:tooling
```

The tooling tests are offline: they use synthetic data and in-memory MCP transports.
They do not connect to PostgreSQL, start Docker containers or run k6. The separate
`test:metric-grouping` command runs k6 against a temporary loopback HTTP fixture and
must be run between benchmarks, never concurrently with a measurement.
