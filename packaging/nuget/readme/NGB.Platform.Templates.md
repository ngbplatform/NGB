# NGB.Platform.Templates

Install the latest stable template from NuGet and create an application.

```bash
dotnet new install NGB.Platform.Templates
dotnet new ngb -n MyBusinessApp
```

The generated README explains configuration, migration, Administrator bootstrap,
the AI Development Kit and deployment.

The starter includes API, a one-shot Migrator, Background Jobs and a Vue client.
PostgreSQL and Keycloak provide local infrastructure. The backend uses the
`NGB.Platform.*` packages from NuGet and the Vue client uses `@ngbplatform/ui` from npm.

The starter includes versioned Codex and Claude Code guidance, public contract
references, a shared CLI and backend, integration, frontend and browser tests.

To upgrade an existing 3.3.0 application, follow the
[3.3.0 → 3.4.0 migration guide](https://docs.ngbplatform.com/architecture/external-app-upgrades#migrate-3-3-0-to-3-4-0).
From the application root, preview the update with
`npm exec --ignore-scripts --package=@ngbplatform/cli@3.4.0 -- ngb upgrade --app . --to 3.4.0`.
Review the plan, then run the same command with `--apply`.
Reinstalling this template does not upgrade an existing application.
