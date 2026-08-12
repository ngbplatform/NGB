# Document Actions and Work Center API

All endpoints require the host's normal authentication. Document/action results are filtered by the current permission snapshot.

## Document Actions

### Read editor state

`GET /api/documents/{documentType}/{id}/editor-state`

Returns:

```json
{
  "document": {},
  "documentVersion": 7,
  "actions": [
    {
      "code": "post",
      "label": "Post",
      "kind": "Primary",
      "executionKind": "Command",
      "order": 100,
      "isAllowed": true,
      "disabledReasons": []
    }
  ]
}
```

Unauthorized actions are absent. Disabled actions contain reason objects.

### Execute an action

`POST /api/documents/{documentType}/{id}/actions/{actionCode}`

Required header: `Idempotency-Key`.

Body:

```json
{
  "expectedVersion": 7,
  "payload": null,
  "reason": null
}
```

The response contains execution ID, updated document/version/actions, `workCenterMayChange`, and an optional created document.

Legacy lifecycle URLs for post/unpost/repost/mark/unmark are thin delegates to the same dispatcher. The former generic derive and derive-actions HTTP routes were removed; derivations are actions.

## Work Center

### Summary

`GET /api/work-center/summary?vertical={vertical}`

Returns attention, open-task, overdue-task, active-notification, unread-notification counts, and
version. `vertical` is optional; when supplied, every count is scoped to that vertical.

### Feed

`GET /api/work-center/items`

Query parameters:

- `cursor`;
- `limit` (1–100, default 30);
- `tab` (`attention`, `tasks`, `notifications`, `completed`);
- `vertical`;
- `priority`;
- `severity`;
- `overdue`;
- `unread`.

Returns `items`, `nextCursor`, and the effective limit. The cursor is opaque. The full-page and
drawer clients load the first bounded page and automatically request `nextCursor` as the scroll
sentinel approaches the viewport. A later-page failure preserves already loaded rows and can be
retried without restarting the feed.

### Notification mutations

- `POST /api/work-center/notifications/{id}/read`
- `POST /api/work-center/notifications/{id}/dismiss`

### Task mutations

- `POST /api/work-center/tasks/{id}/read`
- `POST /api/work-center/tasks/{id}/claim` with `{ "expectedVersion": 3 }`
- `POST /api/work-center/tasks/{id}/snooze` with a UTC `snoozedUntilUtc`

Claim uses optimistic concurrency and returns a conflict if another user claimed or changed the role task.

### Preferences

- `GET /api/me/notification-preferences`
- `PUT /api/me/notification-preferences`

The update body contains a `preferences` array with definition `code`, `channel`, and `isEnabled`. Mandatory/non-disableable definitions reject attempts to disable them. A disabled task definition prevents that user from becoming a recipient of subsequently created tasks of that type. It has no effect on independent notification definitions.

`GET` returns only definitions applicable to the current user's active roles. Every returned
definition includes its explicit `kind` (`Task` or `Notification`), concrete display name,
category, description, effective state, default state, and mutability flags. Task and notification
preferences are independent. Creating a task never creates an assignment notification.

`PUT` rejects a definition that is not applicable to the current user's roles. Delivery repeats
the same role check, so a stale client or direct API call cannot subscribe a user to another role's
notifications. The sample CRM response contains three entries under `CRM Tasks` and two under
`CRM Notifications` for `crm.sales_rep`; users without that role receive none of those entries.

### Realtime

Connect to `/hubs/work-center`. The exact SignalR event name is `workCenterChanged`; it carries a monotonic version. Reload summary/feed when the version advances or after reconnect.

## Error behavior

The platform problem-details envelope supplies a stable error code. Common categories are:

- permission denied or action omitted;
- action unavailable with disabled-reason codes;
- stale document/task version;
- missing/invalid idempotency key;
- idempotency fingerprint conflict;
- action already in progress;
- invalid confirmation reason;
- unknown action/target/definition.

Clients must not retry validation/permission failures blindly. Retrying a transient failure must reuse the same idempotency key and request body.
