---
name: ngb-persistence
description: Implement NGB provider storage, immutable migration packs, deterministic seeds, transaction boundaries and bounded PostgreSQL queries.
---

Use for an authorized persistence change. Kit guidance does not authorize live
database/schema mutations or unrelated changes to existing business calculations.

1. Read [persistence contracts]({{references}}/persistence.md). Inspect the installed
   storage interface, unit-of-work lifetime, pack dependencies and current schema.
2. Keep provider code in its declared layer. Write immutable versioned migrations,
   stable seed identities and parameterized, bounded SQL.
3. Test empty initialization, actual previous-version data, repeated execution,
   failure sequencing and rollback behavior on disposable PostgreSQL.
4. Verify batch/query counts, indexes and paging boundaries with representative data.
   Preserve shared transactions for document state, effects and audit.
5. Run `node infrastructure/ngb.mjs validate --profile build` and
   `node infrastructure/ngb.mjs test --profile integration`.

Done: data/history survive supported migration paths, seeds are idempotent and
measured queries remain bounded. Deployment and tested backups are separate steps.
