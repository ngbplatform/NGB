# Platform read regressions corrected before 3.4.0

Status: all three authorized fixes verified on 2026-11-04. This report covers
targeted regression testing, not full platform release certification. The user
will run the complete Backend and Frontend suites.

## Corrections

1. **Composable report dates.** `PostgresReportPlanExecutor` previously classified
   invalid input as an SDK configuration failure, producing HTTP 500. Invalid
   `from_utc`, `to_utc` and `as_of_utc` values now produce `report.layout.invalid`,
   HTTP 400 and a `parameters.<code>` field path. Strict ISO parsing is retained.
   Inclusive upper dates of `9999-12-31` are rejected explicitly because their
   exclusive next day cannot be represented. Real configuration errors keep
   their original classification. The same validation covers execution, page
   reads and streaming.
2. **First occurrence-history page.** The PostgreSQL movements reader used an
   untyped null cursor parameter in `IS NULL`, causing SQLSTATE 42P08. Explicit
   `timestamptz` and `bigint` casts allow a null first cursor without changing
   exact-date filtering, ordering, resource values, dimensions or storno.
   Continuations retain both occurrence time and movement ID.
3. **General Journal Entry search and MCP detail.** Generic search attempted to
   use the ordinary document storage descriptor, which requires a `display`
   column absent from journal storage. The registered search service now routes
   journals through `IAuthorizedGeneralJournalEntryReadService`, after selecting
   authorized metadata and applying kind/type and fan-out limits. Ordinary
   documents retain their bounded generic lookup. Metadata reads validate the
   definition without constructing a generic CRUD storage descriptor. This
   unblocks MCP metadata, typed detail and compatibility fetch through their
   existing authorized services. Journal schemas, migrations, lifecycle and
   generic CRUD behavior are unchanged.

The existing four-argument `NgbSearchService` constructor remains available and
retains its generic-reader behavior. Standard DI selects the new overload with
the specialized journal reader. Manually composed integrations need that
overload to include built-in journals. No public interface, DTO or permission
contract was removed or changed.

## Verification

Evidence root: `artifacts/ai-devkit/platform-fixes-3.4.0/`.
`summary.json` binds the reports to their SHA-256 digests.

| Check | Result | Evidence |
| --- | --- | --- |
| Targeted Runtime tests | 64 passed, 0 failed | `runtime/runtime.trx` |
| Targeted PostgreSQL provider tests | 127 passed, 0 failed | `postgres/postgres.trx` |
| External application HTTP/MCP/PostgreSQL tests | 25 passed, 0 failed | `http-verified-final/report.json` |
| Kit guidance, capability and ownership checks | 16 passed, 0 failed | `kit-tests.log` |
| Changed executable SDK lines and branches | 100%; 44 changed executable lines | `coverage-summary.json` |
| API compatibility against candidate 18 | Passed | `api-compatibility.json` |
| Kit/template/CLI and starter package hashes | Passed | `delivery.json` |

The affected date-mapping, metadata-validation, search-document and
occurrence-cursor methods have 100% line and branch coverage. This does not claim
100% coverage of the entire SDK or replace the full suites owned by the user.

Date regressions include all three parameter names, invalid/null/empty values,
leap dates, representable range boundaries and rejection before database access.
Metadata checks preserve unknown-type, missing-head and invalid amount-field
validation, optional presentation and ordinary generic reads. Search tests cover
standard DI selection, mixed sources, kind/type filters, inaccessible metadata,
bounded results, truncation, title fallbacks, cancellation and reader failure.
Existing tests retain coverage of the original constructor.

The independent example is copied outside the repository and restored from an
isolated NuGet feed/cache. It uses candidate 18 dependencies with the exact
corrected Runtime/PostgreSql archives recorded in the report; it has no project
references to the platform. Real PostgreSQL, Keycloak and MinIO containers verify
HTTP validation, initial and subsequent cursor pages, equal timestamps, dimension
filters, empty pages, journal search alongside ordinary documents and reports,
MCP detail/fetch, denied journal access and unchanged journal state. Existing
streaming and search regressions pass in the same focused run.

The first external run passed 24 cases and failed one newly added link assertion:
the test host had no journal URL template. Its failure remains under
`http-verified/`. Adding the route to the test host resolved it; the final run
passed all 25 cases without weakening the assertion.

APICompat checks public signatures, parameter names and attributes. PostgreSql
also passes strict equality. Runtime adds one constructor and preserves every
previous signature. Its check excludes only `AsyncStateMachineAttribute`, whose
compiler-generated state-machine ordinal changed; the original diagnostic and
the final check are retained. No compatibility suppression file was introduced.

## Delivery and documentation

Runtime, PostgreSql, Templates and CLI 3.4.0 packages were rebuilt. The NuGet
archives are under the evidence root's `packages/` directory and in both local
feeds (`artifacts/nuget/` and `artifacts/nuget-release/`). The CLI archive is
`artifacts/npm/ngbplatform-cli-3.4.0.tgz`. The replaced 3.4.0 cache entries were
invalidated, then solution dependencies were restored from the refreshed feed.

Starter lockfiles identify the corrected package bytes. The generated contract
inventory, capability registry, known-limitations records, reference guidance,
example regressions and template-owned copies now describe the corrected
behavior. Contract hashes were extracted from the actual archives; the existing
raw metadata signature format also changes hashes when metadata token offsets
move, so APICompat supplies the semantic compatibility check.

Historical profiles, reports and sealed candidates remain intact. No package
was published, no new release certificate was claimed, and no full Backend or
Frontend suite was launched for this task.
