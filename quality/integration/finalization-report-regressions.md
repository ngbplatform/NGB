# Finalization and report admission regression tests

These tests verify the concurrent finalization protocol, transactional recovery and report admission contracts. They do not establish the platform's maximum throughput or the optimal admission limits.

The September 27 expansion adds **91 integration cases**: 44 finalization cases and 47 HTTP cases. Counts include each xUnit theory input. The default-projector class now contains 73 cases, including the 29 previous regressions.

## Added finalization coverage

All paths below are relative to the repository root, under `NGB.Runtime.IntegrationTests/OperationalRegisters/`.

| File suffix in `OperationalRegisterCumulativeBalances_*_P0Tests.cs` | Cases | Verified behavior |
|---|---:|---|
| `Model` | 16 | Fixed random seeds, 24 documents, 6 months, 5 dimension sets, 3 resources. Four concurrent writers execute Post/Repost/Unpost, duplicate requests and a rolled-back business transaction. Every turnover and balance is compared with an independent model of intended active documents. Old committed projections remain visible during preparation; unchanged rebuilds preserve row versions. |
| `CrashRecovery` | 8 | Worker-process kill and PostgreSQL backend termination, each after preparation, after tail application, immediately before commit and after commit. The committed balance, turnover and dirty/finalized marker remain consistent. Recovery runs in a new OS process. |
| `CrashRecovery` | 1 | A TCP proxy drops PostgreSQL's successful COMMIT acknowledgement. The original client reports failure, but a fresh process observes the completed transaction and does not duplicate work. |
| `ActiveSqlCrash` | 4 | Process kill/backend termination while PostgreSQL is executing prefix or tail SQL. A trigger and `pg_blocking_pids` prove the execution point. A real writer commits while prefix SQL is blocked. Rollback releases all locks and a fresh worker recovers. |
| `ProtocolGuards` | 3 | Sequence increment 2, increment -1 and CYCLE are rejected before publication; previous projections and row versions remain unchanged. Corrected configuration allows recovery. The earlier suite also tests CACHE > 1. |
| `ProtocolGuards` | 2 | Repeatable Read and Serializable use the serialized runner path; direct concurrent preparation is rejected. |
| `ProtocolGuards` | 2 | An external transaction controls commit/rollback; other sessions cannot see its unpublished results. |
| `ProtocolGuards` | 1 | Timeout acquiring the movement boundary exhausts exactly three attempts, reports failure and preserves committed projections. Unlocking permits a correct retry. |
| `ProtocolGuards` | 1 | Cancellation of a finalizer waiting on ORF, confirmed by PostgreSQL's blocker graph, does not cancel its owner or prevent movement writes. |
| `ProgressAndCalendar` | 2 | Two/four continuously committing writers remain running through four successful finalization passes. Each pass performs one prefix preparation. Final sums match independent per-writer commit counts. |
| `ProgressAndCalendar` | 4 | UTC month start, last representable microsecond before month end, next-month midnight, leap February, ordinary February, a 30-day month and December/January rollover. Both prefix and tail contribute exact expected values. |

The prior `CatchupConcurrency`, `CatchupProgress` and `CatchupEdges` cases additionally cover accounting-first lock order, dirty predecessor snapshots, eight concurrent finalizers, pending allocation commit/rollback, ID gaps, signed storno, zero-row pruning, zero-resource registers, a real database-detected deadlock, cancellation during publication, SQL failure between projection updates, bounded publication retries and prepared-object lifetime.

## Added HTTP coverage

Sources are under `NGB.PropertyManagement.Api.IntegrationTests/Reports/`.

| File / scenario | Cases | Verified behavior |
|---|---:|---|
| `PmReporting_AdmissionFaults_P0Tests`: burst | 5 | FIFO and concurrency limits for page, JSON export and form export; page bursts with 12 active/48 queued and 12 active/no queue. Overflow is 429 with Retry-After; queued work has not started. |
| Queue deadline | 3 | Expired waiters receive 429 without executing report work; a replacement can occupy the freed queue slot. |
| Queued cancellation | 9 | Cancel the head, middle or tail for each route; survivors retain order and the cancelled request never executes. |
| Active cancellation | 3 | Request cancellation reaches report work; resources and admission are released to the next request. |
| Report failure | 5 | Execution, export preparation and export writing errors before headers produce 500 and release resources. |
| Execution deadline | 3 | Own execution deadline before headers produces 504, cleans up and admits the next request. |
| Partial response | 4 | After response headers and a known payload prefix, fault/deadline aborts JSON/form export streams. No JSON error is appended to the file; the next request succeeds. |
| Separate queue/execution budgets | 3 | A wait longer than the configured execution limit does not consume the later execution budget. |
| Invalid request | 3 | Invalid JSON/form input produces 400, starts no report work and leaves admission reusable. |
| Independent budgets | 3 | Saturating pages does not consume download capacity, and downloads do not consume page capacity. |
| `PmReporting_DatabaseCancellation_P0Tests` | 6 | Real HTTP authentication/MVC, ReportEngine, PostgreSQL readers and XLSX writer. Client cancellation and execution deadline on each route stop a query demonstrably waiting on a real table lock. While the lock remains held, PostgreSQL has no active or idle-in-transaction work for the test application; admission is free. The next real report succeeds with MaxPoolSize=1, and exports are valid OpenXML archives. |

