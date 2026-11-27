# Server acceptance for dependent operations

Before implementation, identify the operations that consume a prerequisite, select
a source, calculate a historical position, or repeatedly process eligible work.
In schema 2 of `ai/acceptance.json`, bind each applicable check in `businessChecks`
to the exact `testName` of a real integration test in VSTest's TRX. Use an empty
array for an application without these behaviors. The CLI checks the bindings
during the integration profile and reconciles metadata during the E2E profile.
Run both profiles or the combined `all` profile.

A binding has `kind`, `target`, `key` and `test`. For example:

```json
{"kind":"source-membership","target":"document:example.approval","key":"request_id","test":"Example.Tests.ApprovalTests.Matching_request_is_required"}
```

Use a real application host, disposable storage and an actor authorized to
perform the operation. In the bound xUnit test, inject `ITestOutputHelper`.
After asserting the outcomes, write a single line prefixed with `NGB-CHECK `,
followed by a JSON object with the same `kind`, `target`, `key` and the observations
below. For example, serialize an anonymous object with `JsonSerializer.Serialize`
and pass the prefixed string to `output.WriteLine`. Read statuses, identities
and snapshots back from the running application. Derive expected results from the
business specification. Use synthetic IDs and values in evidence. The checker
takes output from passing tests in the current run and checks the actual
observations against the expectations. It cannot establish how an application
test obtained a value. Review the fixtures and test bodies as well.

## Source membership

For each header or line business reference, exercise the server's save or post
boundary with two persisted, authorized sources of the same type and lifecycle
status. Both should be plausible choices. Change only the referenced source so
the second source belongs to another owner, transaction, line or state transition.
Hold other selectors constant. Validate the resolved source and line under the
transaction that commits the change. Reopening Document Flow checks the saved edge
separately. Editable and derived fields both require membership coverage.

Use the metadata field path as the key. Capture `correctSource`, `otherSource`,
`sourceType`, `otherType`, `sourceState`, `otherState`, `correctStatus`,
`otherStatus`, the actual `reason` and the independently expected `expectedReason`
(use a stable business error code where available). Compare `beforeRefusal` and
`afterRefusal` objects containing the affected status, relationships and effects.
If the business rule genuinely allows interchangeable sources, declare an
`unrestrictedReason` on the binding and verify both choices succeed. Purely
informational references follow the existing metadata classification.

## Withdrawal after a completed cycle

Each withdrawable document declares `withdrawalDependencies`, an array of
business keys describing the dependencies its posting can supply. Use an empty
array when there are none. For each key bind `withdrawal-cycle` to the document.
Keep the ordinary unused withdrawal and live-consumer tests as well.

Run a later use and release on the same business key. Where the brief protects
chronology, a return to the original balance or status cannot erase the dependency.
Check later effective events or recorded provenance under the same lock as
withdrawal. Record separate IDs for `prerequisite`, `laterUse` and `laterRelease`,
the `initialState` before the later cycle and the matching `restoredState` afterward.
Attempt withdrawal and capture `refusalStatus`, `reason`, `expectedReason`,
`beforeRefusal` and `afterRefusal` including later history and effects. Exercise
the authorized correction sequence in business order, assert its final effects
and capture its `recoveryStatus`. Apply this classification to dependencies that
survive release according to the brief. A process allowing withdrawal after
release still needs its ordinary prerequisite and successful release tests.

## Historical cutoff

Bind `historical-cutoff` to each applicable report and each of its measure codes.
For detail-only reports use a stable row identity or status as the key. The CLI
recognizes standard as-of parameters and reconciles measures from report metadata.
Declare custom cutoffs from the brief too. Include every time-dependent condition,
especially existence of later documents that can change a derived amount even
when the underlying balance is unchanged. Filter the existence test by the same
effective cutoff as the movements. Preserve separate business and recording dates.

