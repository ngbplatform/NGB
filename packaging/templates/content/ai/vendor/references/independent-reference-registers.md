# Independent reference-register writes and time

Target: NGB 3.4.0. Use the public
[Runtime](contracts/NGB.Platform.Runtime.json) and
[ReferenceRegisters](contracts/NGB.Platform.ReferenceRegisters.json) contracts.
The executable example is
[IndependentReferenceRegisterTests.cs](../examples/equipment-inspection/EquipmentInspection.IntegrationTests/IndependentReferenceRegisterTests.cs).

## Ownership and commands

Configure `ReferenceRegisterRecordMode.Independent` with explicit fields and
dimension rules through `IReferenceRegisterManagementService`, then ensure its
physical schema through `IReferenceRegisterAdminMaintenanceService`. There is
no recorder document. A subordinate register instead uses the document lifecycle;
the independent write service rejects that mode.

`IReferenceRegisterIndependentWriteService` is a trusted application service,
not an HTTP authorization boundary. Authorize the caller and validate application
rules before invoking it from an application operation. Do not add an unrestricted
endpoint exposing arbitrary register IDs. The example invokes these contracts
inside its disposable, trusted test fixture; it does not claim UI or HTTP mutation
coverage for independent registers.

Upsert appends a version; it does not update an existing row. Supply a stable
`commandId` when retrying the same logical operation. Idempotency is scoped to
register, command and operation, and returns `Executed` or `AlreadyCompleted`.
It is not a payload-comparison contract: do not reuse a completed command ID for
different content. Use a new command for an intentional correction.

The default `manageTransaction=true` owns the transaction. For a larger atomic
application operation, begin one unit-of-work transaction and pass
`manageTransaction=false` to every participating call. The service rejects that
mode without an active transaction, and rejects transaction ownership when one is
already active. Records, command completion and audit commit or roll back together.
Keep network calls and other nontransactional side effects outside this scope.

## Periods and historical reads

Nonperiodic records require a null period. Periodic records require a UTC instant.
Supported buckets are second, day, month, quarter and year. The stored original
`PeriodUtc` and normalized `PeriodBucketUtc` are distinct; a monthly bucket does
not make a change effective from the first day of its month.

`SliceLast*` uses one `asOfUtc` for both effective time and recorded time. A periodic
record must have `PeriodUtc <= asOfUtc` and `RecordedAtUtc <= asOfUtc`. Within the
selected key, ordering considers bucket, original period, recorded timestamp and
record ID. A future effective record cannot hide the earlier applicable value.
A correction recorded today is not visible in a query for yesterday, even when
its effective period is earlier. These signatures do not expose independent
effective-time and knowledge-time parameters.

`GetKeyHistory*` requires `periodUtc` for a periodic register and forbids it for
a nonperiodic one. The argument selects a bucket, not an exact instant. Query
each required bucket explicitly. Page versions using both
`beforeRecordedAtUtc` and `beforeRecordId`, keeping the same as-of bound across
pages. The example verifies previous-month rates, corrections, two future rates
in one month and the original state before the correction was recorded.

## Tombstones and paging

Tombstone selects the applicable latest version at the supplied effective time
and appends a deleted version with that record's original period and values.
The argument is not a new deletion-effective date. A missing or already-deleted
key is a completed no-op; a successful logical deletion retains its prior values
and history. A later effective record remains independently applicable. To model
a different business concept such as an explicit end date, first verify that the
application's chosen data model expresses it.

By default reads hide the selected tombstone and do not fall back to an older
visible version. Use `includeDeleted` explicitly for history and diagnostics.
For current-state paging use the cursor-aware `SliceLastAll*PageAsync` methods
and their `NextAfterDimensionSetId`/`HasMore`. A cursor advances by examined keys,
which can include deleted keys, rather than necessarily the last returned row.
Keep a fixed as-of, obey the limit and stop on `HasMore=false`. The example checks
multiple pages across deleted equipment keys, dimension filtering and enriched
display values, plus concurrent retries and rollback of records and audit.
