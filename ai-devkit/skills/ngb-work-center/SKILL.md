---
name: ngb-work-center
description: Implement NGB Work Center tasks and notifications, outbox event policies, recipient selection, deduplication and user preferences.
---

Use for event-driven user work; do not turn a display
notification into a new business operation without an application requirement.

1. Read [Work Center]({{references}}/work-center.md). Confirm the event,
   source reference, recipients, deduplication key and transaction owner.
2. Implement the public policy/service contract and explicit registration.
   Keep batches bounded and publish user changes only after commit.
3. Use the public query and UI contracts for claim/read/snooze/preferences.
   Preserve expected versions and current-user/role access.
4. Test repeated delivery, concurrent claim, rollback, partial failure/retry,
   disabled preferences and worker restart against actual storage.
5. Run `node infrastructure/ngb.mjs validate --profile build` and
   `node infrastructure/ngb.mjs test --profile all`.

Done: repeated delivery preserves deduplication, committed changes reach their
intended recipients, and failed delivery is observable.

For a separate Hangfire worker or custom scheduled job, read
[background jobs]({{references}}/background-jobs.md).
