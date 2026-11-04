# Reporting, datasets, paging and exports

Target: NGB 3.4.0. Public contracts live in
[application services](contracts/NGB.Platform.Application.Abstractions.json),
[Contracts](contracts/NGB.Platform.Contracts.json),
[Runtime](contracts/NGB.Platform.Runtime.json) and
[Persistence](contracts/NGB.Platform.Persistence.json).

Choose between a canonical report's supported extension and a composable dataset.
Use `IReportDefinitionSource` for report definitions and
`IReportDefinitionEnricher` for static changes to those definitions. Read the
assembled catalog through `IReportDefinitionProvider`.
`IReportDatasetSource.GetDatasets()` supplies dataset metadata; metadata alone
does not implement bounded data execution. `IReportMetadataContributor` is a
separate public declaration that the current standard catalog does not consume.
Implementing it alone does not change `/api/report-definitions`; use the active
definition-enricher composition described below.

`IReportPageDataSource.ReadPageAsync(ReportDataQuery, ReportPlanPaging,
ReportRowSelection?, CancellationToken)` supplies provider-neutral bounded reads
at the requested aggregation grain. A row selection is a bounded union of keys;
apply it with one set-based query, not one query per selected row. Keep SQL in the
provider project, use parameterized inputs and enforce supported filter/sort fields.

`IReportSpecializedPlanExecutor` identifies its ReportCode and participates in the
shared planning/rendering path. Its `PrepareExecution` resolves time-dependent
defaults once per attempt; `ExecuteAsync` returns a `ReportDataPage`. It is not
permission to bypass report admission, authorization or paging limits. For
streaming and download paths, inspect `IStreamingReportDataSource`,
`IStreamingReportExportService` and `IReportDownloadService` separately.

Use `IAuthorizedReportReadService` for current-user integrations. Report variants
also have their own access-context contract; UI hiding alone is insufficient.
In an HTTP host register `IReportVariantAccessContext` with the public
`HttpReportVariantAccessContext` after Runtime registration. The Runtime default
has no actor, so enabling `AllowsVariants` in metadata alone cannot save a private
variant. The HTTP adapter uses the real current actor and the SDK projects the
owner into platform users when needed, including an unlinked Keycloak administrator.
Do not substitute a fixed actor or use an editable role display name.

`ReportControllerBase` supplies `/api/reports/{reportCode}/variants`. Private
variants are scoped to their owner; identical codes may belong to different users.
Shared variants require `ManageSharedVariants` to change. Saving one's own variant
uses `SavePrivateVariant`; deletion uses `DeleteVariant` or management permission.
Read operations still require report access. A default is unique within its private
owner or shared scope. Do not use the same code for private and shared variants:
that collision is rejected. Route identity takes precedence over body identity.
Use `ReportVariantLimits` for name, code and payload limits. Saving JSON does not
prove every requested layout/filter is executable; execution validates the saved
request against the report definition. Test that execution separately.
Use public shared UI components/exports for rendering and drilldown rather than
private framework source imports.

Freeze expected rows, totals and group subtotals before implementing the query.
Test empty data, multiple pages, boundary keys, tied sort values, invalid/stale
cursors, unauthorized dimensions, changed snapshots and export cancellation.
Reconcile total counts across paging and export; verify that filters applied to
visible rows also constrain totals. Measure actual query count and bounded memory.

## Parameters and authorization test setup

For the standard PostgreSQL composable path, supply `from_utc`, `to_utc` and
`as_of_utc` as strict ISO `yyyy-MM-dd` dates. The provider converts inclusive
upper dates to exclusive next-day bounds. Upper dates must therefore be earlier
than `9999-12-31`; a lower date can use the entire supported date range.
Malformed or unrepresentable upper dates produce `report.layout.invalid`, a
`parameters.<code>` field path and HTTP 400. This validation applies to ordinary,
paged and streaming execution. A parameter marked `IsRequired` must be present.
Other date parameters still need validation in their executor; declaring date
metadata alone does not establish it. Actual SDK configuration failures retain
their original error classification.

