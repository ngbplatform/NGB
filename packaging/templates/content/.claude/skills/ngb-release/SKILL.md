---
name: ngb-release
description: Prepare an independent NGB application for delivery with aligned packages, verification evidence and explicit deployment prerequisites.
---

Use for delivery/release preparation. It does not authorize publication, deployment,
platform package promotion or production data changes by itself.

1. Read [delivery boundaries](../../../ai/vendor/references/release.md). Identify the exact target
   and artifact sources, lockfiles, runtime configuration and release stage.
2. Run `node infrastructure/ngb.mjs doctor`,
   `node infrastructure/ngb.mjs validate --profile build` and
   `node infrastructure/ngb.mjs test --profile all`.
3. Run `node infrastructure/ngb.mjs certify --profile external-application` where
   required. Report missing performance or other evidence as NOT READY.
4. For an authorized deployment, review the tested backup, migration sequence,
   secrets, identity/TLS settings and health/rollback procedure before using
   `node infrastructure/ngb.mjs deploy --backup-confirmed`.

Done: exact artifacts and relevant evidence agree, required gates are complete,
and deployment/publication status reflects only actions actually authorized and run.
