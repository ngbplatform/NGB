# Business outcomes that generation must prove

Use this contract when translating a business prompt into architecture, editors,
reports and acceptance. These decisions belong to the generator. Do not ask the
user to choose registers, cancellation hooks, request counters or test machinery.
Do not add technical implementation requirements to the business specification.

Before implementation, extract every required business outcome into
`ai/acceptance.json` schema 2. Give it a stable ID, a short business description,
the applicable scenarios below and executable case bindings. No required outcome
may be deferred to a limitation paragraph and then counted as complete. A missing
public contract is an explicit unfinished requirement, not permission to invent
an API or weaken the business rule. Keep these expectations independent of the
implementation: a test that expects the existing bug is not acceptance.

## Search and presentation

The shell's search box must search authorized records, not just menu labels.
Preserve the starter `searchApplication` adapter and its `/api/application-search`
controller, or supply an equally complete authorized adapter. `INgbSearchService`
returns `NgbSearchResult`, not the palette DTO: explicitly map source kinds,
identities and routes, including the specialized journal route. Pass cancellation
through, bound requests and retain server permissions. A catalog reader must have
both view and lookup permission on that catalog to find its records in search.
Verify each permission combination with separate test actors. Do not query unrestricted
repositories or preload all records into the browser. A source failure must not
be represented as a successful empty search response.

For each document/catalog, search a known number/code and a human name where
supported, open the exact resulting identity and verify denied records are absent.
For a catalog with a separate business code, register its scalar field in the
API host with `builder.Services.AddSingleton(new CatalogCodeField(catalogType, fieldName))`.
The supplied `CatalogCodeSearch` combines an exact, active-only code lookup with
the standard search and retains the record's normal display name. Use the same
case convention for entry and lookup when the business code is normalized.
The example registers and indexes an optional equipment code and its browser
test finds an item by a code that never appears in its name. Search report
names too. Exercise prefixes, replacement queries and keyboard navigation with
the actual palette. The equipment example includes an executable adapter and
Playwright tests against its real API, PostgreSQL and Keycloak.

Report descriptions are one short business sentence, normally 60–120 characters
and never more than 160 characters. Explain purpose rather than formulas, all
exclusions or implementation. Put calculation rules in report help or business
documentation. Check the actual header and settings drawer; shrinking fonts or
truncating every description in the SDK is not a substitute for concise content.

## Withdrawal and effective history

Traceable history does not imply an unconditional ban on Unpost. In an open
period, an unused erroneous transaction must have a usable correction path.
Test allowed Unpost separately from a refusal caused by an actual dependency.
For a future unused rate, prove withdrawal and the value that applies afterward.
Use `lifecycle` for successful Post/Unpost/Repost and `withdrawal-blocked` for
the separate refusal, unchanged effects and subsequent correction path.

For periodic prices/rates, choose a recorder-aware schedule or explicit independent
compensation according to the supported read contracts. Withdrawal must restore
the previous applicable value for the affected interval without disturbing later
unrelated changes or snapshots on existing transactions. An independent tombstone
ends a value; it does not automatically restore the previous price. Preserve
provenance, recording history and idempotency; serialize competing changes to the
same business key. Test same-date corrections, backdating, future changes,
withdraw/repost retries and rollback. See [Reference Registers](registers.md).
Use `temporal-restoration`; a test that merely asserts a 409 cannot satisfy it.

Review prerequisites as well as explicit document links. A posted operation may
have relied on a balance, security, reservation, approval or limit without storing
a direct reference to the transaction that supplied it. Withdrawing that supplying
transaction must not silently invalidate the operation. Define the invariant,
check it under the same transaction and business-key lock as the competing action,
and show a concrete reason plus a permitted recovery sequence. Test a dependency
without a direct link, unchanged state on refusal, and successful withdrawal after
the prerequisite has been released or replaced (`prerequisite-withdrawal`).

## Dependencies and complete calculation context

For every controlling input, enumerate **all** dependent header fields, lines,
lookups, applied rates, exemption flags, fees and totals. Clear the whole chain
synchronously before a request. Replacement and clear are different test cases.
Use one request identity across every await, including the final display lookup.
Ignore late success and failure after clear, replacement, date change, document
reload, readonly transition and unmount. Abort alone is insufficient.

Follow `DerivedLinesEditor.vue`, not a watcher that only resets lines. Its browser
tests assert the whole draft; `InspectionSourcePicker.vue` additionally demonstrates
two real authorized HTTP stages. In generated acceptance, verify every dependent
field after each stage resolves, including failures. Checking a placeholder,
empty row count or lack of an exception alone does not prove header invalidation.

Preview and Post must use the same complete business context: document business
date, source, customer, jurisdiction, currency, exemptions and other applicable
inputs. Do not omit an already-known date and let the server default to today.
Use one authoritative calculation service for preview and posting. Change the
draft date across an effective-rate boundary, verify the new preview, post and
compare the saved amounts and effects. Reopen the posted snapshot after a later
rate change and prove it remains unchanged. These are the `pricing` outcomes;
a pure arithmetic unit test cannot replace them.

