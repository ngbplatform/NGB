# Operational and reference registers

Target: NGB 3.4.0. Inspect the artifact-derived
[OperationalRegisters](contracts/NGB.Platform.OperationalRegisters.json),
[ReferenceRegisters](contracts/NGB.Platform.ReferenceRegisters.json),
[Definitions](contracts/NGB.Platform.Definitions.json) and
[Runtime](contracts/NGB.Platform.Runtime.json) symbol inventories.

The two engines have different semantics. Select the engine from the business
requirement before modeling dimensions and resources. Register definitions and
provider schema through public contracts; keep physical storage in PostgreSQL.
Do not create a substitute generic effects table.

For document-driven Operational Registers, implement
`IDocumentOperationalRegisterPostingHandler.BuildMovementsAsync(DocumentRecord,
IOperationalRegisterMovementsBuilder, CancellationToken)`. The handler's TypeCode
must match its document definition. Supply movements for Post/Repost. The existing
engine handles Unpost with appended storno movements; do not delete old movements.

For document-driven Reference Registers, implement
`IDocumentReferenceRegisterPostingHandler.BuildRecordsAsync(DocumentRecord,
ReferenceRegisterWriteOperation, IReferenceRegisterRecordsBuilder,
CancellationToken)`. The operation is significant: Post, Repost and Unpost can
require different records. Reference-register history is append-only; a logical
deletion is represented by the supported tombstone semantics, not physical removal.
`IReferenceRegisterIndependentWriteService` is a separate write path; do not use it
to bypass the document lifecycle or its transaction.
Read [independent writes and temporal semantics](independent-reference-registers.md)
for transaction ownership, command identity, periodic buckets, as-of bounds and
tombstone-aware paging. Its example exercises public services in a trusted fixture;
application HTTP/UI authorization remains a separate responsibility.

