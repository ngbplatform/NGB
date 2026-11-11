# Reporting package refresh for AI Development Kit 3.4.0

This refresh brings the Kit's package identities and external consumers up to date
with the reporting implementation in commit `81533f7c`. The release version remains
3.4.0. Historical candidates, upgrade fixtures and evaluations retain their original
bytes and outcomes.

The execution report is
[reporting-refresh-20261111/summary.json](../../../artifacts/ai-devkit/reporting-refresh-20261111/summary.json).
It records the actual checks, package identities and evidence paths. The assessed
candidate is
[reporting-refresh-final-20261111](../../../artifacts/ai-devkit/reporting-refresh-final-20261111/release-manifest.json).
Only completed passing checks in those receipts establish success; this document
does not substitute for a full release certificate.

## Implementation and existing regression evidence

Production changes are confined to `AccountingLedgerAnalysisPostgresDatasetSource`,
`PostgresCashFlowIndirectSnapshotReader` and `ReportPagedQueryExecutor`:

- Eligible Ledger detail pages preserve the indexed period order before document
  joins, while retaining the outer grouping and existing cursor ordering.
- Account/period aggregation reads debit and credit from one grouped source;
  monthly bounds complement the exact UTC date bounds.
- Cash Flow reduces repeated movement scans and retains classification and
  fiscal-year closing exclusions.
- Eligible complete additive decimal pivots reuse row and column aggregates.
  Partial pages, continuations, overlapping groups and non-additive measures keep
  the general calculation. Overflow or possible decimal precision loss also falls
  back to SQL.

Public signatures, DTOs, report definitions, authorization, continuation tokens and
queue settings are unchanged. No migration is introduced by these optimizations.
Skills and the reference application's business logic therefore need no redesign.

The original [implementation validation](../../../performance-tests/artifacts/reports/2026-11/platform-mixed-capacity-20261111T001817Z-c1be8ece/sql-implementation/validation.md)
records 49 added cases, 2,804 passing unit tests and 357 passing integration tests
on each of PostgreSQL 16 and 18.3. Measured changed-code coverage is 147/147 lines
and 76/76 branches. These tests cover old/new SQL equivalence, cursor exchange,
filters, support IDs, totals, decimal boundaries and Cash Flow date boundaries.
Two implementation files were subsequently reformatted; the saved source manifest
describes the original tested bytes rather than the current formatting.

## Package and consumer verification

Before this refresh, the Runtime and PostgreSql contract snapshots identified
8 November archives, predating the SQL optimizations. `ai-devkit/prepare.mjs`
compares canonical and generated Kit files; its `changed: 0` result cannot detect
an SDK implementation omitted from those archives.

The refresh uses the repository packaging and contract-extraction tools, pinned
Node 24.19.0 and .NET SDK 10.0.401. It regenerates affected package references,
starter lockfiles, template payload and CLI from the rebuilt packages. Public
contract hashes are compared separately from assembly and archive hashes, so
implementation-only changes do not invent new capabilities.

The regenerated inventory retains 1,716 public types, 10 UI entry points and 224
capabilities. Thirty type fingerprints change in Runtime/PostgreSql while their
type attributes, method names and field names remain unchanged. The inspector
hashes raw metadata signatures, which include assembly-local type references;
those fingerprints are not semantic API compatibility results. Classifications
and capability bindings are refreshed from the extracted bytes, with ApiCompat
results recorded separately in the execution report.

An additional strict comparison against the previous 3.4.0 Runtime and PostgreSql
archives passes without API changes. That check exposed an ApiCompat invocation
issue: its reference argument requires the full `TargetFrameworkMoniker`, not the
short `net10.0` folder name. The runner now reads the moniker from MSBuild and
rejects unresolved-reference and CP/PKV diagnostics on both output streams,
including a tool exit code of zero. Process tests exercise this failure path;
all 54 published-baseline comparisons are rerun with resolved dependencies.

The final candidate is checked with the `artifacts`, `contracts` and `application`
profiles. The application profile creates an isolated starter through the shipped
CLI and runs its external-application profile, including load checks. It separately
restores, builds and tests `equipment-inspection` against the candidate feed,
including backend, frontend, browser, coverage and capability evidence. These
checks do not use platform project references or a deployed business database.

The initial refreshed candidate passed 157 tooling tests, all 224 capability
checks and both applications. Starter passed 13 unit, six integration, seven
frontend and two browser tests, plus three load repetitions. The independent
example passed 102 unit, 246 integration, 15 frontend and ten browser tests.
Required backend/frontend coverage was 100% in both applications. The final
candidate repeats the three profiles after the ApiCompat correction and this
documentation update; its actual receipts are linked above.

## September 320-VU finding: closed by owner decision

On 11 November 2026, the owner explicitly instructed: "Считай это закрытым!"
The performance follow-up is therefore **closed by owner decision**, with the
observed workload limit accepted for this work. It is not an outstanding action
in this Kit refresh and does not require another load run here.

The [original control analysis](../../../performance-tests/artifacts/reports/2026-11/platform-contention-20261111T030633Z-fd4e3b48/analysis.md)
and raw measurements remain unchanged: k6 exit 99, 29,515 HTTP 429 responses,
3.10% HTTP errors against a less-than-1% threshold. Queue-full rejection accounted
for 29,294 responses and queue-wait expiry for 221. All 2,948 document posting
requests succeeded. The decision closes the finding; it does not turn the raw
run into a threshold pass or establish unrestricted capacity. No queue limits,
test thresholds or automated result files are changed to implement this decision.

## Other quality evidence and scope

The existing full-platform coverage reports show:

| Report | Lines | Branches | Methods/functions | Statements |
| --- | ---: | ---: | ---: | ---: |
| Backend | 94,537/94,537 | 27,190/27,190 | 13,078/13,078 | Not applicable |
| Frontend | 17,278/17,278 | 16,846/16,846 | 5,754/5,754 | 20,495/20,495 |

All 1,548 required backend files and 343 required frontend files are present.
These are existing local coverage results, not newly executed or imported as
candidate-bound full-platform certification by this refresh.

The separate [Ollama repeat](../../../artifacts/coverage/backend-completion-2026-11-11/ollama-rerun-1/README.md)
passed 39 of 44 native-model cases, failed five and skipped none. That result is
unchanged by package synchronization or the performance finding's closure.
The current task does not rerun native-model evaluations, full-platform coverage,
persisted upgrade profiles or real-agent evaluations, and does not publish packages.
Earlier evidence for those stages is not relabelled as evidence for this candidate.
