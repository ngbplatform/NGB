# Ownership and upgrade rules

`ai/vendor/` and rendered `ngb-*` skills are official content. `ai/custom/`, custom
skill sources, project code, tests and `ngb.config.json` belong to the application.
Root `AGENTS.md`/`CLAUDE.md` contain bounded NGB blocks; text outside them survives
byte-for-byte. Custom `app-*` adapters are generated from their canonical sources.

`ai sync` without `--to` uses the installed Kit. It does not discover a new release.
A different SDK version needs `upgrade --to <exact-version>`, which coordinates
SDK, UI, CLI and Kit changes. A new template installation does not rewrite an
existing application. Preview never writes application files or local caches.

Three-way comparison uses verified old bytes, local bytes and incoming bytes.
Unchanged files update; local-only changes remain customized. Changed upstream
content conflicting with local edits requires a decision. Local deletion is a
change. New official paths never implicitly adopt existing unowned files.

Resolution data has `schemaVersion: 1`, the preview's `planSha256`, and a `decisions`
array. Each decision contains its conflict `id` and `choice`: `local`, `incoming`
or `replacement`. A replacement also contains reviewed UTF-8/LF `content`.
Decisions are not executable hooks. Any input change invalidates the plan digest.
Keeping a customization does not prove that its required contracts still hold.

Applied changes retain a private recovery receipt under `.ngb/transactions/`.
An incomplete transaction blocks further writes until recovery is reviewed with
`node infrastructure/ngb.mjs recover`. Read [interrupted transactions](recovery.md)
before applying recovery; never delete its journal or lock to suppress a
diagnostic. Database migration and deployment remain separate, with the
established backup requirement.
