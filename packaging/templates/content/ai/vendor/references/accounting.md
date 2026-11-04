# Accounting and financial periods

Target: NGB 3.4.0. Use [Accounting](contracts/NGB.Platform.Accounting.json),
[Definitions](contracts/NGB.Platform.Definitions.json),
[Runtime](contracts/NGB.Platform.Runtime.json) and
[Persistence](contracts/NGB.Platform.Persistence.json).

`IDocumentPostingHandler` is the accounting strategy for a document type.
Its `BuildEntriesAsync(DocumentRecord, IAccountingPostingContext,
CancellationToken)` runs inside the lifecycle transaction that changes document
status. Match TypeCode, use the context's supported entry APIs and keep the handler
deterministic. Do not commit independently or perform external side effects inside
the posting strategy.

`IChartOfAccountsProvider`, `IAccountByIdResolver` and
`IAccountingPostingValidator` are public accounting contracts. Confirm supported
account kinds, dimensions, currency and amount semantics before constructing an
entry. Test known debit/credit amounts and expected account balances with an oracle
independent of the handler implementation.

General Journal Entry has public application/UI services and Runtime facades.
Interactive operations must use their authorized application boundary. A trusted
system reversal runner is not a replacement for interactive permission checks.
Use existing closing/reversal operations and period validation; never rewrite
posted history or silently reopen a closed period to make an operation succeed.

Accounting, Operational Registers and Reference Registers are peers. A document
can combine supported handlers without making Accounting a mandatory parent engine.
Post with no handler is explicitly unsupported in this SDK; see
[known limitations](../known-limitations.json).

Verify transaction rollback, repeated idempotency keys, stale versions, closed
periods, reversal history and authorization. Report totals must be checked through
the real public report/read path against the same independently specified amounts.

