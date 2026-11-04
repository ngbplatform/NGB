# NGB.Platform.Templates

After 3.4.0 is published, install with `dotnet new install NGB.Platform.Templates@3.4.0`, then run
`dotnet new ngb -n MyBusinessApp`. The generated README explains configuration,
migration, Administrator bootstrap, the AI Development Kit and deployment.

The starter includes API, a one-shot Migrator, Background Jobs and a Vue client.
PostgreSQL and Keycloak provide local infrastructure. Dependencies come from public
NGB packages; generated applications have no repository or workspace dependency.

The starter includes versioned Codex and Claude Code guidance, public contract
references, a shared CLI and backend, integration, frontend and browser tests.

Existing 3.3.0 applications follow the 3.3.0 → 3.4.0 migration guide. After publication,
use `npm exec --ignore-scripts --package=@ngbplatform/cli@3.4.0 -- ngb upgrade --app . --to 3.4.0`
to preview the coordinated update, then add `--apply` after reviewing the plan.
Reinstalling this template does not upgrade an existing application.
