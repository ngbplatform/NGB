# Document contracts

Public packages: `NGB.Platform.Core`, `NGB.Platform.Definitions`,
`NGB.Platform.Metadata`, `NGB.Platform.Application.Abstractions`,
`NGB.Platform.Persistence` and `NGB.Platform.PostgreSql` in their respective layers.

`DefinitionsBuilder.AddDocument` accepts a type code and builder configuration.
`DocumentTypeMetadata` describes head/part tables. `DocumentTypeDefinitionBuilder`
supports `AddDraftValidator<T>` and `AddPostValidator<T>` independently.
The generic document read/write service requires a `display` column in head
metadata and storage, even when a custom presentation is configured. Keep it
nullable if an incomplete Draft has no display value yet.
`IDocumentPostValidator.ValidateBeforePostAsync(DocumentRecord, CancellationToken)`
runs with a locked document and an active transaction. Read typed state through an
application-owned interface whose provider implementation uses the current unit
of work. Do not perform unrelated I/O inside the posting transaction.

Accounting, operational-register and reference-register handler bindings are
independent optional properties of `DocumentTypeDefinition`. A document requiring effects must use the relevant
engine's public contract and prove transactionality, retries and reversal history.
Standard Post requires a registered handler, but the handler need not produce
movements. A workflow-only document can explicitly register an
`IDocumentReferenceRegisterPostingHandler` whose `BuildRecordsAsync` completes
without adding records. Post validators, authorization, idempotency and lifecycle
processing still apply. Bind the handler in the document definition and register
its implementation as `IDocumentReferenceRegisterPostingHandler` in DI. This
binding supplies standard Post/Unpost/Repost actions automatically; do not also
register duplicate standard actions manually. No register definition or artificial movement is needed
solely for this lifecycle. The equipment/inspection example's
`InspectionWorkflowDefinitions` and `InspectionWorkflowPostingHandler` demonstrate
that composition through public contracts.

If none of these handlers is registered, automatic Post registration omits the
action. Even explicitly exposing standard Post through
`IDocumentActionDefinitionsContributor` is rejected by the lifecycle with
`doc.posting.handler.not_configured`. The example separately tests this missing
configuration, retaining Draft status and empty effects. This precise limitation
is recorded in `ai/vendor/known-limitations.json`; it does not prohibit successful
Post without movements. Never bypass dispatch or mutate lifecycle status directly.

Interactive callers use `IDocumentActionDispatcher.ExecuteAsync`, including the
action code, idempotency key and `ExecuteDocumentActionRequestDto`. The system
lifecycle port is for trusted server workflows and must not bypass interactive
authorization. `IDocumentService` exposes metadata, Draft CRUD, relationships and
effects. Inspect its resolved signature before implementing an endpoint.

Idempotent replay returns the original action result, including its original
document version; a client must refresh editor state before issuing a new action.
Compare the complete JSON values using `InspectionRequests.AssertEquivalentJsonAsync`
in the example. A stored replay can serialize object properties in a different order.
Reusing a key for a different request returns `document_action.idempotency_conflict`;
a new request with a stale expected version returns `document.version_conflict`.

Successful generic Draft head/part saves and actual common-header changes advance
the action version in the save transaction. Reload editor state after saving;
posting with a version read before that save must fail. This is action concurrency
protection, not optimistic concurrency for the save itself: generic document and
catalog updates have no expected-version argument in 3.4.0. Two stale editors can
still overwrite one another. Do not advertise lost-update protection or invent a
request field that the installed API does not validate.

Treat the returned version as an opaque concurrency token. A custom action that
updates the common header can advance it during both the save and action
completion. Use the returned/refreshed value; do not calculate `oldVersion + 1`.
Replay must retain the committed version and must not repeat the date change.