For each measure capture `cutoff`, `repeatedCutoff`, independent `expected`,
and actual `before` and `after` at that exact cutoff. Between executions commit
the later event and capture its ID, successful response and effective timestamp
as `laterEvent`, `laterStatus` and `laterEffectiveAt`. Record `laterCutoff` from the
second report request. For calendar dates, bind the report's configured `timeZone`
in the obligation and emit it with the first instant after the earlier local day
(`cutoffExclusive`, an ISO timestamp with offset). Include an event inside the
day and an event on the exclusive boundary. Verify midnight using the configured
zone on short and long DST days. A date-only effective value represents a local
business date. For instant-based reports bind and emit `timeZone: null` and use
offset-qualified timestamps for the cutoffs and event. In both modes the later
read must include the event.
Read the later period as a positive control and include its independent
`laterExpected` and observed `laterActual`. Choose a later event whose effect is visible.
When a total stays constant, include a changing detail ID, count or lifecycle state
from the same read in both expected snapshots. Zero is a valid earlier or later value.
One scenario may emit several observations when it actually reads every measure.
The `Posting_a_later_inspection_preserves_the_count_at_the_earlier_cutoff` example
in `InspectionReportTests.cs` executes both cutoffs against the report endpoint
and emits the observed counts from the test.

## Effective prerequisites and uniqueness in history

List each required dated dependency in the document's `effectivePrerequisites`.
Bind an `effective-prerequisite` check to its key. Post the prerequisite with
a later effective date and attempt the dependent operation at an earlier date.
Read the persisted status, movements and balances before and after refusal. Run
the same authorized operation with sufficient resources at a permitted date.
Report `attemptAt`, `prerequisiteAt`, `controlAt`, `controlStatus`,
`refusalStatus`, `reason`, `expectedReason`, `beforeRefusal` and `afterRefusal`.

List effective-key uniqueness rules in `uniqueHistoryKeys` and bind each to a
`history-conflict` check. Apply the whole key, applicable status, effective date
and allowed correction exclusions before limiting a query, or exhaust its pages.
Seed a conflict behind a full page of newer distinct rows. Read back their
`newerIds`, the original `conflictId` and the actual `pageLimit`. Capture the
same refusal and control evidence as above, omitting the three date properties.
Where concurrent writes are possible, enforce the rule inside the transaction.

## Business workload budgets

For a scale or latency requirement bind a `capacity` check to the actual
document, catalog, report or navigation workload. Set `budget.populations`
by meaningful entity, plus `samples`, `p95Milliseconds` and `maxQueries`
from the requirement before the run. Seed and read back the persisted population.
Measure each authenticated request through the response body, including at
least one database read. Count database commands in that request's trace.
Emit actual `populations` and the array of `{status, milliseconds, queries}`
on the passing integration test. The CLI computes the percentile and checks
each query count. The starter's role/menu load profile covers only those reads.

For a batching or per-row query requirement, bind a `query-growth` check to each
ordinary operation in scope, including line pricing, dependent lookups and editor
reads. Seed enough eligible rows and warm the authenticated route. Read two
different page or document sizes with the same actor, filters and operation.
Fetch related values in a set-based read and match them to the requested rows
by key. Verify the identities and values as well as the number of rows.
Count SQL commands within each request trace, including serialization of the result.
The size of the whole database belongs to the separate capacity workload.

Capture a `budget` such as `{"smallRows":1,"largeRows":12,"maxAdditionalQueries":0}`
in the obligation and copy it to the bound check. Emit `small` and `large`, each
with `actor`, `operation` (the binding key), HTTP `status`, actual `returnedRows`
and `queries`. Bind the fields required by the brief in `expect.valueFields`
(for example `["number","result"]`) and emit the same list as `valueFields`.
For each sample emit ordered `expectedRows` from independently seeded records
and `actualRows` projected from the measured response. Each row contains its
distinct `id` and the named business fields. Use synthetic values safe for test
reports. Derive fixture ordering from the contract and include distinct labels
so a constant or swapped value fails even when IDs and query counts match.
Choose the permitted increase before implementation and keep it
below one additional command per added row. The working source lookup in
`InspectionSourceTests.cs` exercises the same form endpoint at both sizes.

## Bounded recurring work

Keep the starter worker-host integration test that reads `IBackgroundJobCatalog`
from the actual host. It emits a `worker-inventory` observation containing application
job IDs after excluding `PlatformJobCatalog.All`. On upgrade, bring that inventory
check into the existing worker-host test. Classify every application job. Use a
`worker-backlog` binding with target `job:<registered-id>` and a stable effect key
for each recurring batch effect. For single-operation jobs use the optional
`nonBatchJobs` array of objects with `id` and a specific business `reason`.

