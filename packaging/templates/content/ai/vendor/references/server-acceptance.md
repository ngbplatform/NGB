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
as `laterEvent`, `laterStatus` and `laterEffectiveAt`. Use ISO dates/timestamps.
Read the later period as a positive control and include its independent
`laterExpected` and observed `laterActual`. Use a fixture that exercises the condition of interest.
One scenario may emit several observations when it actually reads every measure.
The `Posting_a_later_inspection_preserves_the_count_at_the_earlier_cutoff` example
in `InspectionReportTests.cs` executes both cutoffs against the report endpoint
and emits the observed counts from the test.

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