`DocumentRecord.DateUtc` is the registry's business date; `CreatedAtUtc` is the
creation timestamp. Generic `IDocumentService.CreateDraftAsync` initializes the
registry date from the current clock. A payload field such as `document_date_utc`
is application data and is not automatically bound to the registry date or initial
number allocation. Updating that payload field alone does not move postings made
from `DocumentRecord.DateUtc`. Trusted common-header changes use the existing
`IDocumentDraftService.UpdateDraftAsync`; any interactive workflow needs its
existing authorized application boundary. Do not silently add a date-sync handler,
rewrite historical dates/numbers, or claim that generic CRUD already supplies this
binding. See the explicit 3.4.0 limits in `ai/vendor/known-limitations.json`.

Completed replay refreshes the current actor's permissions before returning the
saved result. It requires an active authenticated actor, source-document View and
the registered action's authorization, including custom evaluators and current
facts. A result containing a created document also requires View of that document.
Denied or deactivated actors receive HTTP 403 even with the complete original
request and idempotency key. The SDK does not execute the handler, check the old
expected version or rerun action availability on a completed replay. The lifecycle
tests also verify that simultaneous identical requests return one execution and
that a denied replay does not prevent an authorized caller from receiving it.
Earlier 3.4.0 candidate failures remain recorded in the historical reports.

`PostgresHeadDocumentTypeStorage` supports a simple head table keyed by
`document_id`. More complex typed storage implements the public persistence
contract. Migrations and storage belong in the provider project. The independent
equipment/inspection example verifies the minimal contract; its test report must
identify separately which unit, HTTP, persistence and browser outcomes ran.

For a custom mutation with explicit permissions, pure availability rules, bounded
context facts and transaction composition, read [custom document actions](custom-document-actions.md).

## Numbering, lookup and document composition

Bind `IDocumentNumberingPolicy` through `NumberingPolicy<T>` and register the same
implementation by concrete type and interface in DI. The inspection policy assigns
numbers on Draft creation; the SDK allocates and formats them. Eight concurrent
creates produce distinct numbers, and failed Post, Post, Repost and Unpost retain
the original number. Do not allocate a number in an API controller or derive it
from the current row count.

Document pages use explicit `limit`, `cursor` and `includeTotal` parameters. Follow
the returned cursor without interpreting it, and keep search and other filters
unchanged between pages. The HTTP controller requires total-count opt-in even
though the DTO's constructor has a different default. Cross-type lookup accepts
`DocumentLookupAcrossTypesRequestDto` at `POST /api/documents/lookup`; its limit is
per type. Lookup filters types by the distinct `Lookup` permission. `View` alone
does not grant lookup, and a request with no permitted types returns an empty list.

`InspectionDerivationDefinitions` registers `example.reinspect` and a directed
ManyToOne relationship. The derivation's code is the action code; no prefix is
added. Dispatching it creates one numbered Draft, its domain relationship and a
standard `created_from` child-to-source edge in the same transaction. The latter
makes the chain visible to the shared Document Flow UI. The target inherits the
source registry date. With no derivation handler, it has no copied application
fields and is not automatically Posted.
An optional `NGB.Runtime.Documents.Derivations.IDocumentDerivationHandler` receives
the source and target in `DocumentDerivationContext` within that transaction.
Keep any prefill work inside the existing unit of work and register the handler
declared by the definition. This example intentionally uses no prefill handler.

The derivation requires source View and target View/Create. An unavailable action
is omitted from the editor action list; a direct unauthorized invocation is also
denied. Replaying the same authorized request returns the existing target. The
relationship service separately enforces allowed endpoint types and cardinality;
adding the same edge again is an idempotent no-op. Its trusted server methods are
not an interactive permission boundary. Use authorized application services for
interactive operations.

Graph reads require View and ViewFlow on the root. The returned graph excludes
inaccessible document types and their edges. Use explicit depth and node bounds.
A bidirectional relationship persists both directions; this is separate from a
head reference field's `MirroredDocumentRelationshipMetadata`. A mirrored field
must be a Guid document lookup on the head table with a registered relationship
and compatible endpoint types. The fixture checks rejection of an invalid String
mirror at startup; it does not claim field synchronization acceptance for that
invalid definition.

`ExtendDocument` adds bindings while preserving the original metadata. Extend
relationship and derivation definitions by their existing codes. The contract
tests verify preservation of endpoints, cardinality and identity after renaming,
and rejection of unknown extensions, duplicate identities and conflicting bindings.

