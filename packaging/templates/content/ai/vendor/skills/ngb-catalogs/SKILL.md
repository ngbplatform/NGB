---
name: ngb-catalogs
description: Add or change NGB catalog metadata, typed storage, upsert validation and catalog API/UI behavior in an external application.
---

Use for catalog implementation, not document posting or arbitrary database tables.
Start with the installed SDK version, existing definitions and provider registrations.

1. Read [catalog contracts](../../references/catalogs.md). Identify the type code,
   fields, parts, supported hierarchy and required public storage interface.
2. Add the definition, provider adapter and immutable migration in their respective
   projects. Register the contributor and storage explicitly; metadata is not DI.
3. Specify invalid payloads and authorization outcomes before implementing the
   validator. Use the public application service and shared UI contracts.
4. Run `node infrastructure/ngb.mjs validate --profile build`,
   `node infrastructure/ngb.mjs test` and
   `node infrastructure/ngb.mjs test --profile integration`.
5. For UI changes, run `node infrastructure/ngb.mjs test --profile e2e` and check
   that an unauthorized user is rejected by the backend as well as the UI.

Done: definition/storage/migration agree, real CRUD/filter paths and negative
cases pass, and startup/repeated migration preserve existing records.
