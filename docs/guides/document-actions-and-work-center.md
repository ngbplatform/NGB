# Register Document Actions and Work Center Policies

This guide describes the 2.0 extension path used by the four sample verticals. Existing 1.3.1 verticals must first follow the [2.0 migration guide](./migrating-to-2.0.md).

## Register an action

Implement `IDocumentActionDefinitionsContributor` and add definitions through `DocumentActionDefinitionsBuilder`.

Choose one execution model:

- lifecycle: use a standard code; the dispatcher calls the posting service;
- derivation: bind a registered derivation code and reuse the derivation engine;
- command: register an `IDocumentActionHandler`;
- navigation/view: provide a typed target and no server mutation handler.

Register custom evaluators and handlers through the vertical runtime DI module. Do not put runtime handler types in metadata projects, import vertical code into platform projects, or add application-specific resolver props to the UI framework.

## Add availability facts

When availability needs database facts:

1. implement `IDocumentActionContextEnricher` for the document type;
2. load all required facts with bounded queries;
3. return stable fact keys;
4. implement `IDocumentActionAvailabilityEvaluator`;
5. return `DocumentActionDisabledReasonDto` values with stable codes.

The evaluator must not infer authorization. Use an authorization evaluator only when the default permission mapping is insufficient.

Property Management's `ReceivablesApplyAvailabilitySource` and `PayablesApplyAvailabilitySource` are examples. They calculate a single document's remaining balance and feed `PropertyManagementApplyAvailabilityEvaluator`. The same availability source is reused by the PM Work Center policy; it is not exposed through document effects.

## Add semantic navigation

Use a stable target code plus parameters. The backend may resolve payload tokens such as `{documentId}`, `{documentType}`, `{createdDocumentId}`, and `{field:lease_id}`.

The PM UI registers semantic resolvers for:

- `pm.receivables.apply`;
- `pm.payables.apply`.

Unknown target codes return no navigation rather than guessing a route.

## Add a task policy

Implement `IWorkCenterEventPolicy` for an exact event type. Parse only the versioned event data you own and create/complete/cancel tasks through `IWorkCenterTaskService`.

Use deterministic deduplication keys. Assign exactly one user or role. Include a source resource, title snapshot, priority, optional due date, primary action code, typed target, and correlation/causation IDs.

Policies must be idempotent because the outbox is at-least-once. Do not call external systems inside the document action transaction.

## Define task and notification preferences

Register a concrete `Task` preference definition for every task code and a separate `Notification`
preference definition for every informational business event. A task must never create an
assignment notification that merely repeats the task title. Decide:

- default severity and retention;
- default enabled state;
- whether the user can disable it;
- whether it is mandatory;
- supported channels;
- applicable role codes.

For tasks, `CreateWorkCenterTaskRequest.TaskCode` selects the `Task` preference definition.
Disabled users are excluded from the task recipient snapshot, and the task is skipped when no
enabled recipient remains. For informational notifications, create one shared notification and
pass distinct recipient user IDs; the runtime batch-loads `Notification` preferences and
bulk-inserts deliveries. The runtime rejects attempts to use a task definition to create a
notification, or a notification definition to create a task.

Definitions with `ApplicableRoleCodes` are omitted from the Preferences page for other users, and
the task and notification services enforce the same role restriction.

## Vertical walkthroughs

### Property Management

Open a posted receivable charge/payment/credit memo. `editor-state` includes `pm.open_receivables_reconciliation`, with the lease and focused document encoded in its target. When a posted payment retains credit, the outbox policy creates a high-priority `Apply receivable payment` role task for `pm-ar-clerk`. Its task preference is enabled by default and configurable only by users holding that role. No duplicate assignment notification is emitted. Fully applying the payment completes the task.

### Snoozed tasks

Snoozing removes a task from **Needs Attention** until its `snoozedUntil` time, but it does not complete or delete the task. Open the **Tasks** tab to find it. The row shows **Snoozed until** and provides **Show now** to return the task to **Needs Attention** immediately. Otherwise it returns automatically when the snooze period expires.

### CRM

CRM registers five preferences, all enabled by default and visible only to `crm.sales_rep`:

- task `Qualify lead`: posting/reposting Lead Intake creates a role task due in two days; unposting cancels it;
- task `Convert qualified lead`: posting/reposting a Qualified Lead Qualification completes the qualification task and creates a conversion task due in three days; posting/reposting Lead Conversion completes it;
- task `Complete CRM activity`: posting/reposting Activity Log with `Due At` and without `Completed At` creates a role task; completing the activity completes the task and unposting cancels it;
- informational notification `Lead qualified`: emitted when a Lead Qualification is posted/reposted as Qualified;
- informational notification `Opportunity won`: emitted when an Opportunity Update is posted/reposted with status Won.

Lead Intake exposes Create Qualification. Qualification exposes Create Conversion. Conversion creates Account, Contact, and Opportunity using the registered derivation chain. Result responses include the created document and the shared editor navigates to it.

### Agency Billing

A Timesheet exposes Generate Invoice Draft as a normal derivation action. The dispatcher calls the existing AB derivation handler, preserving validation, relationship graph, audit, and idempotency.

Agency Billing currently registers no Work Center task or informational-notification policies. Its Work Center Preferences page is therefore empty unless the host adds custom definitions.

### Trade

Trade receives lifecycle and view actions from the platform registry. No Trade-specific UI branch is required; this is the reference proof that the renderer is vertical-neutral.

## Testing

Run:

```bash
dotnet test NGB.sln
npm --prefix ui run test:all
NGB_COVERAGE_BASE_REF=HEAD ./quality/coverage/run-frontend-feature-coverage.sh
```

`dotnet test NGB.sln` includes the platform unit/architecture suites and the PM, CRM, Agency Billing,
and Trade integration coverage. PostgreSQL and API integration suites require Docker because they
use Testcontainers. `npm --prefix ui run test:all` includes type checking, the reviewed public-export
compatibility gate, unit, browser, and E2E suites. Browser and E2E suites require the Playwright
browser binaries and permission to bind local ports. The feature-coverage command also runs the
frontend diff-coverage gate; CI must supply its merge-base ref, and local release validation should
set `NGB_COVERAGE_BASE_REF` explicitly as shown.

For performance coverage, use the k6 workspace under `performance-tests`. PM lifecycle flows call `editor-state`, not the removed derivation-action endpoint. Run the documented smoke profile before baseline/load profiles and retain the generated summary artifacts.

## Upgrade notes from 1.3.1

- Replace action booleans from document effects with `editor-state.actions`.
- Replace separate derivation-action loading with the same action list.
- Execute lifecycle and derivation commands through `/actions/{actionCode}` with expected version and idempotency key.
- Remove app-level `resolveDocumentActions`.
- Treat Work Center SignalR events as invalidations and reload through HTTP.
- Run the 2.0 vertical migrator before deploying any 2.0 API/background host. It applies the
  `platform` dependency, including `V2026_07_26_0100`, before its vertical pack; CRM also applies
  `V2026_07_31_1600`. Do not execute embedded migration SQL manually.
- Deploy the API, background hosts, and web applications together; mixed 1.3.1/2.0 action clients are not supported.
- See [Migrating from 1.3.1 to 2.0.0](./migrating-to-2.0.md) for contract-by-contract replacements and rollout sequencing.