## Head/part storage and separate validation boundaries

The `example.inspection-checklist` API example declares one head table and a
`checks` part. Use an explicit `PartCode`; the payload is keyed by that code, not
the physical table name. The provider owns an embedded immutable migration and
`ChecklistStorage`, bound through a provider contributor's `TypedStorage<T>`.
Register that implementation by concrete type and `IDocumentTypeStorage`. Its
methods require the caller's active unit of work. Creation inserts the typed head;
Draft deletion removes the head and its application-owned cascading child rows.
The standard parts writer replaces the submitted part inside the same transaction.

Omitting `Parts` preserves existing rows. Sending `checks` with an empty `Rows`
list clears that part. Clients cannot supply row `document_id`, unknown part codes
or undeclared columns. The checklist requires positive unique `line_no` values,
while descriptions and results may remain empty in Draft. Acceptance crosses the
provider's batch boundary with 501 rows and injects failure after real head, part
and audit writes to verify rollback. Do not issue independent commits from typed
storage, validators or posting handlers.

Draft validation has two public contracts. `IDocumentDraftValidator` receives
common registry fields, and the trusted Draft service also calls it for header
updates. The example prohibits future observation dates; it does not require a
finished checklist. `NGB.Runtime.Documents.Validation.IDocumentDraftPayloadValidator`
receives incoming payload and converted part rows during generic Draft create
and update. Register it as that interface in DI; `AddDraftValidator<T>` is for the
registry validator, not the payload validator. Missing parts on update are not a
request to clear them.

`ChecklistPostValidator` checks persisted completeness: at least one check must
have a description and a result, and every supplied check must be complete. A
false result is a completed observation, so it does not prevent recording the
checklist. `ChecklistApprovalPolicy` separately requires persisted `reviewed=true`.
The public `ApprovalPolicy<T>` binding and resolver describe and resolve a policy;
generic Post does not automatically invoke it. This example's Post validator
explicitly resolves and calls the registered policy before posting, and rejects
a missing binding. Keep that invocation in the same transaction as posting.

The review flag is this example's completion condition, editable under the normal
Draft permission. It is not a separate approver identity, signature or segregation
of duties workflow. Applications needing those rules must implement their own
authorized transitions and persisted state through supported contracts. The
example exercises both administrator kinds without manual grants. It exposes the
checklist through the generic API; its web fixture does not implement a tabular
checklist editor.

### Draft text limits