Seed more distinct eligible sources than the configured batch limit, leave the
already processed prefix eligible, and execute the real worker repeatedly. A
stable cursor with a unique tiebreaker or selection of still-pending work can
advance past that prefix. Preserve idempotency across retries and task resolution.
Capture `batchLimit`, the complete seeded `eligible` ID array, the number of
actual `runs`, and all persisted delivery keys in `delivered`. Run it once more
and capture `afterReplay`. Follow pagination to the end and retain duplicate IDs
in both observed arrays. Assert resolution behavior wherever the brief defines it.

## Trace the original brief

### Capture the obligations before implementing

Start from the requirements supplied by the project owner. Inventory all authoritative
Markdown and text files, including referenced appendices. Save their project-relative
paths as a JSON array in `ai/brief-sources.json` before extracting any obligations.
A one-file brief uses an array with one path, for example `["requirements.md"]`.
A multi-file brief might use `["requirements.md","appendices/limits.txt"]`.
Read this inventory independently of the acceptance-plan generator. Split compound
requirements when their rules can fail independently. Identify the operation, business
key, effective date, prerequisite, consumer and permitted correction. Include creation,
edits and changes of state. For an interval, specify endpoint inclusion and check both
open and completed intervals. When the end must follow the start, try closing at
the start and verify that the refused operation accrues no charge. For a required
nonblank value, try spaces, tabs and line breaks, then read the saved value.
Record a valid neighboring example for every prohibited operation. Derive any
expected amounts, groups and dates directly from the brief.

Store these project-owned inputs in `ai/business-obligations.json`. For example:

```json
[
  {
    "id": "approved-before-release",
    "source": "requirements.md",
    "quote": "A release requires an effective approval.",
    "effect": "write",
    "proofs": [
      { "kind": "effective-prerequisite", "target": "document:example.release", "key": "approval",
        "expect": { "refusalStatus": 409, "source": "document:example.approval",
          "owner": "recipient", "effectiveAt": "releaseDate" } }
    ]
  }
]
```

Make a second pass over every authoritative source file in its original order.
Write a project-owned `ai/operation-review.json` while reading the brief, before
generating the plan. The approval obligation above has this corresponding entry.

```json
[
  {
    "requirement": "approved-before-release",
    "target": "document:example.release",
    "operation": "post",
    "facts": [
      { "kind": "consumes", "key": "approval", "source": "document:example.approval",
        "owner": "recipient", "effectiveAt": "releaseDate", "editableOwner": false, "withdrawable": false }
    ]
  }
]
```

Use one entry per target, operation and source obligation. Include each application
document. Use an empty `facts` array with a concrete `reason` for a reviewed
operation with no applicable facts. Other applicable checks still belong in its
obligation. Resolve contradictory classifications against the brief before capture.

| Fact | Required evidence |
| --- | --- |
| `consumes` | Identify the source, owner field and effective date separately for each right. Bind these in `effective-prerequisite.expect`; read both owners and the source ID. For an editable owner bind `owner-change` to the consumer. Persist a changed owner, attempt the original source, read back unchanged state, then succeed with matching ownership. For a withdrawable source declare `replaceable` and bind `dated-withdrawal` on that source with the consumer target, replacement policy and independent `expectedControl`. If substitution is permitted, exercise a later substitute that cannot cover the original date, then an effective substitute. |
| `exclusive` | Bind `interval-overlap`. For a correctable release declare `correctable: true`, `releaseTarget` and `boundary`. Bind `boundary-withdrawal` on the release target. For half-open intervals start a successor at the release instant, try to undo the release and read the sole holder. For inclusive endpoints prove refusal at equality and success immediately after. |
| `snapshot` | Bind one `snapshot-stability` key per fact and consumption phase. Put `from` and `until` in both the fact and proof expectations. Change the upstream value between these phases. Read the original record and a fresh control, including reporting group, currency or calendar when applicable. |
| `contended` | Declare `scope` as `record` or `shared-source`. Bind the target's `concurrent-operation` case. For a shared source also bind `competing-consumers` on the same target and key. Submit distinct drafts concurrently, record both HTTP responses and read both records and their source associations. The losing draft must retain its state and return the expected business refusal. |
| `manual-price` | Bind the typed `posted-override:<type>` outcome on the pricing requirement. Use a second browser control for the automatic price. |
| `rows` | Review each public row-dependent lookup, report or calculation. Use its HTTP method and pathname as `operation` and pin it in `query-growth.expect.operation`. Emit the observed method and pathname as `small.requestOperation` and `large.requestOperation`. For remote line pricing use the same route in the pricing case's `priceOperation`. Use `{id}` for GUID path segments. Verify the price fields from independently seeded rows and read them from that request in the browser. |
| `history-total` | Bind `paged-total` for each total derived from a paginated history, including settlement balances. Pin `operation`, `pageLimit` and three independently calculated `expectedTotals`. At limit minus one, limit and limit plus one, read the same record and an independent report, recording `count`, `recordId`, both statuses and both totals. Follow the next page, retain both sets of IDs and verify that the last entry appears exactly once. |

