# Equipment and inspection application

This independently authored fixture uses public NGB packages to define an
equipment catalog and an inspection document. Definitions contain provider-neutral
metadata and read contracts; Runtime supplies posting validation; PostgreSql
supplies storage, typed reads and embedded migrations. Api hosts the public SDK
controllers, Migrator applies the platform and application migration packs, and
Setup applies the registers' default configuration through public services.
The Vue application uses the published UI package for lists, lookup fields,
editors, document actions and authentication. Integration and browser tests start
the actual Api entry point with disposable PostgreSQL and Keycloak.
Bootstrap explicitly configures navigation, metadata, reference lookup, editors,
reporting, Work Center and the command palette. The metadata form delegates reference search
and navigation to the public lookup adapter. Document effects, flow and print
routes use the framework's standard pages. Shared pages are loaded through the
public `lazy` entry; the application's unknown-route page composes the public
layout header. Browser acceptance also checks theme changes, mobile width,
configured navigation and the public assets emitted by the Vite plugin.

The fixture distinguishes two configurations. `InspectionRuntimeDefinitions`
adds completion validation. Adding `InspectionWorkflowDefinitions` also registers
`InspectionWorkflowPostingHandler` in the definition, and the host registers that
handler both by its concrete type and as `IDocumentReferenceRegisterPostingHandler`
in DI. The validator follows the same registration pattern; startup validation
resolves the types declared by the definitions. The handler
intentionally produces no register records,
so a valid inspection can become Posted with empty Accounting/OR/RR effects.
This uses the existing public lifecycle and requires no SDK changes or artificial
movements. Omitting that contributor leaves no posting handler: a complete
inspection then remains Draft and returns `doc.posting.handler.not_configured`.
The HTTP suite independently exercises both configurations. The supported
configuration uses automatically supplied standard actions; the negative fixture
explicitly exposes Post only to verify the lifecycle's missing-handler guard.

Inspections receive SDK-allocated numbers on Draft creation. `example.reinspect`
creates a separate numbered empty Draft linked to its source through the directed
ManyToOne `example.reinspection_of` relationship. It does not copy fields or Post
the new document. Composition acceptance checks concurrent numbering, preservation
through lifecycle actions, idempotent derivation, endpoint and cardinality guards,
bounded pages and cross-type lookup. Graph reads use ViewFlow separately from
View and hide types the reader cannot view. A test-only bidirectional relationship
exercises that filtering; invalid mirrored-field metadata is rejected at startup.

The API-only `example.inspection-checklist` adds real head/part storage. Drafts
may have no checks or incomplete checks; line numbers must be positive and unique,
and observation dates cannot be in the future. Post requires complete rows and a
persisted review flag. The Post validator explicitly invokes the registered
approval policy because generic Post does not do so automatically. The review
flag is a completion condition, not a separate approver authorization workflow.
HTTP tests cover 501-row round trips, omitted/replaced/cleared parts, schema-bound
payload rejection, rollback after real part/audit writes and cascading deletion.
The web fixture currently provides head-only editors for its navigation entries;
it does not expose a checklist parts editor.

Migration acceptance also starts from the four frozen application migrations in
`MigrationFixtures/Baseline`, creates real equipment and an inspection through
public services, and upgrades to the checklist schema. It verifies preserved
records/audit, repeat execution, checksum rejection and recovery with the original
scripts. This is an application-schema upgrade on SDK 3.4.0. The separate Migrator
process runs from its own build output, where all of its runtime dependencies are
present; its copied DLL in a test project's output is not a standalone deployment.

Copy this directory to a temporary directory outside your application before
building. The installed `ai/vendor/` tree is immutable guidance. Use the exact
3.4.0 packages and a fresh package cache. For published packages, run:

```sh
dotnet restore EquipmentInspection.slnx --configfile NuGet.Config --packages .packages
dotnet build EquipmentInspection.slnx --no-restore -c Release
dotnet test EquipmentInspection.Tests/EquipmentInspection.Tests.csproj --no-build -c Release
dotnet test EquipmentInspection.IntegrationTests/EquipmentInspection.IntegrationTests.csproj --no-build -c Release
cd web
npm ci --ignore-scripts
npm exec --no -- playwright install chromium
npm run build
npm run test -- --run --coverage
npm run test:e2e
```

Use an explicit sealed-candidate NuGet configuration before publication; never
fall back to project references or a source checkout. The integration suite
requires a local Docker engine and creates its own test resources. It does not
accept a deployment database or application credentials.

The browser harness creates a fresh realm, users, database and API for each run,
serves the production web build on a loopback port, and removes its own resources
after execution. Its credentials are generated for that run. The browser performs
the normal Keycloak login and uses real API responses. `npm run test:e2e` requires
the Release build above. Use one canonical filesystem path for restore, build
and execution, including on macOS where `/var` aliases `/private/var`.
The browser administrator has an active application user record for personal
Work Center data; the HTTP suite separately verifies unlinked trusted
administrators and linked application administrators without manual grants.

