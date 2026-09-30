# Backend integration-test execution

Integration tests use one isolated PostgreSQL container per xUnit collection. Production migrations run once when a collection fixture starts; ordinary test isolation is data-only through Respawn.

Tests that execute DDL or intentionally damage schema objects must use the project's `*Schema*Collection`. Those collections rebuild and migrate `public` between test cases. A missing relation during an ordinary Respawn reset fails fast with a message directing the test to the schema-changing collection.

Npgsql pooling remains enabled with connection reset-on-close. Temporary databases that are created and dropped inside a test intentionally opt out of pooling so no pooled connection can prevent `DROP DATABASE`.

CRM reporting tests use a compact deterministic seed. The production-volume CRM seed contract is marked as a volume test and is opt-in for ordinary local runs:

```bash
NGB_RUN_VOLUME_TESTS=true dotnet test NGB.CRM.Api.IntegrationTests/NGB.CRM.Api.IntegrationTests.csproj
```

The full backend coverage runner enables volume tests automatically.

## Report performance regressions

`ReportPerformanceProbe.cs` and `ReportScaleAssertions.cs` support automated report tests. Explicit probes count Npgsql commands without requiring diagnostic output. Scale tests compare query counts across page sizes and validate complete XLSX exports; they do not depend on files produced by a local audit.

To collect optional SQL traces during an integration run, set `NGB_REPORT_AUDIT_DIR` to a fresh directory under the repository's ignored `artifacts/` directory, or to an external temporary directory. For example, from the repository root:

```bash
NGB_REPORT_AUDIT_DIR="$PWD/artifacts/reporting/pm-run" dotnet test NGB.PropertyManagement.Api.IntegrationTests/NGB.PropertyManagement.Api.IntegrationTests.csproj --filter 'FullyQualifiedName~Reports'
```

The probe creates the output directory and appends JSONL records containing SQL text, command counts and timing samples. It does not record bind values or connection strings. Allocation deltas are process-wide, and working-set samples are not per-request peak memory measurements. Use a new output directory for each run; do not commit generated results. CI runs can retain them as build artifacts.

Live reporting/load scenarios use the existing [performance testing workspace](../../performance-tests/README.md).

## Rider

`xunit.runner.json` is copied to every integration-test output directory and enables conservative collection parallelism with at most four threads. For a full-solution Rider run, set **Settings | Build, Execution, Deployment | Unit Testing | Maximum number of test runners to run in parallel** to `2` initially. Increase to `3` or `4` only when Docker has enough CPU and memory for the additional PostgreSQL and Keycloak containers.

Run the full coverage gate from the repository root with:

```bash
./run-backend-full-coverage.sh
```

The coverage runner executes two independent test projects concurrently by default. Override that bounded concurrency when appropriate:

```bash
NGB_BACKEND_COVERAGE_JOBS=3 ./run-backend-full-coverage.sh
```