For each substantive clause, identify the actor, write or read, applicable date,
observable effect and independent positive control. Include independent resource
histories, an active sibling beside a retired record, and permitted correction
at each affected date when the brief requires these distinctions. For a scoped
balance, fund one resource, refuse a withdrawal on its unfunded sibling, read
both balances, then fund the sibling and allow the withdrawal. For scoped
intervals, also allow two independent resources to occupy the same interval. Keep separately
testable clauses bound even when their quotations overlap. Record headings,
context or deliberate exclusions in the optional `ai/brief-context.json` array,
for example `[{"source":"requirements.md","quote":"## Operations","reason":"Section heading."}]`.
For a non-heading excerpt give one complete source line, its reason and a
`category` of `background`, `definition` or `out-of-scope`. Bind each required
verification item to an obligation even when it appears under a testing appendix.
For an excluded list item, classify its exact section heading in the same source
as `out-of-scope` too. Review the heading and exclusion reason in the brief's
own language. A repeated item under a required section still needs a proof.
The CLI checks text coverage across the entire declared inventory, including files
with no obligation quotes. It fingerprints the operation review, inventory, source contents and context. Review the list of source files, the meaning of each
quotation and each exclusion independently of the code and generated test plan.

`node infrastructure/ngb.mjs ai capture` previews the fingerprint. Add `--apply`
to record the reviewed input and the complete source-file digests. Commit the source list, obligations and lock with the brief. For CI or model comparison, keep the reviewed
`baselineSha256` independently and provide it as `NGB_OBLIGATIONS_SHA256`.
Once an application declares documents, obligations or a requirement trace, its
acceptance plan must use schema 2, including for the integration-only profile.
For an existing schema 2 plan, review the original delivery and its appendices to
create the source list and operation review, classify existing context lines, and
add the corresponding proof bindings. Recover the intended report zone and lookup fields from
the requirements, update the test observations, then preview and record the new
fingerprint. Update any independently pinned digest after reviewing that change. Recapture differences only as a reviewed change
of requirements or acceptance criteria. Keep capture out of test/plan generators.

Each obligation binds one or more checks by kind, target and key. Its `expect`
fields compare precomputed values with the current integration test's observed
properties. Bind `historical-cutoff` to `expected`, `laterExpected` and `timeZone`,
`report-export` to an ordered array of `expectedCells`, and `dated-withdrawal`
to the `expectedControl` snapshot. For example, when a filtered report should
export a count of three, the obligation proof is
`{"kind":"report-export","target":"report:example.activity","key":"filtered-count","expect":{"expectedCells":[3]}}`.
Derive them from the original fixture and rule before calling the implementation.
For other observations, use `expect` when the brief specifies a concrete value.
A named behavioral integration test can use `{"integrationTest":"..."}`.
A UI obligation uses `{"scenario":"navigation","target":"navigation:example.home","outcome":"dashboard-home"}`.
Write, identity, financial and final-state rules need a write and a fresh read of
persisted effects. A label or search check covers only its own presentation rule.
Classify each obligation as `write`, `read`, `presentation`, `recovery` or
`operations`. A write requires a structured server mutation check or the matching
document `command:<type>/<code>` browser proof, which executes the command and
reopens the changed field. Recovery requires the explicit stores from the brief.
Classify the behavior the clause requires. Seed postings that establish a report's
opening and closing values belong to its read fixture. A clause that also specifies
which postings must succeed or be refused needs a separate write proof. Preview
the map with `ai capture` and resolve its diagnostics against the source before
recording the baseline.