For an application deployment, provide `ConnectionStrings:DefaultConnection`
and `KeycloakSettings:Issuer`, register the web client's ID under
`KeycloakSettings:ClientIds`, and configure that client's redirect URIs. Run
Migrator against that application's database, then run
`dotnet run --project EquipmentInspection.Setup -c Release` with the same
`ConnectionStrings__DefaultConnection` environment setting before starting Api.
Setup is an explicit deployment step, not an API startup side effect. Supply the
corresponding public identity settings in `web/public/runtime-config.js` before
building, or replace `dist/runtime-config.js` when deploying. Serve `/api` through
the same origin as the UI and proxy `/hubs` with WebSocket support;
`NGB_EXAMPLE_API` selects the local Vite proxy target. Api is the single owner of
Work Center outbox processing in this example.

Acceptance covers incomplete Draft save, complete-field validation at Post,
Post/Repost/Unpost without movements, rejection without a handler, stale versions,
concurrent requests, exact idempotent replay and conflicting keys. Catalog cases
exercise point/batch reads, update isolation, bounded filtered pages and reversible
deletion marks. Draft deletion checks both permissions and preservation of Posted
documents. Each HTTP case starts its own database, identity realm and host.

Authorization cases use real tokens for an unlinked trusted administrator, an
active linked administrator with an explicitly registered role code and a renamed
display name, an ordinary actor, an actor whose unregistered role is merely named
Administrator, and an inactive linked administrator. No permission grants are
seeded. The tests check a newly contributed permission, all Attachments/Notes
permission keys and the independently disabled feature. These permission checks
do not claim coverage of attachment upload/storage. Note operations have their
own HTTP and browser scenarios described below.

### Server navigation and bounded search

`EquipmentMainMenuContributor` supplies catalog, document and report destinations
through the public server-menu contract. `MainMenuHttpTests` verifies merged
groups, ordering, duplicate selection, both administrator identities and current
permissions after public role changes. The secured admin controller filters denied
entries and empty groups; destination APIs still enforce access independently.

`EquipmentSearchController` exposes the installed `INgbSearchService` at
`/api/equipment-search`. `SearchHttpTests` checks scoped stored-record/report
results, overall and per-type bounds, query validation, source availability and
current-user `view` plus `lookup` permissions. `SearchPipelineContractTests` verifies
sequential source execution, cancellation and command-palette scope parsing.

The built-in General Journal Entry cannot use generic document lookup in 3.4.0:
its metadata lacks the required `display` column. The limitation test verifies
`UnavailableSources=["documents"]` and `Truncated=true`, while the typed journal
API remains usable. Application-document scope excludes that journal kind. Keep
incomplete search results explicit; do not modify platform metadata to conceal
this limitation. See [composition and search contracts](../../references/application.md).

### Notes and parent audit

The API already registers `AddNgbAttachmentsNotesApi`; set
`FeatureManagement__Notes=true` to enable Notes for a deployment. No object store
is required. The shared catalog/document editor displays Notes under **More
actions** for saved objects and uses the existing permission-aware API. Feature
availability remains independent of administrator permissions.

`NotesHttpTests` covers both administrator identities, notes on catalogs and
documents, plain-text validation, bounded cursor paging, optimistic concurrency,
deletion marks and actor-attributed parent audit. It checks system note permission
and parent access independently, inactive users, disabled features, and filtering
of note text from audit when the caller lacks note-read permission. Role grants
are changed through the public security API so access-version invalidation is
part of the scenario. Directly changing permission storage is not an application
workflow for refreshing a user's cached access.

The browser fixture explicitly enables Notes and creates a separate content-reader
role. A real administrator adds and edits a note through the shared drawer; HTML
markup remains literal text. A second browser session can read it but all mutation
controls are disabled. Deletion removes it from the list and count after reload.
See [content contracts](../../references/content.md) for requests and audit cursor
semantics. This coverage does not imply attachment-storage or journal-parent
acceptance.

### Attachments and retained audit files

The API optionally selects the public MinIO adapter when
`FeatureManagement__Attachments=true`. Supply `Attachments:MinIO` endpoint,
private bucket and application credentials through deployment configuration.
`InternalEndpoint` is for server operations and `PublicEndpoint` must be reachable
from the browser; do not rewrite signed URLs. Configure CORS for the actual web
origin. The defaults leave Attachments disabled, so a deployment that only needs
Notes does not need MinIO credentials or a running object store.

`AttachmentsHttpTests` provisions isolated storage and verifies real PUT/GET,
sealing, completion, metadata limits, quota, expiration, access checks, paging and
retry after a storage credential failure. Sealing protects a completed file from
later writes to its staging URL. Delete and pending expiration change metadata;
they do not claim physical file erasure. Authorized audit-download remains possible
for completed retained files after logical deletion. The browser fixture exercises
real uploads/downloads and a reader with disabled upload/delete controls.

