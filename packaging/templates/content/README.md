# NgbApplication

An independent NGB application: API, one-shot Migrator, Background Jobs and
Vue. It uses official NuGet/npm packages. No NGB repository checkout is required.
The initial UI provides users, roles, permission editing, role audit history and
Work Center. Notes and Attachments are disabled by default.

## Start locally

Prerequisites: Docker with Compose v2, Node.js 24.19 or later in the 24.x line,
and .NET 10 SDK for host development. The generated lockfiles pin dependencies.

```sh
node infrastructure/configure.mjs administrator@example.com
node infrastructure/ngb.mjs start
docker compose ps -a
docker compose logs migrator
```

The initializer generates random secrets into a private, ignored `.env` file and
refuses to overwrite it. Read `BOOTSTRAP_PASSWORD` locally; secrets are not printed
or checked in. Open http://localhost:5182 and log in using the configured email
and password. The local Keycloak realm uses PKCE for the browser and a separate,
limited service client for user administration.

If the NGB `create` command created this app, `.env` is already initialized. Skip
`configure.mjs` and run only `start` if creation did not include `--start`.

The `start` command builds and starts a new Compose deployment. If containers already
exist, use `docker compose start` to resume them, or the explicit `deploy` command
below to rebuild and migrate.

Before package publication, first [prepare local packages](https://docs.ngbplatform.com/architecture/external-app-upgrades#before-publication).
Then run this command from the NGB checkout:

```sh
node ngb.mjs create MyApplication --local --start --email administrator@example.com
```

It creates a sibling application directory, installs the packed template in an
isolated template cache and verifies the release manifest and package hashes.
`--packages /path/to/candidate` selects another local set; `--output /path/to/MyApplication`
selects a nonexistent destination. No Dockerfile edits are needed. The application
owns package copies and has no references to NGB source projects.
`--local` requires an existing `artifacts/release-candidate/release-manifest.json`;
it does not package the platform or run release certification.

Local package state is ignored by Git. `NuGet.Config` retains registry consumption;
`NuGet.Local.Config` adds the local NGB feed while preserving other sources. For host
development with local packages, restore with `--configfile NuGet.Local.Config` and
run `npm cache add .ngb-packages/ui.tgz` from `web` before `npm ci`. The application
command configures these automatically for Compose.

The first identity has the trusted Keycloak role `ngb-admin`, so it can administer
the application before a platform user exists. On **Users**, create/link the same
email and assign the **Administrator** role. This enables personal Work Center
features as well. Active users assigned the registered `application.administrator`
role receive all registered capabilities, including new ones, without manual grants.
An inactive platform user stays blocked, even with `ngb-admin`. Renaming an ordinary
role to Administrator grants no additional access. Deployment flags remain independent.

Create an ordinary role, assign permissions, save, reopen and edit it, then inspect
its audit tab. These are real platform operations backed by PostgreSQL and Keycloak.

API readiness is at http://localhost:5181/health. The worker dashboard uses
http://localhost:5184/hangfire; sign in with the bootstrap administrator. Run the registered
`platform.schema.validate` job and inspect its outcome. API and worker write
structured JSON logs to stdout. No Seq or separate logging service is required.

Compose starts API and worker only after the Migrator exits successfully. A failed
migration blocks both hosts. The Migrator applies platform migration packs, then
idempotently creates the registered Administrator role. It never seeds business
records. PostgreSQL stores the application, Keycloak realm, and Hangfire state.

## Develop on the host

```sh
dotnet restore NgbApplication.slnx --locked-mode --configfile NuGet.Config
dotnet build NgbApplication.slnx --no-restore -c Release
cd web
npm ci --workspaces=false
npm run build
cd ..
```

Configure host connection strings, Keycloak issuer/client IDs and the API's
Keycloak admin-client settings through environment variables or user secrets.
Compose does not expose PostgreSQL to the host by default; a host development
configuration must provide a reachable database endpoint. Use `NuGet.Local.Config`
and the npm cache step above when developing with unpublished local packages.
`NGB_CONNECTION_STRING` configures the Migrator. Run the following before hosts:

```sh
dotnet run --project NgbApplication.Migrator --
dotnet run --project NgbApplication.Migrator -- seed-administrator
```

The API and worker never run migrations on startup. The platform CLI's `--dry-run`,
`--list-modules`, `--info` and other documented commands do not seed the role.

## Grow the application

Add projects only when they have work to do:

- `NgbApplication.Definitions`: metadata, definitions and application contracts;
  no SQL, provider implementations, HTTP hosting or runtime orchestration.
- `NgbApplication.Runtime`: handlers, validators and orchestration; references
  Definitions and provider-neutral NGB contracts/persistence abstractions.
- `NgbApplication.PostgreSql`: SQL, embedded migrations and persistence adapters;
  references Definitions/contracts plus NGB PostgreSql, Dapper or Npgsql as needed.
- API and worker compose the modules. Migrator references the provider migration
  assembly and anchors it with `typeof(YourMigrationPackContributor).Assembly`.

Runtime must not reference PostgreSql, Dapper or Npgsql. The provider must not
reference Runtime. Move shared interfaces to Definitions or a small abstractions
project, not into an orchestration assembly. Declare direct project dependencies.
Register definition-bound handlers by their concrete type and their contract, as
shown in the extension walkthrough below.
Keep `AddNgbRuntimeStartupValidation()` enabled to reject incomplete registrations.
Use immutable, versioned application migrations with a pack depending on `platform`.

The complete runnable extension walkthrough is published at
https://docs.ngbplatform.com/guides/extend-external-application.

## Frontend contract

Use `@ngbplatform/ui/tailwind-preset` and keep PostCSS configuration in this app.
The supported starter toolchain is Tailwind 3.4, PostCSS 8, Vite 7 and Vue 3.5.
Tailwind 4 is outside the supported starter toolchain. Include packaged UI sources in
Tailwind content discovery and use the public Vite asset plugin. All imports must
use documented package entry points; do not copy platform components or reference
workspace files. `web/public/runtime-config.js` contains public browser configuration,
never service credentials. Update it for your deployment URLs.

## Optional content capabilities

Notes need PostgreSQL and an enabled `FeatureManagement:Notes` flag. They do not
need MinIO. To enable Attachments, reference `NGB.Platform.Attachments.MinIO` at the
same `NgbPlatformVersion` defined in `Directory.Build.props`, configure private
object storage, and pass the storage registration callback to
`AddNgbAttachmentsNotesApi`. Configure CORS for the exact web origin and private
presigned upload/download endpoints. Enable `FeatureManagement:Attachments` only
after storage validation passes. Use the platform parent-object authorization;
do not expose buckets publicly or implement a separate application ACL.

## Upgrade and deploy

Select a supported transition from the [upgrade guide](https://docs.ngbplatform.com/architecture/external-app-upgrades)
and replace `TARGET_VERSION` below with its exact target version. Preview and apply
from the NGB checkout without copying or regenerating application source:

```sh
node ngb.mjs upgrade --app ../MyApplication --to TARGET_VERSION
node ngb.mjs upgrade --app ../MyApplication --to TARGET_VERSION --apply
```

Before publication, add `--local`; other artifact locations use `--packages /path/to/candidate`.
The currently registered transition is listed in the guide. The tool requires one
root solution, `NgbPlatformVersion` in `Directory.Build.props`, a mapped
`NuGet.Config`, and the `web` frontend. Other layouts require manual migration.
Preview writes nothing. Apply updates NGB versions and lockfiles and validates both
builds in a private temporary copy. It also manages ignored local packages, their
configuration and `.gitignore` entries. Changes are applied only after both builds pass.
Application source, secrets, data and running services remain untouched. Originals are
saved in `.ngb/upgrades`. Concurrent edits, mixed versions, conditional platform
versions and unsupported transitions stop the upgrade. Dependency-file backups are
not database backups.

New apps carry the tool at `infrastructure/ngb.mjs`. Future releases must register
their transitions and document any required tool update. Read the migration guide
for application-specific configuration and tests. Installing a template never
rewrites an existing app.

Before an upgrade, take consistent backups of PostgreSQL, Keycloak and optional
object storage. Stop API and worker writers. Run the target Migrator against the
same state, then start the matching API, worker and frontend. On migration failure,
keep target hosts stopped, inspect the error and follow the migration guide's
retry/recovery steps. Do not downgrade a migrated database in place.

```sh
node infrastructure/ngb.mjs deploy --backup-confirmed
```

The command validates the Compose topology, builds images, stops writers, runs
Migrator and starts hosts only after successful migration. PostgreSQL and Keycloak
must already be running. `--backup-confirmed`
confirms that you took and tested the consistent backup; it does not create one.
Existing apps without the bundled tool can run `node /path/to/NGB/ngb.mjs deploy
--app /path/to/MyApplication --backup-confirmed` if they have the compatible Compose
services `api`, `jobs`, `web` and `migrator`. Custom deployment topologies keep
their own deployment pipeline. Preserve database and storage volumes.
Do not use `down --volumes` on application data. Repeating the
Migrator must not duplicate data or reactivate/rename an existing Administrator role.

This Compose file is a loopback-only development environment, including Keycloak
`start-dev` and HTTP metadata. For production, use a managed deployment with TLS,
production Keycloak, secrets management, least-privilege database accounts,
backup/recovery and explicit ingress. Keep JWT issuer validation enabled; the optional
`KeycloakSettings:MetadataAddress` changes only the private discovery address.
Production metadata requires HTTPS unless an explicitly trusted internal transport
is configured. Apply the same deployment controls to the jobs dashboard.
