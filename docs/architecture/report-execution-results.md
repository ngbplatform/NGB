---
title: "Complete Saved Report Execution"
---

# Complete Saved Report Execution

Every report exposed through the platform report API uses durable execution. Input cardinality and browser page budgets do not truncate the result. The same path serves platform accounting reports and the PM, Trade, Agency Billing and CRM verticals.

## Execution and consistency

The request resolves variants, validates the layout and expands dimension filters in the authenticated user's scope. The prepared input is persisted before returning HTTP 202. A host registered with `AddNgbReportExecutionWorker()` claims queued work with `FOR UPDATE SKIP LOCKED`.

`ITrialBalanceAccountSummaryReader` returns one row per account, in stable account-type/code/ID order. Its PostgreSQL implementation applies dimension filters before aggregation. Closed-period selection, opening balances, range turnovers and account labels are evaluated in one statement and therefore share one PostgreSQL statement snapshot. The snapshot is taken when the worker executes the query. `ITrialBalanceSnapshotReader` retains its distinct account/dimension contract for detailed consumers.

Runtime formats groups, subtotals, detail rows and the grand total while consuming the source stream. It writes batches of 256 rows using a separate connection. A result becomes `Ready` only after every row has been stored. Pages and exports then use those immutable rows; they never recalculate balances between pages.

Composable reports use `IStreamingReportDataSource`. PostgreSQL performs filtering and aggregation and exposes server cursors fetched in batches of 500. Runtime renders groups and pivot rows incrementally; it retains only the open hierarchy and one pivot row. Subtotals, pivot totals and grand totals are separate SQL aggregates at the required grain, so averages and distinct counts are calculated over original observations. Hidden drilldown IDs do not split the selected aggregation grain; ambiguous combined labels do not open an arbitrary record.

Balance Sheet, Income Statement and Statement of Changes in Equity consume account summaries. Statement opening and closing amounts use independent month-end endpoints and respect authoritative closed-period snapshots; P&L range turnovers are kept separate. Integrity Checks streams account/dimension batches and enriches issues in bounded batches. Journals and canonical vertical reports follow their native continuation cursors, emitting global footer totals only once. Cash Flow and dashboard reports retain their domain calculations and explicit Top-N sections; these are report semantics, not truncation of a requested detail result.

All source queries for one attempt share a `REPEATABLE READ, READ ONLY` transaction. Source paging, metadata, separate total queries and dimension enrichment therefore see one database snapshot. A retry starts a new snapshot; only its complete result can be published. Vertical executors resolve time-dependent paging defaults once through `PrepareExecution`, without introducing vertical report codes into platform infrastructure.

## Queue lifecycle

States are `Queued`, `Running`, `Ready`, `Failed`, and `Cancelled`. A worker processes one run at a time. Additional API or dedicated worker hosts share the queue safely. Workers renew a 30-second lease every five seconds. An attempt token fences writes and completion after another worker recovers an expired lease. Host shutdown abandons an attempt for recovery. Transient database/time-out failures also retry after lease expiry. Three exhausted attempts mark a run failed. Validation, missing-data and business-conflict errors retain their safe error code, message and field errors across the queue boundary. Infrastructure failures expose a generic message; stack traces remain in server logs.

Cancellation changes queue state immediately. The worker observes it through lease renewal and cancels its source query. Partial and superseded attempts are never readable as report results. Completed results expire after 24 hours; queue maintenance removes expired runs and stale partial rows in batches. Migration `V2026_09_12_0100__ngb_platform_report_runs.sql` must be applied before deploying the new hosts.

## API and UI

The shared definition enricher sets `supportsSavedExecution` for every registered report and removes rendered row/cell budgets from that execution path. Layout depth and column limits remain enforced. Internal synchronous `IReportEngine` and snapshot readers retain their bounded compatibility contracts; application clients use `IReportRunService` for complete results.

PM, Trade, Agency Billing and CRM API hosts register the worker and controller dependencies. CRM remains a package consumer: verify it against freshly built platform NuGet packages using `packaging/nuget/pack-platform.sh`. Build the unpublished UI candidate with `npm run pack:platform-ui -- --local-candidate`; the published CRM lockfile remains unchanged. Normal release versioning and publication remain separate from local package verification.

- `POST /api/reports/{code}/runs` starts a run and returns 202 plus its status URL.
- `GET /api/reports/{code}/runs/{id}/status` reads progress.
- `GET /api/reports/{code}/runs/{id}?offset=…&limit=…` reads up to 500 persisted rows through an indexed ordinal range. There is no maximum result offset.
- `DELETE /api/reports/{code}/runs/{id}` cancels pending/running work.
- `POST /api/reports/{code}/runs/{id}/export/xlsx` exports that exact result.

Every endpoint rechecks report permissions; results are also scoped by authenticated subject and report code. Drilldown actions are scrubbed against current permissions. Cross-user or expired result IDs return 404. Legacy `/execute` uses the same saved result and returns real pages; its continuation validates that the original parameters have not changed. Page sizes and totals count rendered rows, including group headers and footer totals. Clients that previously relied on a canonical report ignoring Offset/Limit must follow `HasMore`/`NextCursor` or use the saved-result endpoints.

The UI polls with cancellation, shows preparation state and retains only the current page for saved reports. Previous/Next navigation remains available beyond the former 2,000-row browser budget. Failed execution is distinct from an empty result. Download exports the displayed saved result.

## Export

XLSX rows are written sequentially from the saved result to a temporary file that is deleted on close. The HTTP response starts after the ZIP has completed, so a failed export cannot look like a successful truncated workbook. Each Excel worksheet holds at most 1,048,576 rows including headers; larger results continue on another sheet with repeated headers. Numeric cells remain numeric and user text remains text, including strings beginning with `=`. Pivot headers and horizontal merged footer cells are supported. Merge references are spooled to disk as well, so their count does not grow an in-memory list. Export reuses the persisted result for every report.

## Verification and references

For an existing Docker installation, rebuild the migrator, API and web images from the same revision. Run the normal platform/vertical migration step first, then replace the API and web containers. No ledger rebuild, demo-data reseed or business-data repair is required by this change. The migration adds the two report execution tables and their indexes; it does not rewrite accounting data.

Platform integration tests cover over 10,000 dimension sets in all three opening-balance paths, filtered aggregates, over 10,000 output accounts, complete pages/exports, immutable results, ownership, attempt fencing, cancellation, failures and expiration. Additional tests cover financial statement parity across closed periods, contra/inactive accounts, over 10,000 journal rows, 12,001 flat/grouped/pivot rows, weighted and distinct aggregates, measure sorting, duplicate business labels, and source mutations between pages. PM covers an authenticated 10,001-building report and complete export; Trade, Agency Billing and the packaged CRM consumer verify their real report datasets. UI tests cover progress, cancellation, paging and useful failure messages.

The design follows PostgreSQL's [statement snapshot semantics](https://www.postgresql.org/docs/16/transaction-iso.html), Microsoft's [asynchronous request/reply pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/async-request-reply), and ASP.NET Core's [guidance on bounded batches and large results](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/best-practices?view=aspnetcore-10.0).