In SDK 3.4.0, document metadata `MaxLength` describes the field and checks schema
compatibility; the generic Draft writer does not enforce that length on input.
Map `ColumnType.String` to PostgreSQL `text`, even when metadata has `MaxLength`.
`varchar(n)` fails the SDK's physical schema validation. Enforce a database length
limit with `CHECK (char_length(column) <= limit)`. That constraint alone can produce
HTTP 500 for an oversized value, so also reject invalid input before persistence.
See [physical schema validation](persistence.md#typed-schema-validation-and-posted-document-guards).

Use `NGB.Runtime.Documents.Validation.IDocumentDraftPayloadValidator` in the
application Runtime project. Implement **both** `ValidateCreateDraftPayloadAsync`
and `ValidateUpdateDraftPayloadAsync`, expose the document's exact `TypeCode`, and
register it with `services.AddScoped<IDocumentDraftPayloadValidator, YourValidator>()`.
This interface is resolved from DI; `AddDraftValidator<T>` binds a different
contract for common registry fields and does not register a payload validator.

The working [ChecklistPayloadValidator](../examples/equipment-inspection/EquipmentInspection.Runtime/ChecklistPayloadValidator.cs)
validates optional head and part text on both paths. Its limit comes from the
same Definitions constant as metadata. Head `payload.Fields` values are raw
`JsonElement`s; match names case-insensitively, preserve omitted fields on partial
updates, and allow null/empty/whitespace when Draft permits them. The generic
String conversion accepts other JSON values as text; validate that converted
representation too. Part rows supplied to the validator are already typed.
Throw `NgbArgumentInvalidException` with the field name for HTTP 400; do not
truncate or silently normalize saved text.

Post completeness remains separate: an optional completion note may have a Draft
length limit and require nonwhitespace persisted text only at Post. A Post
validator cannot protect Draft create/update. Run HTTP tests for the exact limit,
one character over it on **both** create and update, nullable Draft values and
omitted-field updates. Rejected writes must leave saved fields, parts, version,
audit and document count unchanged. See
[ChecklistHttpTests](../examples/equipment-inspection/EquipmentInspection.IntegrationTests/ChecklistHttpTests.cs).

## Database protection of posted typed records

Install the platform's existing typed-document guards after the application's
head/part tables are created; see [typed schema validation](persistence.md#typed-schema-validation-and-posted-document-guards).
A correct HTTP lifecycle does not by itself establish database protection against
an incorrect trusted storage call. `TypedDocumentGuardTests` verifies that direct
typed deletion of a Posted inspection or checklist fails, rolls back and preserves
head/parts/audit, while public Unpost restores Draft editing. This is a provider
invariant check, not additional user permission or a new posting behavior.


## Interactive actions and trusted lifecycle ports

Use `IAuthorizedDocumentReadService` for interactive metadata, pages, lookups,
effects and relationship reads. `IDocumentActionQueryService` returns editor
state and action availability; this is a snapshot, so execution must still go
through `IDocumentActionDispatcher` with the version and idempotency contract.

The older `IDocumentService.ExecuteActionAsync` overload taking only a string
action code throws `DocumentActionsNotSupportedException` in the current SDK.
This does not mean document actions are absent. The standard controller uses
the dispatcher. Do not route a new adapter to the similarly named legacy method.

`IDocumentSystemLifecycleService` is explicitly for trusted seeders, migrators and
background workflows. Exposing it directly in an interactive adapter would omit
the dispatcher's user-facing action contract. Neither a stored technical success
nor construction of a request supplies current-user authorization.

## Trusted batch workflows and number formatting

Resolve `IDocumentDraftService` or `IDocumentPostingService` and inspect whether
the instance implements `IDocumentDraftBatchService` or
`IDocumentPostingBatchService`. The standard container registers the base
interfaces; do not assume the optional interfaces can be resolved directly.
These are trusted server workflows, not a public interactive batch API.

Draft creation accepts at most 1000 common-header requests with UTC dates. The
standard workflow retains registered validation, typed storage, audit and
transactional numbering. Optional provider batch ports can reduce round trips,
with the existing per-item fallback when unavailable. `manageTransaction: false`
requires an active caller-owned transaction, including rollback on failure.

Posting accepts at most 25 distinct IDs, deduplicates while retaining input order,
and applies the batch in one transaction. Optional prefetchers only prime immutable
posting reads in that transaction; they do not apply effects. A posting read cache
must remain operation-local and must not retain stale data across later edits.

`IDocumentNumberFormatter` receives type, fiscal year and reserved sequence. Keep
formatting deterministic and stable; it must not reserve a second number. A
type-specific decorator can retain `DefaultDocumentNumberFormatter` for other
types. The batch allocator owns reservations by type/year in the Draft transaction.
Typed header synchronization also stays in that transaction.

`DocumentBatchTests` creates two workflow-only inspections under an external
transaction, then rolls back and confirms that the next batch reuses its reserved
numbers. Its formatter adds an example prefix only for inspections. An incomplete
second document rolls back the first Post; after correction both post, duplicate
IDs are ignored and repetition retains Posted state. Separate checks exercise
the batch limits, empty input and cancellation. These small examples establish
atomic lifecycle behavior, not throughput or all combinations of register effects.

## Runtime action and relationship internals

The startup action registry combines standard actions, declared derivations and
application contributors. It validates handler identities and presentation order;
advertising an action does not authorize its execution. Keep interactive calls
on the action dispatcher. The derivation service creates a target Draft using the
registered relationship semantics; it does not automatically post that target.

Relationship repositories and graph readers are lower-level storage/read services.
Document-flow access must retain current document permissions and registered
relationship rules. The action-execution repository stores request identity,
fingerprint and a recorded result; it does not itself reauthorize a replay. The
action dispatcher checks current authorization before returning that result.
Storage idempotency alone does not prove current authorization.

## Batch relationships and provider list filters

The resolved `IDocumentRelationshipService` may implement
`IDocumentRelationshipBatchService`. `CreateManyAsync` accepts at most 1000 input
requests, checks registered types, Draft requirements, cardinality and cycles,
then writes relationships and audit in one transaction. Repeated identical edges
do not create extra audit records. With `manageTransaction: false`, the caller
must own an active transaction and roll it back on failure. This is a trusted
server operation; do not expose it as an unauthenticated bulk endpoint.

`DocumentRelationshipBatchTests` verifies caller rollback, duplicate edges,
existing cardinality conflicts, a cycle entirely inside a proposed batch, bounds
and cancellation. Its HTTP graph check still requires document-flow permission.
The fixture does not establish concurrent relationship throughput or every
bidirectional topology.

A declared `DocumentListFilterMetadata` maps directly to a head column when
configured or when its key matches that column. Otherwise the PostgreSQL reader
asks `IPostgresDocumentListFilterSqlContributor` implementations in registration
order. Match the exact owned document type and filter key; return false for other
filters. Construct SQL expressions from trusted provider code and bind values
with `DynamicParameters`/`PostgresDocumentFilterSql`. Never concatenate a user
filter value into SQL. Register the contributor in the provider/host layer.

`DocumentListFilterTests` supplies an explicit case-insensitive result filter,
verifies a literal SQL-looking input returns no matches, rejects undeclared keys
before the contributor, preserves direct-column filtering and denies an ungranted
reader before query execution. Query parameters use `filters.result_label` (or
the plain key), not bracket notation. Adding the contributor alone does not
advertise a filter; declare it in the application's original document metadata.

## Withdrawal and dependent documents

A Reference Register handler builds register records. It is not a universal
lifecycle validator. In 3.4.0, Unpost returns from the RR branch before resolving
the handler when no register write state exists for the document. An empty
handler can enable Post but cannot be relied on to reject withdrawal. Repost
also has different semantics; do not treat it as an Unpost callback. Never create
fake RR records to force a business guard to run.

Define correction rules from the business workflow and prove them through the
actual authorized action/API path. Rejected withdrawal must preserve status,
version and all financial/operational effects. Include a dependent Posted record,
repeat requests and a competing dependent write using the same locking policy.
A unit test calling the guard directly is insufficient.

For custom actions, context facts and availability evaluators are rechecked by
`IDocumentActionDispatcher` inside its transaction. Mutation-time invariants must
also be checked by the supported application handler under the relevant locks;
UI action availability is only a snapshot. Follow [custom actions](custom-document-actions.md).
Do not assume a custom action guards the standard Unpost/Repost routes or trusted
system lifecycle calls. The current registry creates standard actions first;
adding another definition under the same type/action key is a duplicate, not an
override API. Inspect supported contracts for every reachable path. If a required
guard cannot be installed through them, report that concrete limitation and keep
acceptance incomplete instead of inventing a hook or changing Platform implicitly.

## Visible Document Flow

The shared Document Flow UI displays standard relationship codes `based_on`,
`created_from`, `reversal_of`, `related_to` and `supersedes`. Select one according
to the business relationship and preserve its source/target direction. Register
compatible endpoint types/cardinality and create the edge in the same transaction
as the linked document, using a derivation, supported head-field mirror or the
public relationship service as appropriate. A document lookup by itself is not
an edge.

A custom domain relationship may coexist with a standard flow relationship. It
must not replace the standard edge if the chain should appear in the shared UI.
Do not broaden shared UI filtering merely because a generated application used
only custom codes. For a new derivation, use the semantic standard relationship;
for an existing persisted domain relationship, preserve its code and add the
standard edge through the authorized workflow rather than renaming history.

Verify a source/child/grandchild chain both in the authorized graph response and
in the actual browser page, including link navigation and excluded inaccessible
nodes. A passing graph API test alone cannot prove visible hierarchy.