Use a distinct test identity for an ungranted reader, a display-name-only
administrator and a registered application administrator. Link roles before each
identity's first request. The example's `InspectionHost.LinkUserAsync` performs
initial repository setup; repeating it after a request does not implement the
public role-change workflow or invalidate cached permissions. For intentional
role changes, use the public security API as demonstrated by
`SecurityAdministrationHttpTests`. Keep the inactive-account assertion after
the successful administrator request. See [verification guidance](testing.md).

`InspectionReportTests` demonstrates these independent actors and separately
verifies malformed-date HTTP 400 responses and parameter field paths. Parameterize negative HTTP cases or
include the request identity in assertions so a failure identifies its input.

## Composable example

The [equipment inspection example](../examples/equipment-inspection/README.md#inspection-summary)
supplies a composable report through `IReportDefinitionSource` and
`IReportDatasetSource`. Its provider uses `IPostgresReportDatasetSource` and
`PostgresReportDatasetBinding`, leaving paging and XLSX streaming to the shared
engine. Use `Count` for a record count, including an empty selection; `Sum` has
different empty-set semantics. The example verifies grouping, filters, cursors,
exported values and authorization. It does not establish support for every report
shape, pivots or workload performance. `ReportVariantHttpTests` checks two owners,
shared mutation denial, independent private codes, default replacement, route
identity, unchanged data after invalid writes and execution of a saved filter.

`ReportStreamingTests` reads 501 actual Posted inspections through
`IStreamingReportDataSource` in a real `IReportReadSession`. For this SDK version
the provider returns batches of at most 500 rows. A write from another HTTP
request after the first fetch is invisible to the active snapshot and visible to
the next session. Cancellation after the first batch is followed by
`EndAsync(CancellationToken.None)` in `finally`: cleanup must not reuse the cancelled
token. The test checks that no transaction remains active and that the same scoped
provider can read again. An empty selection also completes. These are trusted
provider contracts, not authorized public HTTP endpoints. The interactive report
and export boundaries retain their own permission checks. This proves bounded
fetching and cancellation for this query, not a measured process-wide memory limit.

For a fixed report shape, `InspectionOutcomeReportDefinition` and
`InspectionOutcomeReportExecutor` demonstrate `IReportSpecializedPlanExecutor`.
The canonical `example.inspection-outcome-rate` report returns one prebuilt row:
total Posted inspections, those with the exact result `Passed`, and their percentage.
Its two provider aggregates each return one row inside the shared report read
session. The executor performs arithmetic on those scalar counts, not in-memory
aggregation of documents. Empty input returns zero counts and an undefined
percentage, displayed as `No inspections`.

Register the definition source and scoped specialized executor through the public
interfaces. Keep the default layout consistent with declared capabilities:
`ReportLayoutDto` enables totals by default, so a report that disables that option
must specify it explicitly. This fixed shape does not advertise variants, filters,
grouping or XLSX export. HTTP tests verify 3/5 = 60%, Draft exclusion, period
selection, empty input, shared request validation, both administrator forms,
denied readers, inactive accounts and cancellation. A specialized executor remains
behind `IAuthorizedReportReadService`; calling it directly is trusted service code.

For the four built-in financial statements, follow the
[accounting workflow](accounting.md#built-in-financial-statements). Their canonical
root pages can return group headers and grand totals; read account children with
the returned `ChildrenPath` as the next request's `GroupPath`. A group header is
not evidence that its account detail has already been materialized in that page.

The [built-in ledger workflow](accounting.md#built-in-ledger-and-diagnostic-reports)
also distinguishes grouped Ledger Analysis from its flat cursor mode. Default
account headers have empty measure cells when subtotals use separate rows.
Use row kinds and column codes to locate values. Trial Balance's Debit/Credit
columns report turnover, while Account Card and General Ledger expose running
balances for the selected account.


## Trusted persistence contracts behind built-in reports

Built-in reports use specialized page, stream and snapshot ports in
`NGB.Persistence.Readers.Reports`. Their public signatures describe provider data,
not an authorized interactive endpoint. Keep HTTP/MCP use behind
`IAuthorizedReportReadService`, and retain the report read session when consuming
streams. A snapshot DTO describes its aggregated values and projection provenance;
it does not by itself open or hold a transaction.

The canonical Account Card consumes the effective page/stream readers, including
its opening balance and range totals. The separate raw Account Card ports have
different reduction semantics and are not interchangeable with that path.
General Ledger snapshots carry opening/range totals; Trial Balance has distinct
account-only and account/dimension row grains. Shared `AccountingSummary` pages
carry section/account values and explicit keyset continuation for Trial Balance,
Balance Sheet, Income Statement and Equity. Consistency pages/streams carry
read-only comparison results and diagnostics, not storage repair commands.

These supporting contracts are checked against the exact packaged signatures and
the existing report pipeline. The independent report scenarios verify their
public business results. That evidence does not certify a replacement database
provider, every low-level query shape or unbounded materialization. Use explicit
pages/streams and the existing bounded readers for application-specific work.

## Definition enrichment and execution boundaries

Register `IReportDefinitionEnricher` contributors when an application needs to
adjust an existing report's static presentation or filter metadata. The standard
catalog applies them in registration order while assembling its definitions.
Return unrelated definitions unchanged and preserve `ReportCode`; replacing that
identity fails catalog construction. Keep enrichers independent of the current
request, actor and mutable per-request state. `ReportDefinitionEnrichmentTests`
registers two contributors, checks their composed HTTP metadata and unaffected
reports, and verifies that reader authorization still applies.

The public report contracts serve different boundaries:

| Contract family | Responsibility |
| --- | --- |
| `IReportDefinitionSource`, `IReportDatasetSource`, `IReportDefinitionEnricher` | Application-owned definition contributions. |
| `IAuthorizedReportReadService`, concrete authenticated `ReportControllerBase` subclass | User-facing definition and execution access. The controller also checks export and variant mutation permissions. |
| `IReportEngine`, `IReportPlanExecutor`, `ITabularReportPlanExecutor` | Trusted execution and dispatch after the appropriate access boundary. |
| `Application.Abstractions.Services.ReportPlan*`, `ReportDataColumn`, `ReportExecutionResult` | Provider-neutral query and result shapes. |
| `Runtime.Reporting.Planning.ReportPlan*`, `ReportQueryPlan`, normalized runtime models | Intermediate planning and rendering representations, produced by the standard engine. |
| `PostgresReportExecution*`, selection records, SQL statement/cursor metadata | Persistence-owned query compilation and results. SQL and aliases stay server-owned. |
| `IReportVariantRepository`, `ReportVariantRecord` | Trusted variant persistence; a caller-supplied user ID does not establish authentication. |
| `IReportDownload`, `IStreamingReportExportService` | Single-use download lifetime and forward-only worksheet output. Dispose the download even after cancellation. |

Prefer the existing definition, dataset and specialized-executor interfaces when
extending reports. Similar type names across runtime and provider namespaces do
not make their models interchangeable. These contract references do not certify
an alternative provider or every possible grouping/pivot shape.

## Per-host admission and continuation keys

`AddInfrastructure` binds `Reporting:Requests`. Configure page and XLSX download
capacity independently through `ConcurrentPages`, `QueuedPages`,
`ConcurrentDownloads` and `QueuedDownloads`. The corresponding queue deadlines
are `PageQueueTimeoutSeconds` and `DownloadQueueTimeoutSeconds`; execution
deadlines are `PageTimeoutSeconds` and `DownloadTimeoutSeconds`. Concurrency and
deadlines must be positive; queues may be zero. Startup validation rejects
negative queues and timeouts beyond the cancellation timer range.

The standard report controller applies the shared `ReportRequestBudget`. A busy
capacity/queue returns HTTP 429 with `Retry-After`; releasing the execution lease
admits later requests. Other transports can use `IReportRequestAdmission` for
the same page capacity, dispose the acquired lease and retain their authorized
report service. Do not acquire a second lease around an HTTP controller action
that already has the budget filter. Limits apply to each running host, not to an
entire deployment or a particular user's business permissions.

`ReportAdmissionTests` exercises real authenticated page and XLSX endpoints while
each capacity is occupied, verifies independent capacity and recovery, and checks
queued cancellation without sleeps. It does not establish workload throughput or
exercise execution-deadline expiry.

Set `Reporting:Cursor:SigningKey` through the application's secret configuration
to Base64 of at least 32 cryptographically random bytes. Instances that should
accept each other's continuations need the same key. Without this setting the
process creates an ephemeral key, so another process or a restart cannot verify
its continuations. Replacing the key invalidates existing continuations; users
must run the report again. This HMAC protects integrity, not confidentiality, and
does not replace report authorization or query-fingerprint validation.

The example verifies configuration binding, a second protector with the same
key and rejection with a different key. Existing HTTP paging checks verify
tampered and changed-query continuation rejection. Together they validate these
contracts, not a deployed multi-instance topology or an automated rotation scheme.

## Explicit dimension scope expansion

An `IReportDimensionScopeExpander` can expand an explicitly requested dimension
selection using application-owned relationships. Register it through standard DI
and declare `SupportsIncludeDescendants: true` in the report's filter/field
metadata. The lookup metadata must identify the registered catalog dimension or
a single document dimension. The flag alone does not create hierarchy semantics;
a registered contributor alone does not enable descendant requests.

`IDimensionDefinitionReader` resolves active registered dimension codes to IDs.
`IDimensionScopeExpansionService` composes contributors in registration order;
each receives the previous result. Select the intended report and dimension,
preserve unrelated scopes, propagate cancellation and bound relationship lookups.
Null and empty input scopes bypass contributors. Values within one dimension
form an OR selection, while multiple dimensions form an AND selection.

The standard report adapter requests expansion for explicit IncludeDescendants
filters, converts the result back into value filters and clears that flag for
execution. It rejects more than 5000 expanded values. Do not use this post-expansion
check as permission to perform an unbounded traversal inside your contributor.
Continue through the authorized report service; the composition service itself
is trusted server code and performs no user access check.

`ReportDimensionScopeTests` uses an explicit four-equipment fixture: two ordered
contributors expand one selected equipment to its child and grandchild, producing
three posted inspections while excluding the fourth. A direct selection returns
one, an unrelated selection remains unchanged, and a denied reader never invokes
the contributors. Other cases verify the metadata opt-in, cancellation and the
expanded-value limit with subsequent recovery. This establishes extension wiring
and bounded HTTP behavior, not a built-in catalog hierarchy or workload throughput.

## Built-in executor implementation boundaries

Canonical helpers parse ISO dates, normalize accounting periods, validate bounded
GUID selections and reserve page space for summary rows. Materialized execution
keeps an overflow probe so an overlarge result fails instead of silently returning
a partial report. These helpers do not replace report authorization, admission
limits or registered dimension expansion.

Accounting summary and consistency page executors use internal continuation
positions; specialized cursor encoding alone is not a signature. The report
service owns the protected, request-bound cursor envelope. Built-in financial
statement streaming executors serialize trusted prepared execution state and
render ordered accounts into details, sections and totals. Prepared JSON is not
a user permission token. Standard export authorization and resource limits remain
required. Account-card cell actions describe navigation; the destination still
checks access when opened.

## Custom streaming export executor

Register one scoped `IStreamingReportExecutor` for an exact report code to
specialize XLSX export; the `"*"` executor remains the standard fallback. Duplicate
matching executors are invalid. `Prepare` receives the resolved variant/filter
request and must freeze required inputs. `ReadAsync` runs in the report read
session and must honor cancellation. Emit each row exactly once with consecutive
zero-based ordinals. `Template` must be available after the first read, including
an empty stream. Do not retain a requesting user's mutable state in a singleton.

The HTTP report controller performs authorization and export capability checks;
calling the low-level download service directly is trusted orchestration. Keep
`IReportDownload` disposed after success, failure or cancellation so its read
session and transaction are released. It can be written only once. Errors before
the first row can fail before response headers; later streaming errors cannot be
reported as a complete successful download.

`ReportStreamingExtensionTests` uses synthetic rows to verify exact executor
selection, refusal before preparation, failure before the first row, recovery,
frozen parameters, single-write behavior and cleanup after cancellation. It tests
the extension lifecycle, not new business calculations or export throughput.