`ReportAdmissionHarness` replaces only report work with explicit gates for the 41 admission/failure scenarios; it does not establish database behavior on its own. The six database-cancellation cases replace neither ReportEngine nor the download service. Existing neighboring tests retain assertions about business data, security, real XLSX contents, large reports and cursor paging.

Existing unit tests cover limiter metrics, configuration validation, shutdown and repeated cancellation/release races. Existing BackgroundJobs tests ensure publication exhaustion propagates as failure rather than successful zero work.

## Fault injection and reproducibility

- `quality/integration/NGB.Finalization.TestWorker` is a test-only executable referencing the production runner, PostgreSQL rebuilder and unit of work. The Runtime integration project builds and copies it automatically. It is not a production service or a deployed worker.
- Checkpoints use redirected stdin/stdout. Connection credentials are sent over stdin and are not placed in command arguments or output. Process disposal kills any remaining child and observes stderr.
- A dedicated loopback TCP proxy recognizes PostgreSQL's `CommandComplete(COMMIT)` response before dropping it. It does not rewrite SQL. TLS/GSS and pooling are disabled only for this isolated fault-injection connection.
- PostgreSQL containers and databases belong to the test fixtures. Triggers, table locks, advisory locks and backend termination operate only on those fixtures. Tests do not point at the PM load-test database.
- Critical interleavings use gates, transaction checkpoints and PostgreSQL blocker observations. Wall-clock delays are used for actual timeout semantics or bounded polling, not as proof that a race occurred.
- Data-model seeds are fixed and appear in xUnit case names, so any failed model case can be reproduced.
- Integration fixtures default to `postgres:16`. `NGB_TEST_POSTGRES_IMAGE` selects another version without changing the default. The PM environment used for this work selects `postgres:18.3-alpine`.
- Two existing RESTRICT tests assert the version-appropriate SQLSTATE (23503 before PostgreSQL 18; 23001 on 18), the exact constraint name, and preservation of the parent and child. PostgreSQL changed this in [commit 086c84b23](https://github.com/postgres/postgres/commit/086c84b23).

## Run from the repository root

Requires the .NET 10 SDK and Docker. Initial execution restores/builds dependencies and can pull container images. The PM API suite also starts its isolated Keycloak fixture.

```sh
dotnet test NGB.Runtime.IntegrationTests/NGB.Runtime.IntegrationTests.csproj \
  --filter FullyQualifiedName~OperationalRegisterCumulativeBalances_DefaultProjector_P0Tests \
  --environment NGB_TEST_POSTGRES_IMAGE=postgres:18.3-alpine

dotnet test NGB.PropertyManagement.Api.IntegrationTests/NGB.PropertyManagement.Api.IntegrationTests.csproj \
  --filter 'FullyQualifiedName~Reports|FullyQualifiedName~Posting' \
  --environment NGB_TEST_POSTGRES_IMAGE=postgres:18.3-alpine
```

Repeat with `--environment NGB_TEST_POSTGRES_IMAGE=postgres:16` for the default version. Do not use `--no-build` on a clean checkout: the crash tests need the copied worker output. To run the full Runtime regression suite, omit its `--filter`.

## Limits of the evidence

These are concrete regression proofs for the exercised interleavings, not a mathematical proof of every schedule. They do not simulate a PostgreSQL server/power failure, replication failover, storage corruption, an ingress proxy or a broken physical network to production. The HTTP pipeline tests use TestServer, not an external Kestrel/reverse-proxy deployment.

They also do not establish maximum RPS, sustainable active users, production p95/p99, WAL savings, memory/disk use at production data volume, or optimal queue length. In particular, the controlled queue/no-queue bursts verify behavior, not a performance A/B result. Admission limits are per API process, not a cluster-wide quota.

The user runs the final 65-minute capacity profile. To evaluate the original finalization conflict, a nonempty finalization must actually overlap the load; an empty job or a test outside that window cannot establish the performance outcome.
