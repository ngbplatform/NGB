# Document Actions

Document Actions are the single source of truth for operations and navigation available for a document. Since platform version 2.0, lifecycle commands, derivations, vertical commands, and view/navigation actions all use the same metadata registry and response model.

## Runtime flow

1. A vertical contributes definitions through `IDocumentActionDefinitionsContributor`.
2. `DocumentActionRegistry` combines platform and vertical definitions and validates duplicate codes, execution kinds, handlers, derivations, targets, and ordering when the singleton registry is constructed. Hosts that require fail-fast startup validation must resolve it during startup rather than waiting for the first editor-state/action request.
3. `GET /api/documents/{documentType}/{id}/editor-state` loads the document, its monotonic version, a permission snapshot, bounded business facts, and the evaluated action list.
4. The shared Vue editor renders the returned metadata without reconstructing business availability.
5. Commands and derivations execute through `DocumentActionDispatcher`.
6. The dispatcher locks the document row, validates the expected version, refreshes authorization, evaluates availability again, invokes the lifecycle/derivation/custom handler, increments the version, writes audit and outbox records, and stores the idempotent result in one transaction.

The old action flags in document effects and the separately loaded derivation-action response were removed. The effects endpoint now returns accounting entries and register movements only.

## Action metadata

Each definition has:

- a stable lowercase code;
- label, optional localization key, description, and icon;
- `Primary`, `Secondary`, or `Dangerous` presentation kind;
- `Command`, `Derivation`, `Navigation`, or `View` execution kind;
- deterministic order;
- optional confirmation or required-reason policy;
- optional typed target;
- optional custom authorization, availability, handler, or registered derivation code.

Platform lifecycle codes are `post`, `unpost`, `repost`, `mark_for_deletion`, and `unmark_for_deletion`. `repost` remains a backend lifecycle/compatibility action but is intentionally hidden by the shared product editor; the primary UI presents Post/Unpost and Mark/Unmark for deletion according to document state. View actions include effects, document flow, audit, and print.

## Authorization and availability

Authorization answers whether an action may be disclosed to the current user. Unauthorized actions are omitted. Standard actions map to document RBAC permissions. A derivation requires view access to the source and create/view access to the target.

Availability answers whether a disclosed action is currently executable. Disabled actions remain visible with stable reason codes and human-readable messages. Availability evaluators consume a preloaded fact dictionary. A document type may register only one context enricher, which prevents accidental N+1 fact loading.

Execution repeats both checks after acquiring the document row lock and refreshing the permission snapshot. This closes the time-of-check/time-of-use window.

## Concurrency and idempotency

Command requests must include:

```http
Idempotency-Key: 2f2f56d6-45d8-4a53-a2f5-f77e49fb5343
Content-Type: application/json

{
  "expectedVersion": 7,
  "reason": null
}
```

`documents.version` is incremented after every successful action. A stale `expectedVersion` fails with a conflict and does not run the handler.

`platform_document_action_executions` stores the idempotency key, a SHA-256 request fingerprint, execution state, and completed response. Repeating the same request returns the stored response. Reusing a key for different input fails. A concurrent in-progress request is reported explicitly.

## Atomic side effects

A successful command transaction includes:

- the lifecycle, derivation, or custom handler;
- document version update;
- audit event;
- `ngb.document.action.completed` outbox event;
- stored idempotent response.

Work Center policies consume the outbox event asynchronously. A Work Center projection failure therefore cannot partially roll back an already committed document action, and the durable consumer retries it.

## Vertical implementations

- Property Management registers receivable and payable Apply navigation actions. Availability sources calculate remaining open-item balances, while semantic targets resolve to the correct reconciliation route.
- CRM registers Lead Intake → Qualification, Qualification → Conversion, and Conversion → Account/Contact/Opportunity derivations. Policies create follow-up tasks after the relevant action events.
- Agency Billing exposes Timesheet → Sales Invoice through the existing derivation engine.
- Trade uses the shared lifecycle/view action set without platform changes.

## Extension boundaries

The platform does not reference vertical assemblies. Metadata contributors contain declarative definitions; runtime handler types are supplied only through runtime registration. PostgreSQL persistence stores generic action/outbox data and does not reference vertical business policies.

See [Registering document actions](/guides/document-actions-and-work-center) and the [API reference](/reference/document-actions-work-center-api).
