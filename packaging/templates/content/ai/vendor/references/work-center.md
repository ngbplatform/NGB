# Work Center, outbox and background jobs

Target: NGB 3.4.0. Inspect
[application services](contracts/NGB.Platform.Application.Abstractions.json),
[Definitions](contracts/NGB.Platform.Definitions.json),
[BackgroundJobs](contracts/NGB.Platform.BackgroundJobs.json) and
[Runtime](contracts/NGB.Platform.Runtime.json).

Use `IWorkCenterTaskService` and `INotificationService` to create supported task
and notification requests. Requests carry stable deduplication keys, source
references and correlation/causation identifiers. Notification recipients are
either explicit users or one role source, not both. Preserve that invariant.

`WorkCenterMutationResult` returns changed user IDs. Notify only after the
transaction commits; do not carry mutable scoped state across that boundary.
`IDocumentActionCompletedWorkCenterPolicy` reacts to the existing completion event.
`IOutboxProcessor.ProcessBatchAsync` processes bounded work; retry behavior must
preserve deduplication and avoid notifying about rolled-back changes.

`IWorkCenterQueryService` owns current-user list/summary, preferences and supported
read/claim/snooze/dismiss operations. Claims use an expected version. Use these
services and public UI exports; do not let an agent mutate task tables directly.

For Hangfire composition, explicit schedules, register finalization and runner
failure semantics, read [background jobs](background-jobs.md). Outbox delivery
and recurring jobs have distinct execution and retry contracts.

Verify repeated events, concurrent claims, role recipients, disabled preferences,
rollback before commit, retry after a partial failure, bounded batch sizes and
worker restart. Observe health and failed delivery counts. A scheduled job's
registration is not proof that its business effect actually ran.

## Retention and maintenance

`IWorkCenterMaintenanceService.PruneAsync` performs trusted, bounded maintenance.
Configure the existing `Ngb:WorkCenter` policy through `NgbWorkCenterOptions`:
completed action receipts default to 90 days, terminal tasks to 180 days,
notification deliveries and outbox records to 30 days. The default batch size is
1,000 with at most ten batches per invocation. The batch bound applies separately
to each candidate record kind; it is not a global maximum for all deleted rows.
Each batch runs in a transaction and a run stops early when nothing is removed.

Only completed action receipts are eligible by completion time. Expiring a receipt
allows its idempotency key to begin a new execution; choose retention to cover the
application's retry window. This does not delete the document or its business audit.
Completed/cancelled tasks expire by their last update; open tasks remain. Notification
deliveries age from dismissal or notification expiry. Old notification records
can disappear once no deliveries reference them; a later batch can collect them.

Outbox retention uses event creation time and preserves events with any Pending,
Processing or Failed consumer state. Completed and DeadLetter states are terminal
for retention. The latter are eligible for removal, so arrange operational review
within the configured retention window. This maintenance contract does not provide
a manual redrive API. Public cutoff/result DTOs and the maintenance repository
describe provider work; they do not establish current-user authorization.

`WorkCenterMaintenanceTests` uses a controlled `TimeProvider`, disposable PostgreSQL
and the actual public runtime/provider. Four cases check expired/recent/unfinished
receipts, bounded task deletion, live notification preservation and outbox terminal
states. No wall-clock waits or manual SQL are required. These tests do not claim
deployment scheduler or configuration-binding coverage.

## Tasks, delivery and preferences in an external application

The [HTTP contract example](../examples/equipment-inspection/EquipmentInspection.IntegrationTests/WorkCenterHttpTests.cs)
registers test-owned `IWorkCenterPreferenceDefinitionSource` definitions and
creates synthetic work through trusted public application services. Its read and
mutation assertions use the actual `WorkCenterControllerBase` endpoints and
Keycloak identities. These service tests are separate from the application-owned
withdrawal policy described below. There is no generic notification-creation endpoint.

A Work Center user needs an active linked platform identity. Administration does
not imply a personal Work Center identity exists. Task assignment and notification
delivery choose recipients separately from source visibility: a recipient without
view permission for the source cannot list or mark that item read. The source
reference preserves kind, code and entity ID; it is not permission to read them.

`CreateWorkCenterTaskRequest` requires exactly one user or role assignment. A
role-assigned open task can be claimed with its expected version; competing
claims have one winner. Reading and snoozing affect personal attention state.
Completion/cancellation use the public task service and stable deduplication keys;
repeating a terminal operation does not report another change. Task completion
does not itself execute a document action.

In-app notification delivery is deduplicated per definition/key and recipient.
Reading or dismissing a notification for one recipient does not mark it read or
dismiss it for another. Definitions specify default enablement, mandatory status,
supported channels, role applicability and retention. User preference changes
apply to subsequent recipient resolution; they are not deletion of old deliveries.
A mandatory definition cannot be disabled. Inactive users are excluded from new
delivery, and their authenticated Work Center requests are denied.