### Exercise the business timeline

Trace each business mutation and each prerequisite to the text that authorizes
it. A projection of charges or usage does not by itself authorize a posting to
another ledger or require advance funding. For independent workflows include
an allowed transaction with no records in the other workflow and compare both
sets of balances before and after. Record any material ambiguity before
implementation.

For each report advertising XLSX export, bind a `report-export` check. Execute
the report and download its workbook with the same parameters and filters. Read
the worksheet cells and compare both results with independently seeded expected
values. Emit `downloadStatus`, `contentType`, `expectedCells`, `screenCells`
and `worksheetCells`. Include a populated business value such as a name, date,
number or boolean. Zero and false are valid. Also exercise a genuinely empty
filter when applicable, checking the empty report and downloaded worksheet.

* Bind `state-invariant` to the actual catalog or document operation. Exercise blank
  required input, attempts to change or reuse a stable identifier, or a transition
  from a terminal state, where specified. Try the normal action again after the
  alleged reactivation. Distinguish correction of a mistaken original entry from a
  new business operation. Read both the accepted control and the state after refusal.
  Emit `operation`, `persistedControlId`, both HTTP statuses, the expected and actual
  business reason, and `beforeRefusal` / `afterRefusal`.
* Give each right and each source of funds its own `effective-prerequisite` key.
  Check availability as of the consuming operation, including when a later posting
  has already settled the balance. For `dated-withdrawal`, recalculate all dependent
  confirmations at their dates under the proposed withdrawal. A later replacement
  cannot fund an earlier commitment. Exercise the authorized correction order and,
  where substitution is allowed, an already effective replacement. Emit `sourceId`,
  `consumerId`, `sourceAt`, `consumerAt`, `replacementAt` for substitution,
  both statuses and refusal reasons. Read the original records before and after
  refusal and after the permitted correction. Emit `persistedControlId` for the
  original source and `controlConsumerId` for the original dependent record.
  Each of `beforeRefusal`, `afterRefusal` and `afterControl` contains persisted
  lifecycle values in `source` and `consumer` and the numeric balance on the
  dependent date in `balance`. Compare the permitted result with the separately
  captured `expectedControl`. Verify the source changed and that both dated
  balances and the dependent movement match the correction rule.
  Serialize competing writes on the same key.
  For ID-based reversals specify which arguments are omitted and which effective
  date comes from the original event. Run both the refused and the permitted
  reversal through that same interface and read the resulting dated balances.
  Execute a first successful business write against the real provider before
  evaluating its refusal cases. Creating a stored function may defer query
  planning until invocation. Qualify columns and distinguish routine variables
  from query aliases; check the application's expected refusal reason.
* For resources with exclusive use, exercise every supported write that can create
  an overlap. Extend an earlier period into a later confirmed one through the
  normal correction interface and verify the original end and charges after
  refusal. Include a permitted booking that starts exactly at the preceding
  end when the business rule uses half-open intervals.
* Give each snapshot fact and consumption interval its own key. Vary classification
  and the business calendar while an operation is open, after completion before
  settlement, and after settlement. Test a pair of identical instants spanning local
  midnight. Preserve agreed business dates or the agreed zone for the full interval.
  A fresh operation uses the new configuration. Check the amount in the original
  group and the absence of that amount in the new group.
* Bind `business-day-cutoff` where configurable zones and date-only facts feed
  end-of-day reports. Preserve calendar dates as dates. For instants use consecutive
  local midnight boundaries in the business zone, accounting for DST. Run the same
  dated event in two configured zones, including a zone such as Pacific/Kiritimati
  that maps noon UTC to the next date. Emit `recordId`, `businessDate`, `opening`,
  `closing` and a `zones` array of `{zone, configuredZone, dates, values}` with measured
  positions on the previous, selected and following calendar dates.
