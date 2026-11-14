---
name: ngb-security
description: Configure NGB Keycloak identity, registered permissions, administrator roles, feature flags and matching API/UI access checks.
---

Use for an authorized identity/access change or permission-sensitive feature.
Do not change business permissions to make an MCP or Assistant scenario pass.

For demo generation, provision a readable invented identity, active platform user
and role assignment for every employee role, including Administrator. This is a
Kit responsibility, not a required addition to the business specification. Follow
[demo identity and acceptance rules](../../../ai/vendor/references/application-generation.md#demo-identities).

1. Read [security contracts](../../../ai/vendor/references/security.md). Identify the existing
   authorized service, capability keys, record scope and optional feature flag.
2. Register new permission definitions through the public contract and preserve
   explicit administrator role codes. Display names never determine administration.
3. Enforce backend access and align navigation/action availability with it.
   Keep inactive-user and feature checks independent of administrator capability.
4. Test anonymous, limited user, active linked administrator, trusted `ngb-admin`
   without a platform record, inactive user and disabled feature where applicable.
5. Run `node infrastructure/ngb.mjs test --profile integration` and
   `node infrastructure/ngb.mjs test --profile e2e` against disposable identity/storage.

Done: existing administrators can use new capabilities without manual grants,
unauthorized calls fail at the backend, and UI results match the access contract.
