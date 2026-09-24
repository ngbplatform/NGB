---
title: "Report Browsing and Direct Downloads"
---

# Report Browsing and Direct Downloads

All report API hosts use the same request contracts. There is no report-run queue or PostgreSQL table of rendered report rows. Saved report variants contain configuration only.

## Responsibilities

- API: permissions, HTTP status/headers, cancellation, admission limits and download response streaming.
- Application abstractions: report definitions, execution, bounded page data source and a disposable prepared download.
- Runtime: validate and resolve variants/filters, plan queries, render a page or streamed rows, format XLSX, authenticate continuation state.
- Persistence/PostgreSQL: set-based predicates/aggregates, deterministic keyset ordering, bounded page queries and read-only transactions. Runtime and API do not construct SQL.

## Interactive execution

`POST /api/reports/{reportCode}/execute` returns the requested page directly. The public endpoint rejects `disablePaging=true` and nonzero offsets; clients follow the opaque `nextCursor`. `total` may be null. Clients must stop based on `hasMore`, not an assumed row count. A server request does not create an export, queue a job, or write report rows.

Composable reports page the next row-group level. `childrenPath` on a group row becomes `groupPath` in a new execution request. The UI expands and collapses groups in the same table, inserting the requested children below their parent without route navigation. Root and branch pages have the same column schema; pivot column keys and header actions use the root filter scope so values stay under the same headers at every depth. Each expanded branch has its own continuation cursor. Parent aggregates remain visible; branch grand totals are not inserted again. Pivot reports select row-axis keys first and fetch all cells for those keys in one set-based query. Column cardinality is bounded. Global and group aggregates use the original observations, including average, distinct count, minimum and maximum; averages are never summed. Global footers appear on the final page.

`ReportRowSelectionLimits` defines the shared pivot key budget: at most `PagingLimits.MaxPageSize` keys, with at most `ReportLayoutLimits.MaxTotalFilterValues` scalar values across those keys. Runtime reduces page size before querying when keys are wide; the PostgreSQL provider validates the same contract. Key width follows the layout limits, rather than a separate smaller field cap. PostgreSQL-specific guards remain in the provider: a 1 MiB application budget for generated SQL text and the protocol ceiling for bind parameters. Continuation predicates factor shared prefixes so their SQL text grows linearly with key width and handle null keys without untyped null parameters.

Accounting summaries return bounded section aggregates and keyset pages of the selected section's accounts. Journals, Account Card, General Ledger, operational queues and vertical canonical reports use their specialized page readers. Accounting Consistency selects anomalous account/dimension keys in SQL and enriches each page in batches.

Account Card and General Ledger browse bounded raw candidate keys before document-level normalization and aggregate expansion. Full-document reversal/group semantics are preserved even across dates and dimensions. At most four candidate batches are attempted before one set-based fallback for dense cancellations; filtering cannot create an unbounded loop of database round trips. Each new page recomputes its current prefix balance. Maintenance Queue applies limits to disjoint date/request/work-order ranges before display joins; this also avoids poor tuple-range estimates on heavily repeated dates. Occupancy selects building keys before computing occupancy, and computes the global total only when needed.

Ledger Analysis can preaggregate by account before labels only when its requested grain, filters and additive measures permit it; finer layouts retain the original dataset. A complete first page of disjoint SUM groups supplies its own total. Partial pages, pivots and non-additive measures use source-level totals. Cash Flow aggregates by semantic role in SQL; unclassified observations retain an exact count with at most 50 diagnostic examples. Fiscal-year closing transfers do not cancel the requested period's profit-and-loss movements.

The UI virtualizes rows and retains a sliding window of up to 2,000 rows and about 8 MiB of estimated serialized page data (a single server page is the minimum window). This is a retention budget, not a bound on the browser's total heap. Earlier row data is discarded; lightweight cursor bookmarks permit backward reload. Bookmarks are bounded to 128 entries and approximately 512 KiB; older bookmarks are discarded. The UI offers Back to beginning when earlier history has been evicted. This is live browsing, not an immutable report snapshot across HTTP requests. Re-running or exporting evaluates current source data. Account Card and General Ledger calculate current prefix balances in SQL on each new page session. Embedded balances can be reused only inside the identical read snapshot (for example, successive export batches). Rows already displayed are not pushed updates; re-run to refresh them after source changes.

Inline group state is shared by all reports, without a report-code registry in the UI. Child requests use the captured executed layout and filters, fetch up to 100 rows at a time, and have a maximum of two concurrent requests. Only an explicit expansion or a visible continuation marker requests a branch page. Branch windows retain up to 500 rows each, subject to an aggregate budget of 2,000 rows, 8 MiB of estimated serialized data and 64 branch states, in addition to the root window. Each branch retains at most 32 backward bookmarks. Least recently used branches are evicted, preferring collapsed branches; ancestors of the current request are protected. Evicted branches can be expanded again. A page that cannot fit the branch budget produces a local error instead of growing the cache. Errors and retry controls stay within their group. Collapsing cancels pending descendant reads; rerunning, changing reports or unmounting invalidates pending work. Late responses cannot replace another report's data. Branches whose parents leave the root window are released. Already materialized canonical outlines expand locally without a network request.

The flattened visible tree uses the same virtualized table as flat reports. Stable row identities preserve height measurements and scroll anchors when inserting children. Session snapshots contain only bounded root pages, not expanded branch data; their keys are scoped to the executed report context.