See [content contracts](../../references/content.md) for storage configuration,
lifetime defaults and retention boundaries. All fixture credentials are generated
for disposable resources. An injected clock tests expiration; no test modifies
the immutable creation time in storage or waits a full day.

### Authorized runtime MCP reads

The API includes `NGB.Platform.Mcp.AspNetCore` and the application-owned
`EquipmentReadToolContributor`. Deployments explicitly enable
`FeatureManagement__Mcp=true` and set `Ngb__Hosting__PublicApiUrl` to their public
API origin. The normal bearer validator, active-user policy and catalog access
remain in force. MCP is disabled by default and provides no mutation tools.

`example_get_equipment` reads one saved equipment item through
`IAuthorizedCatalogReadService` and returns its ID/name with an explicit catalog
source. `McpHttpTests` invokes the real HTTP endpoint, including tools/list and
tools/call, and verifies denied and granted access, opt-out, request limits,
malformed input, cancellation and unchanged data/audit. The test identity helper
uses Keycloak's authorization-code exchange with PKCE in a disposable realm;
the application's authentication is never replaced with a test handler.
See [MCP contracts and deployment settings](../../references/mcp-assistant.md).

`McpBuiltInReadTests` invokes all 18 built-in read tools with known records,
reports, notes, audit, notifications and Ready attachment metadata. Reads preserve
saved data, audit and notification unread state. Catalog access changes use the
public security API. The journal-detail case verifies metadata, typed MCP detail and compatibility
fetch against the dedicated authorized HTTP API, including denied access and
unchanged journal state. See the
[built-in read contracts](../../references/mcp-assistant.md#built-in-authorized-reads).

### Opt-in AI Assistant

`EquipmentAssistant` composes the public Assistant host and selects one of four
published provider adapters. Both `FeatureManagement:AiAssistant` and
`FeatureManagement:Mcp` must be enabled. The example's `AiAssistant:Provider` must
be exactly `OpenAI`, `Anthropic`, `Bedrock` or `Ollama`; bind that provider's options
under its matching `AiAssistant` subsection. Supply keys through server secret
configuration or use a Bedrock workload identity. No provider is resolved while
Assistant is disabled, and no credential belongs in browser runtime settings.

The API maps settings and streaming chat after the existing MCP endpoint. All
business reads use the current user's authorized MCP tools. The example adds no
custom Assistant UI; applications can use the public shared UI integration.

`AssistantHttpTests` exercises every published adapter through the real API and
MCP loopback with disposable application data and Keycloak. Only the vendor
transport responses are fixtures; these tests make no paid model calls and never
read personal provider credentials. They verify sources, permissions, configured
budgets, timeout, client cancellation and absence of credentials or private error
details in responses. `AssistantConfigurationTests` verifies startup failures,
opt-out and native-adapter cancellation. This does not measure live model quality.
See the [provider and hosting guide](../../references/mcp-assistant.md#assistant-providers).

### Catalog extensions and typed storage

`EquipmentCatalogRuntimeDefinitions` adds `EquipmentNameValidator` to the original
catalog without replacing its metadata. This example requires a printable,
single-line name; its schema still owns maximum length and required storage.
Both create and partial update use effective-field validation. The runtime
registers the concrete validator and its public interface as the same scoped
instance. Missing that registration prevents startup.

`CatalogCompositionTests` exercises rejected upserts without changing saved data
or audit, bounded authorized lookup, and the actual typed provider. Ordinary
metadata CRUD writes the validated head directly; the separate trusted
`ICatalogDraftService.CreateAsync` path creates a placeholder through
`PostgresHeadCatalogTypeStorage`. The test forces a failure in an enclosing unit
of work and verifies that header, head and audit all disappear. It does not use a
raw SQL query or add a draft-creation endpoint to the application.
See [catalog contracts](../../references/catalogs.md) for transaction ownership.

Two retained SDK boundary tests expect authorization to be checked again when a
completed action is replayed. The current package returns the saved result, so
these assertions fail. The cross-user test supplies the complete original request
and key; it does not demonstrate how another user could obtain them. The inactive
user case returns that user's previous result without executing the action again.
These observations do not establish practical exploitability. The reports retain
the failures separately from the application workflows; the Kit does not change
the platform's authorization or document lifecycle.

## Inspection summary

Open `/reports/example.inspection-summary` for the standard report page. It counts
Posted inspections by result in an inclusive UTC date range. Drafts and documents
marked for deletion are outside that report's business definition. Equipment and
result filters constrain both rows and the grand total. The report uses `Count`
aggregation so an empty result has a count of zero.

`InspectionReportDefinitions` contributes the public report and dataset metadata.
`InspectionReportDataset` binds those fields to the application's provider tables;
the SDK performs parameterized grouping, cursor paging and streaming XLSX export
in PostgreSQL. No report rows are loaded into an application-side aggregation loop.
The API delegates to `ReportControllerBase` with `IAuthorizedReportReadService`;
the UI uses `NgbReportPage` and `createDefaultNgbReportingConfig`.

The acceptance fixture creates two equipment items and five Posted inspections:
the first has two Passed and one Follow-up, the second one Passed and one Rejected.
An additional Draft must not contribute. The expected unfiltered total is five;
the first equipment's total is three. Tests traverse one group per page, reject
changed or invalid cursors and unbounded interactive requests, compare filtered
XLSX cells to those same expected counts, and check both administrator forms,
ordinary and inactive actors. The browser follows a real Posted inspection into
the report and separately checks denied direct navigation.

The report supports personal and shared saved variants. The API explicitly binds
`IReportVariantAccessContext` to `HttpReportVariantAccessContext`; Runtime's default
context has no HTTP actor. Real Keycloak tests prove private ownership, different
users reusing a private code, separate shared mutation rights, default replacement,
input limits and execution of a saved result filter. The unlinked Keycloak
administrator receives an owner projection through the existing SDK service.

The streaming test creates 501 distinct Posted results through the public API and
checks two bounded provider batches. A newly posted result cannot change the
active snapshot. It then cancels a subsequent stream, ends the read session with
an uncancelled cleanup token and verifies a fresh read sees all 502 results.
The provider test uses trusted service access; it does not replace HTTP access
checks or claim a general memory benchmark.

`/reports/example.inspection-outcome-rate` is a second, fixed-shape report. Its
specialized executor returns a prebuilt summary row from two bounded PostgreSQL
aggregates: Posted inspection count, exact `Passed` result count, and their ratio
as a percentage. Three Passed results among five Posted documents produce 60%;
Drafts are excluded and empty input has no percentage. The normal report host
still owns authentication, permissions, request validation and the read snapshot.
This report deliberately advertises only its supported single-row presentation;
filter composition, variants and XLSX export are not enabled for it.

## Equipment usage and operational registers

`example.equipment-usage` records from greater than zero through 24 operating
hours for one equipment item. Drafts may remain incomplete. Post validates the
fields and emits one `hours` resource movement in `example.equipment-hours`,
dimensioned by that equipment. The document's UTC timestamp determines the
movement period. The example adds its application-owned dimension in a new
immutable migration; its ID and code are stable across installations.

`EquipmentDefaults` registers the resource and required equipment dimension using
`IOperationalRegisterManagementService`, then ensures physical storage through
`IOperationalRegisterAdminMaintenanceService`. Running Setup again preserves
existing movements and configuration. A different schema must follow the public
append-only rules; Setup does not suppress a schema incompatibility.

`EquipmentUsagePostingHandler` reads through the provider-neutral application
contract, resolves the dimension set and builds a movement. The SDK owns
transactional persistence and reversal. Successful retry adds no movement;
Repost and Unpost retain the original records and append storno. The runtime
layer contains no SQL or provider dependency. The frontend uses the same public
metadata editor and Effects page as the inspection workflow.

Tests verify the 6.25-hour lifecycle, unchanged history on stale requests,
repeatable setup after posting, and rollback after an injected failure observes
an already-written movement inside the transaction. A separate fixture expects
four hours for one equipment item and five for another, verifies one-row pages
and dimension filters, and explicitly runs the SDK finalizer before reading
turnovers and balances. The separate worker schedules finalization; this example's
API does not claim synchronously refreshed monthly projections. Browser acceptance
creates usage and displays its actual movement
and enriched equipment name.

## Equipment assessments and reference registers

`example.equipment-condition` records the assessed condition of one equipment
item. It is a separate document so the original inspection still demonstrates
Post without effects. Incomplete Drafts are allowed; Post requires an equipment
reference and nonblank condition. `EquipmentConditionPostingHandler` emits a
nonperiodic record in `example.equipment-condition-records`. Setup registers a
required equipment dimension and a nonnullable string field through the public
reference-register management service.

The register is `SubordinateToRecorder`: the key is scoped to the assessment
document. Read operations must supply its recorder ID. The result represents
that document's assessment, not a global latest condition across all assessments.
For a global independently written register, choose `Independent` and its public
write service and define the ownership/reversal policy separately.

Post and Repost emit current values; successful retry adds no record. On Unpost
the handler emits nothing and the engine appends tombstones for that recorder,
preserving the nonnullable values and original history. A new Post after changing
the Draft to another equipment item leaves the old key tombstoned. Tests traverse
history and current keys one row at a time, retain the cursor returned for the
last examined key, and check historical reads, filtered/enriched reads, separate
recorders, concurrent Post and rollback after a real write. Repeated Setup must
preserve records and reject incompatible periodicity or dimension removal.

The equipment dimension ID is the public SDK's
`DeterministicGuid.Create("Dimension|example.equipment")` value; it is shared by
both register examples and the migration. The Reference Register management
contract checks this correspondence. Arbitrary stable GUIDs are insufficient.
This example is still unpublished; its earlier local candidate used an arbitrary
ID and is superseded. Published migrations and installed register identities
must not be rewritten using this development correction.

Browser acceptance creates an assessment, displays its current effect in the
standard Effects page, and confirms that Unpost clears that view. Effects is a
projection of the current Posted document, not its append-only journal; its
display row IDs are not persisted record IDs. The integration suite separately
checks the actual tombstone and preserved history through the register reader.
The runtime handler delegates persistence and transaction control to the SDK;
no direct register SQL or independent write is used inside document posting.

## Equipment service and combined effects

`example.equipment-service` records one equipment service: positive cost, more
than zero and at most 24 service hours, and the condition after service. Cost is
expressed in the application's accounting unit; this example does not implement
currency conversion, taxes or a jurisdiction-specific chart of accounts. Drafts
may be incomplete. The document registers all three public posting contracts on
`EquipmentServicePostingHandler`:

- Accounting debits `example.service-expense`, dimensioned by equipment, and
  credits `example.service-payable` without an equipment dimension.
- Operational Registers write `hours` to `example.equipment-service-hours`, kept
  separate from the operating-hours register.
- Reference Registers write the condition to the assessment register, scoped to
  this service document's recorder ID.

Each handler builds its own effect. The SDK owns the single lifecycle transaction
and the engines' reversal behavior. Runtime never commits independently. On
Unpost, accounting appends an entry with debit and credit swapped and the same
positive amount; Operational Registers append storno movements; Reference
Registers append recorder tombstones. Do not apply an extra sign reversal to the
accounting amount just because `IsStorno` is true.

`EquipmentServiceDefaults` creates the two example-owned accounts through public
chart management and configures the service-hours register. Repeated Setup
preserves existing IDs, postings and custom account names. Inactive/deleted codes
or incompatible type, statement section, contra flag, negative-balance policy or
dimension rules cause an explicit failure, without silently repairing the chart.
Apply Setup as a single deployment operation before starting application hosts.

Acceptance expects expense and payable closing balances of +125.50 and -125.50,
2.5 service hours and `Ready after service`. Repost retains these values; Unpost
returns the net balances and hours to zero and removes the current reference state.
Original accounting entries remain in the journal, whose one-row pages are checked
against the document history. A fault writer verifies all three effects already
exist inside the transaction, then throws: independent reads must find none of
them, and the exact request can succeed on retry. Separate checks preserve custom
account labels and refuse incompatible Setup configuration.

Browser acceptance creates the document and displays all three current effects,
including 125.50 on both sides of the accounting entry. The reference-state and
history contracts described above also apply to this combined document.

## Financial periods and fiscal-year close

The application exposes the existing SDK period-closing controller and routes
`/admin/accounting/period-closing` to `NgbAccountingPeriodClosingPage`. Navigation
uses `admin / period_closing / view`; each HTTP mutation enforces its own platform
permission. The operator identity comes from authentication. Both trusted
identity administrators and explicitly registered application administrators work
without individual grants; ordinary, inactive and anonymous users are denied.

`AccountingPeriodHttpTests` specifies two service amounts, 125.50 and 74.50, with
separate equipment dimensions. Month close freezes balances and prevents new
Post, Repost and Unpost. Closing out of sequence and reopening an earlier month
are rejected. Reopening requires a reason recorded in audit, permits the exact
previously refused Post to succeed, and a new close produces a 200.00 payable
balance. A fault after the real balance and closed-marker writes verifies their
atomic rollback with audit; the service entry remains untouched.

The fiscal-year scenario closes January through November, then closes the open
December period into an explicitly configured example retained-earnings account.
It expects two closing entries with preserved equipment dimensions, zero net
expense, a +200.00 debit balance in retained earnings and a -200.00 payable.
Duplicate close does not add entries. Opening the fiscal year requires any later
closed month to be reopened first. It restores the original +200.00 expense,
removes the synthetic closing entries and December snapshot, and preserves the
original document entries and audit. Reclosing uses the same fiscal-year identity.
Concurrent closing of an empty year records completion once without inventing
accounting entries.

Historical document dates are arranged with public `IDocumentDraftService` in
trusted test setup. Generic document HTTP payloads do not expose date changes;
this test helper is not registered as an application endpoint. System and database
clocks remain unchanged. The production UI uses the SDK's current-month defaults.
Browser acceptance closes a month, reloads the stored state, requires a reopen
reason and confirms the operation. Denied direct navigation is checked against
the real API. Fiscal-year browser interactions and additional accounting-report
shapes require separate acceptance; HTTP evidence does not certify those UI flows.

## Background worker

`EquipmentInspection.BackgroundJobs` composes the SDK's Hangfire host and
PostgreSQL storage adapter. `AddEquipmentRuntime` is shared with the API, and
both hosts use the same application persistence registration. Apply migrations
and Setup before starting either host; the worker does not modify application
schemas on startup.

The worker's `appsettings.json` enables only
`opreg.finalization.run_dirty_months`, every five minutes in UTC. It processes at
most fifty dirty register-months per invocation. Monthly projections are eventually
refreshed; posting does not wait for the next scheduled run. Supply application,
Hangfire and Keycloak configuration from the environment or secret store. The
dashboard remains protected by the existing admin-console authentication.

`BackgroundJobsTests` starts the real worker with disposable PostgreSQL/Keycloak,
checks its desired-versus-actual schedule and triggers the stored recurring job
through Hangfire. It checks 6.25 hours in actual turnover/balance projections,
repeat execution without new movements, restart with the same storage, zero
after Unpost and removal of recurring registrations when scheduling is disabled.
Test-only probes verify a visible Failed job, explicit requeue, overlap refusal
and cancellation releasing the distributed lock. They are not production jobs
and send no external notifications. The runner disables automatic retries;
applications must design recovery and replay safety for each business operation.

## Independent and periodic reference registers

`IndependentReferenceRegisterTests` configures additional registers inside its
trusted disposable fixture and uses the public SDK services directly. It covers
all six periodicities, required UTC periods and dimensions, forbidden recorder
arguments, append-only independent commands and command retry. These are contract
examples for an application service; no independent-write HTTP endpoint is added.

The monthly rate scenario distinguishes effective time from recorded time. A
future rate cannot hide an earlier applicable rate, and an old as-of query keeps
the state known before a correction. History is paged within an explicitly chosen
period bucket. A tombstone retains the selected version's period and values;
it does not replace a later effective rate or remove prior history.

Separate scenarios verify rollback of upsert/tombstone records, audit and command
completion in a caller-owned transaction, two concurrent identical commands, and
bounded cursor pages across deleted equipment keys with enriched display values.
The [independent-register guide](../../references/independent-reference-registers.md)
explains the exact contracts and the application's authorization responsibility.
Report variants, pivot workflows and independent-write UI flows still require
their own scenarios and evidence.

The integration assembly runs at most two test collections concurrently through
its packaged `xunit.runner.json`, using xUnit's conservative scheduler. Each test
owns disposable PostgreSQL and Keycloak instances; the fixed collection budget
keeps container startup bounded independently of the host's CPU count. Explicit
concurrent operations inside a test remain concurrent. Timeouts and assertions
are unchanged, and diagnostics record the runner's effective configuration.

## Work Center service and HTTP contracts

`WorkCenterHttpTests` supplies test-owned task/notification definitions and linked
synthetic users, then uses public creation services and the real Work Center HTTP
endpoints. It verifies recipient deduplication, independent read/dismiss state,
role delivery, subsequent preference changes and mandatory preferences, paging,
concurrent versioned claims, snoozing, completion and cancellation. A recipient
without source access cannot see the item; inactive users receive no new delivery.
An outer transaction's notification remains invisible until commit, and rollback
permits an exact retry. No email or external messaging service is invoked.

These service fixtures are separate from the application's document-event policy.
See [Work Center guidance](../../references/work-center.md).

## Inspection withdrawal and outbox

`InspectionWithdrawalPolicy` creates a personal review task and an in-app notice
when a linked user withdraws an inspection. The task concerns that recorded
withdrawal, so subsequent posting does not cancel it. Each later withdrawal has a
new event ID and a new review; retrying the same event retains the original items.
The review is mandatory, while the informational notification can be disabled.
Other actions, other documents and events without a linked actor create no personal
work. This is a deliberately small example policy, not a platform business rule.

`WorkCenterOutboxTests` verifies the actual HTTP action and hosted projection:
rollback of both writes after an injected policy fault, delayed retry, stable event
identity, duplicate suppression, and reads during the after-commit realtime callback.
A rolled-back envelope remains invisible, and a failed realtime signal does not
undo committed work. `OutboxDeliveryTests` uses a separate real PostgreSQL instance
and controlled clock for eight-attempt dead-letter behavior, runtime-container
restart, the 25-event batch bound, concurrent claims and expired-lease fencing.
These cases use public packages throughout. They do not test SignalR transport or
browser Work Center interactions, nor do they provide a manual dead-letter redrive API.

The separate browser scenario maps the public Work Center and notification-preference
pages, observes a real SignalR message in a second tab, and verifies both items
appear without refreshing. It exercises dismiss, snooze and persisted preferences;
disabling the optional notice still preserves a new mandatory withdrawal review.
The API, identity provider, outbox and database are real throughout. No business or
authorization behavior is added to the SDK for this scenario.

## Custom inspection action

`example.defer-one-day` moves a selected-equipment draft's date forward by one day
and requires a reason. Its explicit evaluator requires both view and draft-edit
permission; a viewer cannot mutate merely because they can read the inspection.
One context enricher reads bounded facts, pure availability rules explain disabled
states, and the handler joins the dispatcher's transaction through the public
draft service. The action retains the document's number and Draft status.

`InspectionDeferralHttpTests` verifies authorization, required reason, current
state facts, audit attribution, repeated/concurrent requests and rollback after an
observed date write. `InspectionDeferralTests` adds cancellation, date-limit and
pure-rule cases. See [custom action guidance](../../references/custom-document-actions.md).

## Standard General Journal Entry

The example also tests the SDK's existing `general_journal_entry` workflow through
its dedicated accounting controller. It reuses the equipment expense/payable
accounts and required equipment dimension; no GJE definition, migration or
business logic is added to the application. This standard workflow has its own
approval state and typed API, separate from the custom inspection documents.

`GeneralJournalHttpTests` covers Draft/header/line operations, balanced submission,
approval, immutable rejection, posting and allocation, manual reversal,
due-only system reversal, bounded typed reads, permissions and deletion marks.
The trusted atomic-facade case verifies rollback and successful posting. Actual
entries are reconciled through the public accounting reader. See
[standard-journal guidance](../../references/accounting.md#standard-general-journal-entry).

## User and role administration

`SecurityAdministrationHttpTests` exercises the existing security controller with
both administrator kinds. It creates and updates users through the real disposable
Keycloak realm, changes active state and assignments, and checks persisted audit
records. Ordinary role mutations change current effective access and access versions;
registered administrator roles retain their protected code and implicit permissions.
User paging binds its cursor to the active filter, and ungranted, inactive and
anonymous actors cannot administer users or roles.

The test identity realm includes a confidential user-management client with a random
secret scoped to that realm. Its settings are injected only into the test host.
It uses the starter's username-editing setting for email/profile changes. No personal
identity-provider credentials or existing deployment are used. See
[administration guidance](../../references/security.md#user-and-role-administration).

## Chart of Accounts administration

The application exposes the public `AdminControllerBase` through the explicitly
injected `PermissionAwareAdminService`. `ChartOfAccountsHttpTests` verifies both
administrator identities, public metadata, create/update and ID lookup, audited
activation/deletion transitions, bounded pages and cursor/filter consistency.
An ungranted reader is refused; after a view-only grant, the same reader can read
accounts but cannot mutate them. Inactive, display-name-only administrators and
anonymous actors remain denied. See the
[account administration contract](../../references/accounting.md#chart-of-accounts-administration).

## Built-in financial statements

`FinancialStatementsHttpTests` posts four balanced standard journals through the
public API and reconciles Balance Sheet, Income Statement, indirect Cash Flow and
Statement of Changes in Equity against fixed expected totals. It follows the
returned group path to account details, retains inactive-account history, checks
empty results/request bounds and verifies both administrator identities and denied
actors. See [financial statement guidance](../../references/accounting.md#built-in-financial-statements).

## Built-in ledger reports

`LedgerReportsHttpTests` reconciles three balanced journals through General
Journal, Account Card, General Ledger, Trial Balance, Ledger Analysis, Posting
Log and Accounting Consistency. Seven HTTP cases cover one-row continuations,
running balances, turnover totals, account filters, separate group subtotals and
period children, flat posting sides, request bounds and access checks. No report
formula or SDK behavior is reimplemented. See the
[ledger and diagnostic contracts](../../references/accounting.md#built-in-ledger-and-diagnostic-reports).

## Work Center retention

`WorkCenterMaintenanceTests` exercises the public maintenance service against a
disposable PostgreSQL database with a controlled clock. It verifies completed
action receipt expiry, terminal tasks, notification deliveries and outbox retention,
including bounded batches and preservation of recent or unfinished work. These
are trusted maintenance tests, not a new user-facing deletion endpoint. See
[retention guidance](../../references/work-center.md#retention-and-maintenance).

## Operational-register administration and nets

`OperationalRegisterAdministrationTests` exercises trusted public services with
disposable resources. It diagnoses missing physical tables, ensures only selected
registers, verifies repeatability and checks default-projector finalization.
A separately misconfigured host verifies the blocked state without changing SDK
behavior. The net-reader case posts two equipment dimensions through HTTP and
reconciles their independent amounts before and after Unpost without finalization.
See [administration and transactional reads](../../references/registers.md#operational-register-administration-and-health).

## Operational-register dimension paging

`OperationalRegisterDimensionPagingTests` uses the public query reader against
disposable PostgreSQL. Signed facts and their reversal are written through the
trusted public applier for real equipment/inspection records; this is technical
fixture setup, not an additional inspection posting rule. January nets of 10, 3
and -7 become February balances of 12, 3 and -6. The cancelled fourth dimension
does not appear. One-row offset and cursor pages preserve ordering and totals
before finalization, after a January snapshot with February roll-forward, and
after all dirty months are finalized.

A separate case appends facts out of occurrence order, including three records
at the same instant. It verifies exact-date offset/cursor reads, month-based
movement-ID ordering, dimension enrichment/filtering, maximum-month filters and
retained storno. The first cursor page starts with a null cursor; subsequent
pages preserve all facts without skips or duplicates, including equal timestamps. These service tests do not expose a new HTTP endpoint or claim
authorization, concurrency or volume/performance acceptance. See
[dimension net and history contracts](../../references/registers.md#dimension-net-pages-and-movement-history).

## Reference-register administration

`ReferenceRegisterAdministrationTests` exercises trusted runtime/provider services
against disposable PostgreSQL for independent and subordinate nonperiodic registers.
A metadata-only setup created through public persistence ports models an interruption
before physical table creation. The test diagnoses both missing tables, ensures
only the selected register, repeats ensure and checks the whole health report.
The resulting tables retain append-only guards and contain no records. These
service checks do not establish HTTP authorization or exhaustive schema repair.
See [reference-register administration](../../references/registers.md#reference-register-administration-and-health).

## Watchdog host

`WatchdogHttpTests` composes a monitoring host directly from the public Watchdog
package. A separate local web server alternates between HTTP 200 and 503; the
health response follows failure and recovery. The default stylesheet comes from
the package output. Further cases validate custom route/title normalization,
missing configuration/assets and protected dashboard/data responses for anonymous,
reader and `ngb-admin` principals using real test cookies.

The synthetic sign-in route exists only inside the test host. The fixture does not
claim external OIDC login or production TLS verification. It does not install a
monitoring deployment or change platform authorization. See the
[Watchdog host contract](../../references/application.md#watchdog-host).

`PostgresCompositionTests` verifies provider options at host startup, application
clock preservation and scoped connection ownership without opening a database
connection. See the PostgreSQL composition section in
[the persistence reference](../../references/persistence.md).


## Error boundaries

`ErrorBoundaryContractTests` verifies the standard runtime error policy and the
real Production HTTP middleware on a loopback server. It checks stable codes,
field validation, trace/request identity, distinct HTTP statuses and a synthetic
provider mapper returning 503. Cancellation and provider exceptions retain their
identity; unexpected and timeout wrappers retain private details only as inner
exceptions. The fixture does not add a business endpoint, implement authorization
or claim arbitrary application context is sanitized. See
[application error contracts](../../references/errors.md).

## Maintenance diagnostics and typed schema guards

`BuiltInMaintenanceJobsTests` executes seven built-in maintenance jobs twice on
an initialized disposable database: platform schema, audit health, two accounting
integrity/drift checks, stale-operation monitoring, OR schema and RR schema.
These checks use real provider services and inspect their metrics. Their initialized
baseline does not claim to reproduce production drift or scheduler delivery.

The new application migrations install the existing SDK's typed-document guards,
align string columns with its `text` schema contract while preserving length checks,
and add the required direct registry foreign key for checklist rows. Previous SQL
resources and the frozen migration baseline remain unchanged.
`TypedDocumentGuardTests` verifies SQLSTATE 55000 on a direct storage deletion of
a Posted document, transaction rollback, retained checklist rows/audit and successful
public Unpost followed by Draft editing. `MigrationHttpTests` continues to verify
upgrade from the frozen earlier application schema and checksum-drift refusal.

## Batch, maintenance and provider extension examples

`AccountingPostingValidatorTests` composes the standard accounting validator with
an example expense limit. `DocumentBatchTests` verifies trusted atomic Draft/Post
batches and deterministic number formatting, including reservation rollback.
`DocumentRelationshipBatchTests` covers atomic/idempotent relationship batches,
cardinality, cycles and authorized graph reads.

`AccountingRebuildTests` demonstrates explicit recovery of derived accounting
rows from the unchanged register in a disposable database, rollback on failure
and refusal after month closing. It does not authorize automatic production
repair or establish concurrent closing/repair safety.

`CatalogPartsHierarchyTests` registers a test-only catalog to exercise part
replacement, omitted/empty parts, transaction rollback and trusted hierarchy
query helpers. Its table setup is fixture-local; production catalog changes need
immutable application migrations and an explicit parent validation/locking policy.

`DocumentListFilterTests` adds a registered provider filter with parameterized
values and preserves direct-column filtering and access checks.
`ReportStreamingExtensionTests` verifies custom export lifecycle with synthetic
rows, frozen inputs, cancellation and resource cleanup. These targeted examples
do not establish all provider implementations, concurrency or performance.