Resolve the document business date before allocating a year-scoped number.
If numbering occurs on creation, the creation path must receive the intended
date; changing a typed payload date after allocation is too late. Use an explicit
policy for changing an already-numbered draft's year; never silently renumber an
issued document. Seed through the same business path. The `numbering-date` scenario
checks both sides of a year boundary, timezone conversion, saved/reopened date
and number, and seeded documents. See [document identity](documents.md#document-identity-and-dates).

## Complete aggregates and historical state

A dashboard total cannot be a sum of one paged list. Prefer an authorized
server-side aggregate over the same filtered dataset as the report. Preserve
permissions, business-day boundaries and currency. A report's grand total may be
emitted only on its final page: never assume it is present on page one. If the
contract requires cursor traversal, follow it with a stable context and explicit
bound/error behavior; never hide incompleteness by raising `Limit` above the SDK
cap or loading all history into the browser. Seed more than the real page cap and
reconcile the dashboard with an independent expected total (`dashboard-complete`).

An as-of report must evaluate every time-dependent condition at that time, not
combine historical amounts with today's posted/deleted/retired status. Distinguish
effective time from recording time and specify which historical question is
answered. Save the expected result before a later return, retirement, correction
or withdrawal; execute the same earlier cutoff afterward and compare identities,
statuses and totals (`historical-as-of`). Current-only data may be explicitly
labelled current, but does not fulfil a requested historical report.

## Cross-document processes

### Linked documents and settlement

List each business edge, its direction, cardinality and the operation that writes it.
Include creating a document manually with a source lookup as well as deriving it
through an action. Use a registered standard relationship for a supported head
mirror. For a line reference or another source outside head mirroring, synchronize
the persisted relationships through the application's transactional write path.
Replacing or removing a source removes the obsolete edge. Multiple lines pointing
at one source retain the edge until its final reference is removed. Verify the
direct links and one complete multistep chain in the browser using saved IDs.

Keep the amount originally issued as a snapshot. When the brief requires a balance
on a posted document, read settlements from the authoritative ledger at the requested
cutoff. Show the original amount, applied amounts and remaining balance with
unambiguous labels. Open the actual settlement from the record. Test a partial
settlement, full settlement with zero due, and reversal or withdrawal as permitted.
Reopen the original and reconcile its figures with the ledger and statement.

### Partial adjustments and historical grouping

Allocate adjustments at the original line's currency precision against its frozen
base and tax. For a proportional allocation, compute the target cumulative tax
for the newly adjusted base and subtract tax already allocated. Assign the final
eligible residual from the original line. For example, tax of 1.51 on 20.10 split
into two equal credits at 7.5% allocates 0.75 and 0.76 under nearest-cent rounding.
Verify both postings, the final zero balance, and a retry. Check the limit and write
the allocation in one transaction under the same business-key lock as competitors.
Use the specified rounding and allocation policy for each currency and tax component.
The [minor-unit reference calculation](partial-adjustment.mjs) provides an
independent expected amount for a frozen half-up policy. Compare the application
postings and ledger to that expectation in acceptance tests.

For a historical report grouped by a mutable classification, persist the applicable
dimension at posting or use a dated relationship that resolves as of the transaction.
Post a known amount in group A, change the catalog to B and post a later amount.
Re-run the earlier period. Its source IDs, original group and total must agree with
the recorded expectations. Reconcile the later period separately.

### Dashboard scope, roles and competing operations

For every clickable figure, define its population, date interval, status, unit,
currency and permission. The dashboard and target query must apply the same rules.
Seed at least one in-scope and one out-of-scope record. Open the tile and check the
actual execute request, exact included identities or totals, and the exclusion.
Omit restricted figures from the server response for that role and hide unauthorized
links. Exercise direct navigation as well. Reconcile all figures visible to the administrator.

For each distinct operation that can contend for the same stock, balance, original
line or timeline, start independent requests against a shared fixture. Verify the
permitted results and the persisted aggregate after reopening. For each idempotent
write, retry the same payload and key, then change the payload under that key. Assert
the original execution identity, the concrete conflict and exactly one committed
effect. Name each operation in the plan so a passing race for one write cannot
satisfy the requirements for another.

## Completion

Run the [browser outcome gate](browser-acceptance.md), applicable integration tests
and required application profiles on the current sources. Complete all required
business rows; report failures and not-run checks explicitly. Coverage and green
test counts do not substitute these outcomes. Neither the schema nor the helper
can infer the correctness of arbitrary business assertions: the generator must
derive test data and expected values from the prompt, review the assertions, and
fix the application until they pass. Do not run FULL Platform Certification merely
to validate generated application behavior.

Use [server acceptance](server-acceptance.md) to exercise source membership, completed
cycles, measure-specific cutoffs and recurring work beyond the effective batch limit.