`ReportQueryService` resolves the report definition and variant and validates the continuation before opening a read session. Report execution, enrichment and any final-footer refresh then share one repeatable-read, read-only transaction. Metadata and variant reads performed before that session are outside its snapshot. The transaction ends before the result returns to the controller; no transaction remains open while the user reads the page.

Continuations authenticate their full payload and bind report definition, resolved layout, filters, parameters and group path. Configure `Reporting:Cursor:SigningKey` with base64 of at least 32 random bytes and share it across API replicas. Without configuration a process-local key is generated; process restarts invalidate existing cursors, and different replicas cannot validate each other's continuations. Keys are secrets and must not be checked into source control.

Account Card and General Ledger encode their inner continuations with the shared versioned envelope, a distinct report kind and required named fields. Totals form one optional object; the read-session identifier is independent of their presence. Decoders accept only this format and reject unsupported versions and missing fields. The envelope is serialization; the outer continuation protector supplies authentication.

## Export

`POST /api/reports/{reportCode}/export/xlsx` validates the request and establishes the first row/template before sending the HTTP response. `ReportDownloadService` resolves the definition, variant and filter scope before opening the export read session. All subsequent report-row reads share one read-only repeatable-read transaction until the download is disposed. The XLSX is written to the response as rows arrive. Account Card, General Ledger and Maintenance Queue declare their main SQL once and fetch batches of 500 through transaction-scoped `NO SCROLL` cursors. They do not rerun the full selection for each batch. Document and dimension labels are resolved in batches. Disconnects, explicit cancellation and the export deadline cancel source reads and release the transaction. An error after headers aborts the download.

XLSX rows and ZIP output are streamed with bounded buffers. Worksheets split at Excel's row limit. XLSX merge references are spooled to a temporary file with `DeleteOnClose` and owner-only permissions on Unix, because merge metadata follows sheet data in the format; it is disposed within the request. No completed workbook is retained on the server and no file store or cleanup job is required.

The report page uses File System Access when available to pipe the response to the chosen file with backpressure. Otherwise it submits a native same-origin POST to `/api/reports/{reportCode}/export/xlsx/form`; the browser download manager owns the response, so the report page does not create a full-file JavaScript Blob. The short-lived form contains the bearer token and serialized request in its URL-encoded body, never in the URL, and is removed immediately. Only this marked endpoint accepts body tokens; normal JWT validation, permissions and download admission limits still apply. Production transport requires HTTPS. The adapter retains at most one hidden iframe; the browser manages progress and cancellation after handoff, because native attachment completion is not observable reliably from page JavaScript. The reusable `exportReportXlsx` helper still supports a Blob response when called without a streaming destination; this is not the report page's fallback path.

Downloads emit `X-Accel-Buffering: no` so nginx forwards chunks with backpressure instead of buffering them into temporary response files. Other ingress products must likewise allow streaming. Align proxy idle timeouts with the longest permitted gap between chunks.

API admission settings live under `Reporting:Requests`: `ConcurrentPages` (12), `ConcurrentDownloads` (2), `PageTimeoutSeconds` (30), and `DownloadTimeoutSeconds` (300). Limits are per instance. Excess requests receive HTTP 429 with Retry-After; they do not queue database connections. Tune replica counts, these limits and database pool capacity together. Full exports inherently read all matching observations and can hold an MVCC snapshot until completion; keep the deadline appropriate for the deployment.

The native-download adapter currently removes its iframe after 310 seconds, matching the default five-minute server budget with a margin. Increasing the server download deadline also requires aligning this client cleanup delay.

## Deployment

Deploy matching 3.0 API and UI packages and run the committed platform/vertical migration packs.
The current repository does not contain a stored-report-results cleanup migration or report-run
tables/endpoints. Upgrading from the `v2.0.0` release uses the migration history already in the packs;
do not reset that history. Clients that used nonzero offsets or disabled paging must switch to
continuation cursors and the download endpoint as described in the
[3.0 migration guide](../guides/migrating-to-3.0.md).

CRM consumes the platform through NuGet and npm packages. Validate platform changes against freshly packed packages while preserving that consumer boundary.

## Regression coverage and performance validation

Runtime and PM integration tests cover complete paging and XLSX exports, dense reversals, ledger groups across dates and dimensions, tied queue keys, current receivables totals and source changes during a repeatable-read export. The Trade, AgencyBilling and CRM `Reporting_ScaleTests` compare query counts for different page sizes and validate full export row counts. `ReportPerformanceProbe` and `ReportScaleAssertions` in `quality/integration/` are shared test helpers; they remain part of the automated tests.

UI tests cover inline and nested expansion, independent cursors, incremental loading, bounded retained pages and bookmarks, local retries, request concurrency, stale requests and cancellation across report navigation. `nativeDownload.browser.spec.ts` tests the production browser adapter with form submission mocked. The PM `Native_form_export_uses_the_same_permissions_validation_and_streaming_result` integration test checks the endpoint with signed tokens and validates its XLSX response. These tests cover separate layers; neither is a claim that the complete browser-to-server download flow ran against a live deployment.

Use the existing [performance testing workspace](../platform/performance-testing.md) for reporting regression and load scenarios. Bounded responses and fixed query counts do not bound the cost of each SQL query: full financial aggregates and exports still read the matching source range. Validate latency, throughput, cancellation and admission settings against the intended workload and database capacity.

Keep reusable test code and fixtures in Git. Generated traces, query plans, timings and test summaries belong in ignored `artifacts/` directories or CI artifacts. This document describes execution contracts and configuration, not results from a particular local run.
