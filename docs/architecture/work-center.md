# Work Center

Work Center is a permission-filtered operational hub for attention items, durable tasks, and in-app notifications. It is available as a shell drawer and a full page in Property Management, CRM, Agency Billing, and Trade.

## Read model

The unified feed combines:

- open tasks assigned directly to the current user;
- unclaimed tasks assigned to one of the user's active roles;
- tasks claimed by the current user;
- non-dismissed notification deliveries for the current user.

Source-resource view permissions are applied in SQL before counts and cursor pagination. The service performs one bounded in-memory permission check per distinct resource key as a defense in depth; it does not query permissions per item.

The summary reports attention, open-task, overdue-task, active-notification, and
unread-notification counts plus a version used for invalidation. Summary queries accept the same
optional vertical scope as the feed. Reading a task does not remove it from the open-task count.
Completed and cancelled tasks leave the open count. Dismissed or expired notifications leave the
active-notification count; reading a notification removes it only from unread and attention counts.

The full page and shell drawer show these server-side totals in their tabs. Feed rows use opaque
cursor pagination with automatic infinite scrolling (30 rows per full-page request and 20 per
drawer request). Appended pages are deduplicated by item kind and ID. If a later request fails,
already loaded rows remain visible and the sentinel offers a retry. Completed and cancelled tasks
are history only, so the clients do not render the More or Take action controls for them.

## Tasks

`IWorkCenterTaskService` creates tasks with a stable task code and deduplication key. Exactly one user or role assignment is required. Tasks support:

- open/in-progress/completed/cancelled state;
- priorities and optional due dates;
- optimistic role-task claims;
- per-user read and snooze state;
- primary action and typed navigation target;
- correlation and causation IDs.

Task policies implement `IWorkCenterEventPolicy`. Property Management creates an Apply receivable payment task when a posted payment has unapplied credit. CRM creates Qualify lead, Convert qualified lead, and Complete CRM activity tasks from document action events. Policies complete or cancel matching tasks by deduplication key when the business condition is resolved.

Task preferences control task participation only. Candidate users are resolved from the direct assignment or active role membership, filtered by their concrete task preference, and snapshotted in `platform_task_recipients`. If every candidate disables a task type, no task is created. With mixed preferences, one shared role task is created for the enabled recipients only. Creating or reopening a task never emits an assignment notification. Reprocessing an already-active task is idempotent.

## Notifications and preferences

`WorkCenterPreferenceDefinitionRegistry` owns task and notification definitions. Each definition has an explicit `Task` or `Notification` kind, code, display name, category, supported channels, default enablement, mutability, mandatory status, and optional applicable role codes. Notification definitions additionally supply default severity and retention. Runtime kind checks prevent task definitions from being used to create notifications and vice versa.

Creation first removes recipients who do not hold an applicable active role, then resolves preferences for the remaining users in one query and inserts deliveries in one bulk operation. Mandatory definitions override user preferences. Per-user read and dismiss timestamps do not mutate the shared notification record.

The Preferences page reads only definitions applicable to the current user's active roles and submits a batch update to `/api/me/notification-preferences`. Default enablement is declared independently by each definition through `DefaultEnabled`; all optional definitions registered by the current sample verticals are enabled by default. A user without `crm.sales_rep`, for example, neither sees nor can update the CRM task/notification checkboxes.

The sample verticals currently register:

- Property Management / `pm-ar-clerk`: task `Apply receivable payment`;
- CRM / `crm.sales_rep`: tasks `Qualify lead`, `Convert qualified lead`, and `Complete CRM activity`; notifications `Lead qualified` and `Opportunity won`;
- Agency Billing and Trade: no vertical-specific Work Center notification definitions yet.

## Durable outbox

Document action transactions append CloudEvents-style records to `platform_outbox_events` and one consumer-state row for `work-center`.

The hosted processor:

1. claims a bounded batch with row locking;
2. runs matching policies in a transaction;
3. records attempt history;
4. marks the delivery complete; or
5. schedules exponential backoff with deterministic jitter.

After eight failed attempts, a delivery becomes dead-lettered. The event remains immutable. Consumer state and history retain operational evidence.

The health check exposes pending count, failure count, and oldest pending age. More than 15 minutes of lag is unhealthy; failed deliveries are degraded. Metrics use the `NGB.Platform` meter and traces use `NGB.Platform.DocumentActionsWorkCenter`.

The processor is an ASP.NET Core hosted service, not a Hangfire recurring job. It polls every two seconds to provide low-latency projection and drains ready rows in bounded batches. It therefore does not appear on the Hangfire `/hangfire/recurring` page. Inspect it through:

- the `Work Center outbox` entry returned by `/health`, including `pendingCount`, `failedCount`, and `oldestPendingAgeSeconds`;
- logs from `NGB.Runtime.WorkCenter.WorkCenterOutboxHostedService` and `NGB.Runtime.WorkCenter.OutboxProcessor`;
- `ngb.outbox.pending`, `ngb.outbox.oldest_age`, `ngb.outbox.processed`, and `ngb.outbox.failures` telemetry.

Hangfire remains the scheduler for coarse recurring business jobs. The outbox processor is an event-delivery worker and deliberately has a separate lifecycle and observability surface.

Processor ownership is explicit. `AddNgbRuntime()` registers the projection services but does not
start the polling loop. Each API host explicitly calls both `AddNgbWorkCenterRealtime()` and
`AddNgbWorkCenterOutboxProcessing(configuration)`: the first registers SignalR and its notifier,
while the second independently registers the single hosted polling loop. Enabling realtime never
starts outbox processing implicitly. Background Jobs hosts must not call
`AddNgbWorkCenterOutboxProcessing()`: otherwise they could claim an event without owning the SignalR
connections that must receive its invalidation.

## Realtime invalidation

SignalR publishes a monotonic version through `/hubs/work-center`. The client treats messages as invalidations, deduplicates older versions, and reloads summary/feed data. Reconnect also refreshes state. HTTP remains the source of truth; hub messages never carry privileged item payloads.

Without a distributed SignalR backplane, deploy exactly one steady-state API replica per vertical.
The ingress must preserve WebSocket upgrades and use long read/send timeouts. A rolling deployment
may briefly run one surge replica; connected clients refresh authoritative HTTP state after
reconnecting. Configure a distributed backplane before increasing steady-state API replicas.

## Database schema

Migration `V2026_07_26_0100__ngb_platform_document_actions_work_center.sql` adds:

- `documents.version`;
- `platform_document_action_executions`;
- `platform_outbox_events`;
- `platform_outbox_consumer_state`;
- `platform_outbox_consumer_history`;
- `platform_tasks`;
- `platform_task_recipients`;
- `platform_task_user_states`;
- `platform_notifications`;
- `platform_notification_deliveries`;
- `platform_user_notification_preferences`.

Partial indexes cover ready outbox work, active and unread notification deliveries, open
user/role/claimed task paths, and cursor-ordered per-user feed access. Unique constraints enforce
action idempotency and task/notification deduplication.

## Operations

Monitor:

- `ngb.document_action.*` rates, failures, duration, and concurrency conflicts;
- `ngb.outbox.*` pending age, failures, and throughput;
- `ngb.work_center.*` query/policy duration, open/overdue tasks, and notification creation;
- the Work Center outbox health result.

Investigate a failed delivery using its event ID, consumer state, attempt history, correlation ID, and causation ID. Fix the deterministic policy/data error before retrying. Do not edit the immutable event payload.