The [equipment service example](../examples/equipment-inspection/README.md#equipment-service-and-combined-effects)
combines all three engines under one document lifecycle. Its trusted Setup uses
public account management and preserves custom account names. The handler posts
an equipment-dimensioned expense against a payable. Integration acceptance checks
125.50 on both sides, one-row journal pages, trial-balance reconciliation, repeated
requests and a fault after all three engines have written. Accounting storno swaps
debit/credit while retaining a positive amount; use the public reader's account
sides to compute net balances. Exchange rates and the complete accounting-report
surface require separate verification.

## Standard General Journal Entry

`general_journal_entry` is an existing platform document with its own typed
workflow. Do not register a replacement definition, copy its tables into an
application migration or route its edits through a generic document editor.
The public `GeneralJournalEntriesController` at
`/api/accounting/general-journal-entries` composes `IGeneralJournalEntryUiService`
for operations and `IAuthorizedGeneralJournalEntryReadService` for reads.
`AddControllersApi`, `AddNgbRuntime` and the PostgreSQL provider supply this
composition in the independent example. Use the resolved package's public
`NGB.Contracts.Accounting` request/result types.

The HTTP controller requires the existing document permissions for
`general_journal_entry`: create, edit-draft, post, unpost, view and deletion marks
as appropriate. Active registered application administrators and trusted Keycloak
administrators retain full access. The trusted facade, document service, reversal
runner and accounting readers are not permission-checking HTTP substitutes.

Create an incomplete Draft, then update its typed header and replace its line
collection through `/header` and `/lines`. Use active compatible accounts and
their required dimensions, discovered through `/accounts/{accountId}`. Supply the
required reason code before submission. Line replacement validates account and
dimension constraints before committing; debit and credit totals must balance
before Submit, Approve and Post. The typed API also returns the allocated number
and deterministic debit/credit allocations.

There are two distinct states: the document remains `Draft` while its header
progresses from `Draft` to `Submitted` and `Approved`. `/post` changes the document
status to `Posted`. Manual header and line edits require approval state `Draft`;
submission locks them. `/reject` records a rejection reason and retains a
`Rejected` header. This SDK does not expose an edit/resubmit transition from that
state. Do not invent a reset command or update the stored state directly.

`/reverse` creates a separate linked journal and can post it immediately. Its
lines exchange debit and credit sides, retaining the positive amount and matching
dimension values; the original document and entries remain unchanged. This is
different from appending a storno entry to the same document. Optional automatic
reversal creates a separate preapproved system Draft on the configured future
date. `IGeneralJournalEntrySystemReversalRunner.PostDueSystemReversalsAsync` posts
due documents with an explicit batch bound; invoke it from trusted scheduled
work. Repeating a completed batch does not add another posting.

`IGeneralJournalEntryFacade.CreateAndPostApprovedAsync` is a trusted atomic
composition of creation, validation, approval and posting. A missing required
header field or unbalanced journal must leave no partially created document.
It requires explicit actor arguments and does not create an authorized interactive
session. Applications must authorize before invoking trusted service operations.

Read lists and details through the dedicated typed endpoints. They support
bounded search/date filters and cursor continuation; preserve the same selection
when supplying the next cursor. `trash` accepts `active`, `deleted` or `all`.
The typed GJE header deliberately differs from generic document storage. The
independent acceptance reconciles actual accounting entries through the public
bounded `IAccountingEntryReader` in trusted test code, not the generic document
effects endpoint or raw SQL.

`GeneralJournalHttpTests` verifies both administrator forms, required dimensions,
invalid-line rollback, unbalanced submission refusal, header immutability,
approval/rejection, actual 125.50 allocations/entries, manual and automatic
reversal, read authorization, cursor filters and reversible deletion marks.
Its trusted-facade case verifies an atomic failed creation and a successful
balanced posting. These cases reuse platform behavior without altering it.

## Chart of Accounts administration

Compose an authorized application controller from `AdminControllerBase` and inject
`PermissionAwareAdminService`. The default `IAdminService` implementation performs
trusted operations; injecting it directly does not establish current-user access.
Keep the controller's `[Authorize]` and `[ApiController]` attributes. The secured
service requires `admin / chart_of_accounts / view` for metadata, pages, details
and ID lookups, and `manage` for mutations. Both administrator identities retain
implicit permissions. A view grant alone never authorizes account changes.

Use `/api/chart-of-accounts/metadata` to discover account types and supported
cash-flow roles/line combinations. Create and update through the public upsert DTO;
account creation, mutation and lifecycle operations retain business audit. Invalid
cash-flow combinations are refused. Trusted Setup may use account management
directly, but interactive API callers must use the permission-aware boundary.

The list endpoint supports search, account types, active/deletion filters and a
limit from 1 to 500. Follow `nextCursor` with the same filters; changing filters
invalidates it. This API also accepts nonnegative offsets. Do not infer a blanket
offset prohibition from the separate composable reporting contract. `/by-ids`
deduplicates IDs, ignores empty/unknown IDs and applies the SDK's lookup bound.

`set-active` changes availability. `mark-for-deletion` records a logical deletion
and deactivates the account; ordinary pages omit it. `unmark-for-deletion` restores
visibility while preserving the inactive state. Use an explicit activation if
needed. Accounts with movements retain the SDK's existing deletion and immutable
field restrictions; do not bypass them with SQL or recreate history.

The [administration example](../examples/equipment-inspection/README.md#chart-of-accounts-administration)
exercises public metadata, create/update, deduplicated lookups, audit, active and
deletion transitions, cursor/filter binding and both view/manage permission paths.
It uses disposable resources and does not change platform account rules.

## Built-in financial statements

Use the existing report controller and `IAuthorizedReportReadService` with
`AccountingReportCodes.BalanceSheet`, `IncomeStatement`, `CashFlowStatementIndirect`
and `StatementOfChangesInEquity`. They are canonical reports registered by the
runtime. Do not duplicate their accounting formulas or query application tables
to recreate statement balances.

Balance Sheet requires `as_of_utc`; the other three require `from_utc` and `to_utc`.
Inspect each public definition for its parameters, filters and limits. Balance
Sheet is a month-end statement: its date selects a reporting month. Income and
equity readers use month-start period bounds; cash-flow requests use their own
date-range contract. Do not assume every financial report has identical date or
dimension semantics. Typed request/result models and `I*ReportReader` interfaces
are trusted contracts; interactive callers still need the authorized report API.

The root response can contain groups and totals without account details. Submit a
returned group's `ChildrenPath` as `GroupPath` to read its children, retaining the
same parameters and filters. An empty statement may have zero rows. Interactive
execution rejects missing required dates, `DisablePaging`, nonzero offsets and
invalid cursors; follow returned continuation state within the published bounds.

The [financial statement example](../examples/equipment-inspection/README.md#built-in-financial-statements)
creates accounts with valid cash-flow mappings and posts four balanced standard
journals through public HTTP APIs. Known totals reconcile assets 1170, liabilities
50, equity 1120 and net income 120. Operating cash 170 plus financing 1000 produces
ending cash 1170. Deactivating the cash account preserves historical statement
values and its grouped account detail. Both administrator identities work without
manual grants; ungranted, inactive, display-name-only and anonymous users are denied.
This statement dataset does not certify every dimension combination. The separate
ledger report workflow below covers the other named report families.

## Built-in ledger and diagnostic reports

Use `IAuthorizedReportReadService` through the shared report API for
`AccountingReportCodes.GeneralJournal`, `AccountCard`, `GeneralLedgerAggregated`,
`TrialBalance`, `LedgerAnalysis`, `PostingLog` and `Consistency`. Their typed
reader interfaces are trusted runtime/provider contracts. Calling a reader
directly does not establish permission to view financial data.

General Journal, Account Card, General Ledger and Trial Balance require
`from_utc` and `to_utc`; Account Card and General Ledger also require the
`account_id` filter. Follow the returned opaque cursor with the same parameters,
filters and layout. Journal, card and ledger details support bounded pages.
Trial Balance is a bounded summary; its Debit/Credit columns are period
turnovers, not closing balances. Three journals of 100, 30 and 25 produce total
debit and credit turnover of 155 each; the selected cash account's 125 debit and
30 credit produce a closing balance of 95 when its opening balance is zero.

Ledger Analysis is composable. Its default account/period hierarchy places
account subtotals on separate rows, leaving group header measure cells empty.
Use `RowKind` and the returned column codes instead of assuming every row is a
detail or that a group header contains its totals. Send the returned
`ChildrenPath` as `GroupPath` to read the account's periods. The `account_id`
filter accepts multiple account IDs. Grouped analysis uses bounded pages rather
than cursor continuation.

Flat Ledger Analysis supports cursor paging when there are no row or column
groups, at least one detail field, only Sum measures, no grand totals and the
default ascending period order. Explicitly clear groups, set the desired detail
fields and disable grand totals. Each posting has two sides; do not mistake the
six flat rows from three balanced journals for duplicate entries. Retain the
same selection on continuation. Other layouts must follow their own published
paging contract.

Posting Log accepts optional date bounds and the published `operation` and
`status` filters. Inspect its definition before choosing filters; a lower-level
reader property is not automatically exposed by the public report definition.
Accounting Consistency requires `period_utc` and optionally a previous period
for chain checks. It returns diagnostic issues and summary counts. Reading it
does not repair ledger data or authorize a repair operation.

The [ledger report example](../examples/equipment-inspection/README.md#built-in-ledger-reports)
reconciles a known three-journal dataset across all seven report families. It
checks one-row continuation, account filters, running balances, grouped children,
flat posting sides, completed posting-log entries, zero consistency counts,
required inputs and access for both administrator identities. These checks do
not certify every dimension, aggregation, export, damaged-ledger diagnostic or
volume/performance property.

## Financial periods

Reuse `AccountingPeriodClosingController` at `/api/accounting/period-closing`
and the published `NgbAccountingPeriodClosingPage`. The controller requires
`admin / period_closing` with `view`, `close_month`, `reopen_month`,
`close_fiscal_year` or `reopen_fiscal_year` as appropriate. Compose the existing
API host and explicit application administrator codes. `IPeriodClosingUiService`
derives the audit operator from the current actor; it is not itself a substitute
for the controller's access check. `IPeriodClosingService` is the trusted domain
operation, not an unauthenticated HTTP endpoint.

Month close normalizes its input to month start, validates the closing chain and
atomically freezes balances, records the closed marker and writes business audit.
Subsequent accounting Post/Repost/Unpost is rejected. Reopening requires a reason
and is restricted to the latest closed month. Use `IAccountingBalanceReader`
`GetLatestClosedAsync` when a report needs a closed snapshot; reopening a month
does not automatically rebuild its old balance rows. Closing it again computes
the updated snapshot.

Fiscal-year close requires an open end month and closed prior months from January.
Choose an eligible active, nondeleted, credit-normal equity account without
required dimensions, using the retained-earnings lookup. Each profit/loss account
and dimension-set row is closed separately; retained earnings has empty dimensions.
The operation is deduplicated by the end period. A duplicate request returns
`period.fiscal_year.already_closed` without adding entries. An empty year still
records completion and audit; never invent entries to make it close.

Fiscal-year reopening is an explicit correction operation and requires a reason.
Later closed months prevent it. The SDK opens the end month if needed, removes
the current synthetic fiscal-close entries, rebuilds turnovers, removes that
month's balance snapshot and clears mutable completion state. Business documents,
posting history and audit remain preserved. This differs from ordinary document
Unpost, which appends accounting storno. Never reproduce either correction by
writing SQL or deleting arbitrary entries.

The [period example](../examples/equipment-inspection/README.md#financial-periods-and-fiscal-year-close)
uses the real authorized HTTP endpoints, two independent equipment balances,
public readers and an injected audit failure. Its fixed historical document dates
are arranged through `IDocumentDraftService` in trusted test setup; the generic
HTTP document payload does not expose number/date editing. No system clock is
changed, and the frontend uses the current month.

## Typed calculations and diagnostic boundaries

Account Card raw lines distinguish selected-account dimensions from the
counter-account dimensions. Their delta is debit minus credit relative to the
selected account. Running balances continue across grouped sections and pages;
optional totals describe the whole filtered range. Raw readers may ignore the
totals flag, and materialized readers do not supply a page bound. Keep interactive
requests on the authorized effective/paged report path.

Aggregated ledger continuation preserves period, document, counter-account code
and ID, and dimension set. Dimension expansion happens before low-level readers.
Typed DTOs describe these values; they do not authorize an account or certify
every grouping mode. `IReportExportService` serializes a materialized sheet to
XLSX bytes; it does not turn materialized output into a streaming export.

The in-memory balance calculator computes previous closing plus debit minus credit
for each account/dimension set and expects unique input keys. The turnover
calculator expects same-month entries. `AccountingPeriod` projects the month
without checking `DateTime.Kind`, while `OperationalRegisterPeriod` checks UTC
for its DateTime overload. Preserve those actual contracts. The storno factory
swaps account sides/dimensions and retains positive amounts; only the lifecycle
service owns writing and coordinating that history.

Negative-balance, closed-period rebuild and fiscal-close diagnostics preserve
their specific reasons. An in-progress operation or later closed period is not
permission to reset state or delete snapshots. Fiscal closing requires a suitable
retained-earnings account without required dimensions. Cash-flow reconciliation
and unclassified-counterparty errors require correcting the source configuration
through existing workflows, not adjusting report formulas to hide the mismatch.

Posting-log normalization explicitly selects strict or lenient UTC handling and
whether to validate only explicit bounds or the defaulted query range. The
lenient reader policy does not relax the report wrapper's strict UTC contract.
The built-in automatic journal reversal job delegates to the existing runner
with a UTC date and 100-post bound; a missing runner fails configuration. This
registration contract alone does not establish deployed scheduling or throughput.

## Composed accounting entry validation

The primary `PostingEngine` resolves one `IAccountingPostingValidator`. To add an
application rule, register one explicit composition that invokes
`BasicAccountingPostingValidator` first, then checks the additional rule. Adding
several registrations does not make the primary engine invoke all of them.
The separate `AccountingPostingPipeline` does iterate multiple validators, but
it is a lower-level writer and does not provide the document lifecycle, locks,
user authorization or transaction ownership.

Keep the standard checks for document identity, UTC/day, positive amount and
scale, distinct account sides and permitted/required dimensions. The validator
is synchronous; keep additional checks bounded to its supplied entries. Respect
its DI lifetime and avoid request-specific mutable state in production singleton
implementations. Return a stable application error through the standard boundary.

`AccountingPostingValidatorTests` supplies a test-only composition that restricts
one example service expense to 100. Both administrator identities can save the
125.50 Draft but receive a validation refusal on Post. The document/version and
all effects remain unchanged. Correcting the amount to 90 succeeds with accounting,
operational and reference effects. An unauthorized reader never invokes the
validator, and the standard empty-entry guard remains active. The mutable probe
exists only to observe this isolated test fixture; it is not application state.

An accounting validator's empty-entry guard does not forbid workflow-only
documents with a registered no-effect handler. Such documents do not submit an
empty accounting posting batch through this validator.

## Accounting engine boundaries

Posting contexts, chart snapshots and posting/reposting/unposting engines sit
below document lifecycle orchestration. The accounting engine requires accounting
entries; that rule does not mean every document must have Accounting, OR or RR
movements. A document handler may validly produce no effects. Keep interactive
posting on the document action path so validation, status, permissions and all
registered effect subsystems remain coordinated.

## Explicit accounting rebuild

`IAccountingRebuildService` is a trusted maintenance service for derived monthly
turnovers and balances. It reads the original accounting register; it does not
recreate business documents or rerun their posting handlers. Rebuild methods
normalize a date to its month, while `VerifyAsync` requires a month start. Use
`RebuildAndVerifyAsync` for a logged combined operation; individual turnover and
balance rebuilds are also available. The service owns its transaction and must
not be nested inside another owned UnitOfWork transaction.

An already closed month is refused. Coordinate maintenance with period-closing
operations in the deployment; this example does not establish concurrent
close/rebuild safety. Do not invoke repair automatically because a diagnostic
fails, and do not expose the raw service without an explicit authorized
operational workflow.

`AccountingRebuildTests` posts an ordinary service document, deliberately removes
derived turnover rows in a disposable database, and verifies a failed rebuild
rolls back its new rows without changing the original entry. A later explicit
rebuild restores exact turnovers/balances and a clean consistency report. Separate
rebuild methods remain repeatable; after closing the month, all write variants
refuse and verification remains available. Test damage and the failure decorator
are isolated fixture code, not production maintenance commands.