In 3.4.0 the standard PostgreSQL provider distinguishes automatic cancellation
from intentional deletion for periodic `SubordinateToRecorder` registers. Its
internal `__is_recorder_cancellation` column is not a resource or a public DTO
member: handlers keep the existing contracts and must not populate this flag.
Readers resolve the latest recorded version per exact effective point before
selecting the active schedule. Moving a replacement to an earlier date must not
let an obsolete cancellation hide it. Explicit deletion still ends the value;
historical recorded-time boundaries and repeated Repost idempotency are preserved.
Post also supersedes points left by an older Unpost. The generic Unpost fallback
pages the full recorder history, including future effective dates. Custom providers
must implement equivalent read/write semantics themselves. Independent and
nonperiodic registers keep their existing contracts. Follow [upgrade rules](upgrades.md#reference-register-migration-in-340)
before running new hosts against an existing database.

Operational register read, projection and finalization contracts and reference
register read/management contracts are in Runtime and Persistence. Confirm bounds,
period buckets, ordering and cursors in the installed signatures. Build dimensions
and resources consistently with declared metadata. Changing schema after records
exist may be rejected; document the supported migration instead of suppressing
the engine's schema-drift check.

Acceptance must independently assert balances/turnovers or current reference
state, original history, retry behavior and reversal results. Test two competing
operations, failure rollback and a batch larger than one page. Measure provider
query counts and compare results across page boundaries; a unit test of a builder
does not establish transactional correctness or projection performance.

The [equipment usage example](../examples/equipment-inspection/README.md#equipment-usage-and-operational-registers)
contains an application document, Post validator, operational-register handler,
provider reader, migration and explicit Setup command. Its tests exercise actual
movement history, retry, Repost, Unpost, rollback after a write, and monthly
projection reads with two equipment dimensions. The shared Effects page displays
the public API result. Monthly projection tests invoke the finalizer explicitly;
this is not evidence of a scheduled worker or a volume/performance profile.

The [equipment assessment example](../examples/equipment-inspection/README.md#equipment-assessments-and-reference-registers)
implements a nonperiodic Reference Register subordinate to a document. Its handler
emits records for Post/Repost and leaves Unpost to the engine's recorder tombstones.
Read calls supply the recorder ID and paginate history/current keys using the
returned cursors. This does not implement a global latest-state register across
documents. Reference-register dimension IDs must correspond to
`DeterministicGuid.Create("Dimension|" + normalizedDimensionCode)`; use the same
value in the migration and runtime metadata, not an arbitrary UUID.

## Operational-register administration and health

`IOperationalRegisterAdminEndpoint` provides JSON-friendly metadata, physical
health and finalization operations. It is a trusted runtime facade: its name does
not imply current-user authorization. An interactive host must establish its
existing authorized boundary before invoking it. The reference application tests
these services directly and does not expose a new administration endpoint.

Inspect register list/details and the health of movement, turnover and balance
tables, including missing columns/indexes and the movement append-only guard.
Unknown register details/health/ensure return null. Ensuring physical schema does
not create movements. Use `EnsurePhysicalSchemasByIdsAsync` on the maintenance
service for targeted setup: the default batch implementation deduplicates IDs,
ignores empty/unknown IDs and leaves unrelated registers untouched. The extension
also supports non-batch implementations by calling them individually; do not
assume every custom implementation has the default batch normalization.

Read finalization markers using month-start periods and bounded limits. Dirty
months can be finalized per register or across registers; already finalized work
does not count again. The standard provider includes a default projector for new
registers. `BlockedNoProjector` is a defensive state for a host missing both an
applicable custom projector and the default registration, not the normal state
of every newly created register. After repairing that host configuration, explicitly
mark blocked months Dirty before retrying. Do not delete or edit marker tables.

## Transactional resource nets

Use `IOperationalRegisterResourceNetReader` when a trusted operation needs current
resource nets inside its existing unit-of-work transaction. It supports one or
several dimension-set IDs, one dimensional filter, and a batch of independent
dimensional filters with an inclusive as-of date. The batch filter result preserves
input order; missing matches yield zero. Set-ID batches deduplicate their IDs.
Specify a registered resource column code; do not derive identifiers from user
text. Empty filter groups and unknown resource columns are rejected.

These reads account for storno and do not require a preceding monthly finalization.
They are not permission-aware APIs. The
[administration and net-read example](../examples/equipment-inspection/README.md#operational-register-administration-and-nets)
verifies selective/idempotent schema ensure, default and missing-projector states,
bounded administration calls and two equipment dimensions with independent nets.
It posts 4 and 5 hours through HTTP, then unposts one 2.5-hour document and verifies
1.5 and 5 through all four net-read methods. Historical bounds exclude later
movements. This small functional fixture does not establish load/query-count or
concurrent-finalization performance.

## Dimension net pages and movement history

`IOperationalRegisterMovementsQueryReader` serves trusted reporting/read-side
operations. Its month-window resource nets and cumulative balances group by one
dimension value. Both require month-start `DateOnly` bounds, omit zero nets and
account for appended storno. Positive nets precede negative nets, with value UUID
ordering within each group. Rows include the value's display text. Offset pages
carry the filtered row count, positive sum and absolute negative sum; in the
default provider an empty offset page has zero totals, so do not replace previously
known totals with those of an out-of-range page.

For cumulative balances, prefer `GetResourceBalancesByDimensionCursorAsync` for
continuation. Start with a null cursor and use `HasMore`. Construct the next
`OperationalRegisterDimensionResourceNetCursor` from the last row's sign and
`ValueId`, the consumed row count, and the returned totals. The default PostgreSQL
provider seeks by sign/value and carries the first page's totals; the interface's
fallback implementation uses `NextOffset`. These are trusted continuation values,
not an authenticated HTTP cursor or a consistent snapshot across concurrent
writes. Restart paging if the register, resource, grouping dimension, filter or
month changes. An interactive adapter must enforce access and validate/bind its
own continuation data.

Cumulative reads can combine a finalized balance snapshot with later movements;
they also work before finalization. Bounded materialized helpers are suitable only
for known small results. Explicit pages avoid depending on their materialization
ceiling. The independent test compares movement-only, snapshot-plus-roll-forward
and fully finalized results, including negative values and a zero after reversal.

Movement history has two distinct time/order contracts. `GetByOccurredAtPageAsync`
and `GetByOccurredAtCursorAsync` accept inclusive exact UTC dates and order by
occurrence time, then movement ID. Carry both fields in
`OperationalRegisterOccurredAtCursor`; several records can share one timestamp.
The cursor method returns rows without `HasMore`: an adapter can request one
extra row, display its bounded page and continue from the last displayed row.
Use a bounded limit; a null offset-page limit explicitly disables paging.
In SDK 3.4.0, a null cursor starts the first bounded occurrence page. Continue
from the last displayed row's time and movement ID; no sentinel timestamp/ID
or offset-page workaround is needed. PostgreSQL parameters have explicit types,
including for the first request. See
[`operational-registers.null-occurrence-cursor`](../known-limitations.json).
`GetByMonthsAsync` instead requires month-start bounds and seeks by movement ID,
with optional dimension-set, document and storno filters. `GetMaxPeriodMonthAsync`
returns the latest matching month or null when there are no matching facts or
the registered table is not present. Dimension filters use AND semantics.

The [dimension paging example](../examples/equipment-inspection/README.md#operational-register-dimension-paging)
verifies equal-time continuation, exact-date bounds, differing month/occurrence
ordering, enriched dimensions, bounded pages, retained storno and maximum-month
filters. Its signed facts are seeded through the public technical applier against
real catalog/document records in disposable storage. This isolates reader
semantics; it does not establish an interactive posting rule, user authorization,
large-volume performance or concurrent-snapshot consistency.

## Reference-register administration and health

`IReferenceRegisterAdminEndpoint` exposes list/details and physical-schema health
through stable runtime DTOs. Like the operational-register facade, it is trusted
infrastructure and does not establish current-user authorization. Keep interactive
access behind the application's existing authorized boundary.

Health reports compare the records table, expected fields/indexes and append-only
guard with the registered metadata. Unknown register details/health/ensure return
null. Use `EnsurePhysicalSchemasByIdsAsync` for selected registers; the default
batch implementation deduplicates IDs and skips empty/unknown IDs. An adapter
without the batch interface instead receives individual calls from the extension,
so custom implementations must define their own normalization.

Normal management `UpsertAsync` ensures the table immediately. The
[administration example](../examples/equipment-inspection/README.md#reference-register-administration)
uses public persistence ports to model interrupted setup with metadata but no table.
For independent and subordinate nonperiodic registers, it verifies diagnosis,
selective ensure, repeated ensure and whole-report consistency without creating
records. This fixture does not claim every periodic index variant, damaged-schema
repair case or performance bound. Do not repair register storage with ad hoc SQL.

## Register-specific month projectors

Register a scoped `IOperationalRegisterMonthProjector` through host composition.
Its `RegisterCodeNorm` identifies the register; the runner trims and lowercases
that value and rejects empty or duplicate normalized codes. A matching custom
projector takes precedence over the default. Other registers retain the standard
fallback. Legacy `IOperationalRegisterMonthFinalizer` is adapted by the runner;
new extensions should use the projector interface.

`RebuildMonthAsync` receives registered identity, month-start period, UTC execution
time and a movement reader in `OperationalRegisterMonthProjectionContext`. A
transaction is already active. Use the same scoped unit of work and injected
provider-neutral projection stores. Do not begin a separate transaction, commit
the runner's transaction, or publish external side effects from this hook.
Use bounded reads and define the resource and cumulative-balance semantics of a
custom aggregation explicitly.

The runner publishes the Finalized marker only after the projector succeeds.
An exception rolls back its owned transaction, including changes to projections,
and preserves Dirty state. Callers who explicitly supply an external transaction
remain responsible for its rollback. A custom projector can delegate to
`IOperationalRegisterDefaultMonthProjector` when retaining standard aggregation.
The default's optional preparation path has additional transaction/lock conditions;
it is not automatically used for a register with a custom projector.

`CustomOperationalRegisterProjectorTests` posts 2 hours, verifies the custom hook
and default fallback for another register, then posts 1.5 more hours. An injected
failure after projection writes preserves the earlier 2-hour result and Dirty
marker; a subsequent successful run publishes 3.5 hours. Unposting the first
document leaves 1.5 hours. These are real provider transactions, with no sleeps
or test retries. The test deliberately delegates aggregation to the standard
projector; it does not claim a new aggregation algorithm or load certification.

The packaged `MovementsCountProjector` is a separate demonstration that requires
a `movement_count` resource. It publishes monthly net counts into both tables;
it is not a generic cumulative-balance implementation and is not registered by
default. Trusted finalization services are maintenance boundaries, not substitutes
for current-user authorization at an interactive endpoint.
