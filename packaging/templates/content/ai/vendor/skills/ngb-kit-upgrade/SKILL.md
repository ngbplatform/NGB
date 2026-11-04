---
name: ngb-kit-upgrade
description: Inspect NGB Kit ownership, synchronize custom skills and resolve instruction conflicts during exact-version application upgrades.
---

Use this for Kit drift, custom skill rendering and coordinated SDK/Kit upgrades.
It does not deploy an application or migrate a running database.

1. Inspect `ai/kit.json`, current SDK/UI versions and developer changes. Read
   [ownership and upgrades](../../references/upgrades.md).
2. Run `node infrastructure/ngb.mjs ai status --format json` and
   `node infrastructure/ngb.mjs ai sync --format json` to inspect the current
   version. Preserve all `ai/custom/` sources and text outside root managed blocks.
3. Review each conflict's old, local and incoming content. Bind explicit choices
   to the preview's plan digest and conflict IDs. Never use blanket overwrite,
   edit the manifest to claim files or discard independently edited adapters.
4. Apply the reviewed plan using `ai sync --apply --resolutions <file>`. If files
   changed after review, regenerate the plan and review the new conflicts.
5. A different SDK version requires the registered coordinated `upgrade --to`
   transition and matching target CLI. Do not update only `ai/vendor/` or use an
   unversioned/latest tool acquisition. Preview before applying and preserve the
   transaction receipt until validation is complete.
6. If an operation was interrupted, read [recovery](../../references/recovery.md),
   inspect `node infrastructure/ngb.mjs recover --format json`, and review the
   proposed rollback before using `recover --apply`. Preserve subsequent edits.

Done means SDK/UI/CLI/Kit identities align, custom sources survive, both agent
adapters agree with their canonical sources, and required checks actually pass.
