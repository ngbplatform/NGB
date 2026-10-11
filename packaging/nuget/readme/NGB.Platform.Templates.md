# NGB.Platform.Templates

Install with `dotnet new install NGB.Platform.Templates::3.2.0`, then run
`dotnet new ngb -n MyBusinessApp`. The generated README explains configuration,
migration, Administrator bootstrap and deployment.

The starter includes API, a one-shot Migrator, Background Jobs and a Vue client.
PostgreSQL and Keycloak provide local infrastructure. Dependencies come from public
NGB packages; generated applications have no repository or workspace dependency.

Existing applications upgrade their packages and apply the published migration
guide. Reinstalling this template does not upgrade an existing application.