* For `restore-rehearsal` declare `stores` in both the obligation proof and the
  business check when more than the application database is in scope. For example
  `["database","identity","attachments"]` requires a fresh login through the
  restored identity provider (`restoredLogin.status/subject/originalSubject`) and
  a download through the restored application (`restoredAttachment.status/id/bytes/sha256/originalSha256`).
  Seed before backup, isolate the restored instances, use the same business identity
  and compare bytes. Exercise upgrade from the declared earlier release if available;
  record the missing baseline explicitly when it is unavailable.

### Report the application upgrade baseline

For schema 2 integration acceptance, create `ai/application-upgrade.json`.
Set `currentRelease` to the tested application's release identifier. When there
is no supported previous application release, set `previousRelease` to null
and give a nonempty `reason`. The machine report records `unavailable`.
A frozen database schema can separately establish migration compatibility.

When a previous release exists, declare `previousRelease`, the project-relative
`sourceManifest`, its `sourceManifestSha256`, and the fully qualified integration
`test`. Seed the previous release using its published artifacts, upgrade the
existing data, and reopen the same record in the target application. From that
passing test write one `NGB-UPGRADE ` JSON line with the observed release IDs,
manifest digest, successful `seedStatus` and `readStatus`, matching `recordId`
and `reopenedId` and the nonempty `before` and `after` business state. Record
values that the upgrade must preserve. The integration report supplies the
verified release pair and the reopened identity under `test.integration.application-upgrade`.

Before implementation, retain the supplied brief (or its text transcription)
in the project. Store an independent `ai/requirement-trace.json` alongside the
schema 2 acceptance plan. List each requirement ID once, give its project-relative
source and an exact excerpt, and bind all relevant server checks by kind, target
and key. Other server behaviors can bind exact passing TRX test names in
`integrationTests`. One requirement can need several checks. A purely visual requirement
has a browser case and a `browserOnlyReason`. For a server-only requirement use
`serverOnly: true` in the acceptance plan and bind the server checks here.

```json
{
  "requirements": [{
    "id": "approved-before-release",
    "source": "requirements.md",
    "quote": "A release requires an effective approval.",
    "checks": [{"kind":"effective-prerequisite","target":"document:example.release","key":"approval"}]
  }],
  "documentRules": [{
    "type": "example.release",
    "effective-prerequisite": ["approval"],
    "interval-overlap": {"reason":"Release records a point-in-time event."},
    "snapshot-stability": {"reason":"This operation consumes no captured classification or rate."}
  }]
}
```

Review each application document for dated conditions, closed intervals and
captured facts. For each applicable kind list all business keys and exercise each
one. For effective prerequisites include available funds, issued documents and
consumed limits when required by the brief. Record `persistedControlId` from a
read-back of the successful positive control.

* `interval-overlap` proves that a completed interval still occupies that part
  of the timeline. Record `resourceId`, `closedRecordId`, `priorStart`, `priorEnd`,
  `attemptStart`, `attemptEnd`, `controlStart`, `controlEnd` and `persistedControlId`.
  Set `boundary` to `inclusive` or `exclusive-end` according to the business rule. Supply
  the same authorized control, refusal code and unchanged snapshots as for a
  dated prerequisite. Use the business calendar and its endpoint conventions.
* `snapshot-stability` changes the original source between capture and actual
  later consumption. Record `sourceId`, `consumerId`, `capturedAt`, `changedAt`,
  `consumedAt`, `original`, `changed`, `expected` and `observed`. A fresh case
  after the change must exhibit the new result (`freshExpected`, `freshObserved`).
* For a resource limit add `workingSet: true` to the capacity binding in the
  trace and `maxWorkingSetBytes` to the test budget. Sample the actual serving
  process and record `workingSetBytes` alongside each measured request.
* For a recovery requirement bind `restore-rehearsal` to its navigation surface.
  Back up, restore into a separate disposable database or stack, start the
  application there, reopen a known record and compare business state. Record
  `sourceInstance`, `restoredInstance`, `backupBytes`, `backupSha256`, `recordId`,
  `readStatus`, `beforeBackup` and `afterRestore`. Include attachments, identity
  configuration or other stores when the recovery requirement includes them.