These creation services join an already active unit of work or own a transaction
when none exists. A returned `ChangedUserIds` is not evidence that an enclosing
transaction committed. The application must wait for commit before notifying
clients. The example confirms that an uncommitted/rolled-back notification is
invisible to separate HTTP requests and that the same deduplication key works
after rollback. It sends no email or external messages.

## Transactional document-event projection

The [withdrawal policy](../examples/equipment-inspection/EquipmentInspection.Runtime/InspectionWithdrawalPolicy.cs)
implements `IDocumentActionCompletedWorkCenterPolicy`. In this example, an
inspection's `unpost` event asks its linked actor to review the withdrawal and
sends an informational in-app notification. The task concerns a recorded event;
subsequent posting does not erase that review obligation. A later withdrawal has
a new event ID and produces a separate review. This is example application policy,
not a platform-wide rule. Other document/action types and events without a linked
actor are ignored. Applications requiring supervisor/role assignment must define
that policy explicitly rather than infer a recipient from an administrator token.

Both mutations use the event ID in stable deduplication keys, preserve correlation,
and use the completion event as their causation ID. The policy returns the union
of changed users. It neither starts another transaction nor sends realtime signals
or external messages. Runtime executes all registered policies and marks the claimed
attempt completed inside one transaction. The API explicitly owns polling through
`AddNgbWorkCenterOutboxProcessing`; registering SignalR alone does not start it.
Keep that owner distinct from the example's Hangfire worker.

The [HTTP/outbox cases](../examples/equipment-inspection/EquipmentInspection.IntegrationTests/WorkCenterOutboxTests.cs)
observe the actual dispatcher event, verify both writes inside the projection's
transaction, inject a later policy failure, and confirm separate HTTP reads see
neither item. A subsequent retry commits one task and one notification. Reads from
inside the realtime callback verify that data is already committed. Replaying the
action or applying the same policy event again creates no duplicates. Another case
rolls back a test-owned envelope, then commits the same envelope successfully.
The synthetic envelope isolates persistence behavior; it does not pretend that a
document action occurred. Realtime failure after commit is logged by the SDK and
does not retry or undo the persisted projection; clients can recover through queries.
These tests replace the notifier and do not certify SignalR transport or browser UI.

The [delivery cases](../examples/equipment-inspection/EquipmentInspection.IntegrationTests/OutboxDeliveryTests.cs)
exercise public Runtime and PostgreSQL packages without an API poller. A test clock
advances retry/lease time; the real provider retains state across fresh runtime
service containers. In 3.4.0, the processor claims at most 25 events per call and
dead-letters a persistent failure on attempt eight. Backoff is exponential with
event-specific jitter; do not implement an immediate hot-loop retry. The test
verifies no retry after one second and eventual eligibility after advancing beyond
the supported backoff, without asserting an exact jitter value. Dead-lettered
delivery remains in `FailedCount` but leaves `PendingCount`; zero pending alone
does not mean healthy. Automatic processing does not resume a dead-lettered event
merely because its policy is later fixed. This example exposes no manual redrive API.

Claims are exclusive across concurrent consumers. Abandoned claims become eligible
after the provider's 30-minute lease; the tests advance 31 minutes, reclaim attempt
two, reject completion by attempt one, then complete the current attempt. A fresh
dependency container models processor restart, not a process-kill/host-recovery test.
Health's pending count excludes currently processing leases. Use it with failure
counts and delivery logs; do not treat a momentary zero as proof of full drainage.

## Public pages and realtime delivery

The example registers `NgbWorkCenterPage` at `/work-center` and
`NgbNotificationPreferencesPage` at `/settings/notifications`, using the declared
`@ngbplatform/ui/work-center` entry. These paths match the default navigation
contract. The shell uses `createDefaultNgbWorkCenterConfig` for the authenticated
HTTP gateway and SignalR client. Its proxy forwards `/hubs` with WebSocket support
as well as `/api`; the API maps the existing Work Center hub.

The [browser case](../examples/equipment-inspection/web/tests/e2e/inspection.spec.ts)
withdraws a real inspection in one tab and observes the actual `workCenterChanged`
WebSocket invocation in a second tab. That tab displays the task and notification
without navigation or refresh. It dismisses the notice, snoozes the task, reloads
saved notification preferences, and confirms a second withdrawal still creates a
mandatory review while the optional notice is disabled. Authentication, source
authorization, projection and database writes are real; no HTTP or hub mock feeds
the page. The test records only frame kind/count, never the access-token query in
the socket URL. Transport reconnect, other browsers and responsive layouts remain
separate compatibility cases.
