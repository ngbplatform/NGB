# External applications and upgrades

Use the official template to create an independent application, then update its
NuGet/npm dependencies through supported upgrade transitions. This page covers
application creation, extension boundaries and the upgrade contract.

The version-specific examples below target **3.2.0**, with **3.1.0 → 3.2.0** as the
registered upgrade transition. Registry commands require that target to be published;
use local packages before publication. A transition is certified only when its
candidate evidence passes and registry verification subsequently permits promotion.
Merely updating a version in this repository does not establish a published or
certified release.

## Create an application

### From published packages

Run these commands in the directory that should contain your new application,
after the target release is published. No NGB checkout is required:

```sh
dotnet new install NGB.Platform.Templates::3.2.0
dotnet new ngb -n MyApplication
cd MyApplication
node infrastructure/configure.mjs administrator@example.com
node infrastructure/ngb.mjs start
```

Use .NET 10, Docker Compose v2 and Node 24.19.x. The initializer writes random
secrets to an ignored, private `.env` and refuses to replace an existing file.
Open `http://localhost:5182`, using the configured email and `BOOTSTRAP_PASSWORD`
from the application's `.env`. Check `docker compose ps -a`, `docker compose logs
migrator` and `http://localhost:5181/health` before using the app. The Migrator must
exit with code 0. Keycloak is on port 5180 and the worker dashboard is at
`http://localhost:5184/hangfire`.

### Before publication

Run from the NGB repository root on macOS/Linux or in WSL2, using .NET 10, Node
24.19.x and Docker Compose. `--local` consumes an existing package set; it does not
build packages. If you already prepared a candidate, reuse it for application
creation; packaging and certification are not required for every new application.
On a fresh checkout, prepare the package set first:

```sh
npm --prefix ui ci
npm --prefix quality ci
node ui/scripts/pack-platform-ui.mjs --local-candidate
bash packaging/nuget/pack-platform.sh
bash packaging/nuget/verify-platform-packages.sh
node quality/external-consumer/release.mjs seal
```

Build the npm archive before NuGet: packing the template generates its lockfiles
and the CRM npm lockfile against the existing archive. Sealing creates
`artifacts/release-candidate/release-manifest.json` and copies the package files.
This prepares local development; it does not run certification or publish anything.

Once the package set is prepared, create and start the app with:

```sh
node ngb.mjs create MyApplication --local --start --email administrator@example.com
```

The CLI initializes `.env` itself; do not run `configure.mjs` again in this app.
After publication, replace `--local` with `--version 3.2.0`. Output defaults to a
sibling of the NGB checkout. `--output` selects another nonexistent directory outside
the checkout. Application names contain 2–64 letters/digits and start with a letter.

`--local` selects `artifacts/release-candidate`. If your candidate has another name,
pass the directory containing its `release-manifest.json` explicitly. For example,
to consume an already prepared `artifacts/release-candidate-next`:

```sh
node ngb.mjs create MyApplication --packages artifacts/release-candidate-next --start --email administrator@example.com
```

Local mode verifies package hashes and copies the feed and npm archive into the
application. Generated Dockerfiles need no edits. A sealed directory cannot be
overwritten. When changing the packaged platform or template, repeat packaging,
then seal into a new, nonexistent directory, for example:

```sh
node quality/external-consumer/release.mjs seal artifacts/release-candidate-updated
node ngb.mjs create MyNextApplication --packages artifacts/release-candidate-updated --start --email administrator@example.com
```

Keep previous candidates for diagnosis. Each candidate identifies one source and
package set; changing any tracked documentation also requires resealing before
release certification. Initial package preparation still needs registry access for
third-party dependencies. Local mode does not mean an offline installation.

### Starter contents and first login

The starter contains API, Migrator, Background Jobs and Vue projects, Keycloak and
PostgreSQL. It provides login, the platform shell, users, roles, permission editing,
audit history, Work Center, health endpoints and a real worker. It includes no
business vertical, empty extension projects, Watchdog, Seq, MinIO or pgAdmin.
`platform.schema.validate` is the initial registered job. Logs go to stdout.
Notes and Attachments are disabled by default; Notes do not require object storage.

From the application directory, `docker compose stop` stops the local services and
`docker compose start` resumes the existing containers with their saved data. To
rebuild and migrate an existing deployment, use the `deploy` procedure below.

The bootstrap identity has trusted Keycloak role `ngb-admin`. It works before a
platform user exists. Create/link its platform user by email and assign the
registered `application.administrator` role for user-specific Work Center behavior.
Active application administrators receive every registered permission, including
future permissions. Inactive accounts remain blocked, even with `ngb-admin`.
Changing a role's display name to Administrator grants nothing. Feature flags
remain independent of permissions.

Compose is a development deployment: loopback HTTP, Keycloak `start-dev`, private
PostgreSQL and non-root application containers. Production deployments must provide
TLS, production Keycloak, protected ingress/dashboard, managed secrets, restricted
DB accounts and tested backups. The browser runtime configuration is public.
`KeycloakSettings:MetadataAddress` optionally selects an internal discovery endpoint;
it does not change or disable JWT issuer validation.

## Grow only when needed

| Project | Responsibility | Allowed application references |
| --- | --- | --- |
| Definitions | Metadata, definitions, provider-neutral interfaces | None, or a dedicated abstractions library |
| Runtime | Validators, handlers, orchestration | Definitions/abstractions |
| PostgreSql | SQL, storage adapters, embedded migrations | Definitions/abstractions |
| API / Background Jobs | HTTP/worker composition | Definitions, Runtime, PostgreSql |
| Migrator | Discover and execute migration packs | PostgreSql migration assembly |

Runtime must not reference PostgreSql, Dapper, Npgsql or other concrete database
providers. PostgreSql must not reference Runtime. Domain layers must not import
hosting projects. Keep direct dependencies explicit. For an executable walkthrough,
see [Extend an external application](../guides/extend-external-application.md).

The template is for new applications. Installing a newer template never rewrites
an existing application. Upgrade existing applications through package, configuration
and source changes documented for the selected transition.

## Public frontend tooling

The public entry points include the root, `contracts`, `editor`, `layout`, `lazy`,
`navigation`, `work-center`, `styles`, `vite-public-assets` and `tailwind-preset`.
Use the packaged declarations and components; avoid private paths and monorepo aliases.
The preset extends the current Tailwind 3.4 architecture. PostCSS remains app-owned.
The generated Vite configuration installs the public asset plugin, and Tailwind
content includes application sources and `node_modules/@ngbplatform/ui/src`.

```js
import ngbPreset from '@ngbplatform/ui/tailwind-preset'

export default {
  presets: [ngbPreset],
  content: ['./index.html', './src/**/*.{vue,js,ts}', './node_modules/@ngbplatform/ui/src/**/*.{vue,js,ts}'],
}
```

The packed-package gate checks every entry point, declarations, assets, CSS tokens,
Vue props/emits/slots, peers, local PostCSS and production compilation outside the
workspace. Negative tests prove that incompatible changes fail the checker.

## Compatibility and supported transitions

Compatibility, certification and support are different promises:

- **Compatibility:** existing supported public APIs remain compatible within a
  major line. Every runtime NuGet package is compared against its first stable
  version in the major line and the selected previous release. npm contracts are
  compared with the published previous consumer.
- **Certified transition:** the exact source and target versions, app patch and
  persistent state pass the named runtime profiles.
- **Supported path:** the published migration guides identify the transitions
  users can follow. Adjacent certified steps do not imply a certified direct jump.

For a minor release, select the latest supported patch of the previous supported
minor and freeze its exact version. For a patch, select the previous supported
patch in that minor. Major upgrades require an explicit source range and migration
guide, including manual changes. No promise covers automatic compatibility of every
historical application.

The checked-in matrix selects **3.1.0 → 3.2.0**. Most runtime
packages have 3.0.0 as their major baseline. Attachments, Notes and Attachments.MinIO
were first published at 3.1.0. `NGB.Platform.Templates` is a new content package;
assembly compatibility and symbols do not apply. These exceptions are explicit in
`packaging/nuget/baselines.json`; there are no blanket runtime exemptions.

## Migrate 3.1.0 to 3.2.0

The automated tool currently supports this exact transition only. It expects one
root `.sln`/`.slnx`, a root `Directory.Build.props` with one unconditional
`NgbPlatformVersion`, `NuGet.Config` with explicit sources and source mapping, and
`web/package.json` with an exact `@ngbplatform/ui` dependency. Backend and frontend
must both start at 3.1.0. Explicit and centrally managed NGB package versions are
supported within that layout. Other layouts, conditional versions and custom
version expressions require a reviewed manual migration; the tool refuses them.

From the NGB checkout, preview and apply the supported dependency update:

```sh
node ngb.mjs upgrade --app ../MyApplication --to 3.2.0
node ngb.mjs upgrade --app ../MyApplication --to 3.2.0 --apply
```

Before publication, prepare the package set as described [above](#before-publication)
and use:

```sh
node ngb.mjs upgrade --app ../MyApplication --to 3.2.0 --local
node ngb.mjs upgrade --app ../MyApplication --to 3.2.0 --local --apply
```

Use `--packages` instead of `--local` for another local set, for example:

```sh
node ngb.mjs upgrade --app ../MyApplication --to 3.2.0 --packages artifacts/release-candidate-next
node ngb.mjs upgrade --app ../MyApplication --to 3.2.0 --packages artifacts/release-candidate-next --apply
```

The selected directory must already contain a sealed candidate. Generated apps
also carry `infrastructure/ngb.mjs`; run it from the application root and use an explicit
`--packages` path for local packages outside that application. Preview is read-only.
Apply validates both builds in an isolated copy before updating dependency files
and lockfiles. It also manages ignored local package copies, `NuGet.Local.Config`,
`.ngb/packages.json` and `.gitignore` entries when needed. User application source,
runtime configuration and secrets are preserved; originals of
changed files are retained under `.ngb/upgrades`. Concurrent edits, inconsistent
versions and unsupported transitions are rejected. Build validation does not run
the application's business tests. Applying does not stop services or run migrations.

1. Back up PostgreSQL, Keycloak state and optional object storage consistently.
   Test restore before the maintenance window. Record the current app/configuration
   and package versions. Stop API and worker writers before target migration.
2. Review the command's changes: `NgbPlatformVersion`, direct NGB dependencies
   and `@ngbplatform/ui` must resolve to exact `3.2.0`. Commit the dependency files
   and regenerated lockfiles. Preserve application code, data and storage volumes.
3. Keep existing local Tailwind configuration or adopt the public preset. For an
   API container using a public localhost issuer during development, configure the
   internal `MetadataAddress`; production still requires a valid trusted issuer.
   No other application source change is required by the frozen core consumer.
4. Build the matching hosts/frontend and run the target Migrator once. Keep API
   and workers stopped if it fails. Resolve the reported issue and follow the
   migration's documented retry procedure. Repeating successful migrations must
   preserve checksums/history and must not duplicate seed data or effects.
5. Start the target API, worker and frontend. Verify health, login, role/audit flows,
   pending jobs and the application's own business scenarios before reopening traffic.

After reviewing the dependency diff, testing the app and taking a consistent backup,
deploy the generated Compose topology with:

```sh
node infrastructure/ngb.mjs deploy --backup-confirmed
```

For an app without the bundled tool but with the compatible Compose topology, use
`node /path/to/NGB/ngb.mjs deploy --app /path/to/MyApplication --backup-confirmed`.
This requires buildable services named `api`, `jobs`, `web` and `migrator`, with API
and jobs depending on successful migration. PostgreSQL and Keycloak must already be
running; `deploy` does not start dependencies. The command builds images, stops
writers, runs Migrator and starts hosts only after successful migration. Custom
Compose/Kubernetes topologies retain their deployment procedure. An ordinary
`start` refuses existing containers; use `docker compose start` to resume them.
During `deploy`, Migrator output is streamed to the terminal from a temporary
container that is removed afterwards. The `migrator` service's old logs are not
evidence of this deployment; retain the deploy output and check current API/worker
health before reopening traffic.

New apps generated at 3.2.0 are already on the target; the 3.1.0 → 3.2.0 upgrade
command is not a refresh command for those apps. A future target requires a
registered transition and, where specified, a newer CLI. Template installation or
dependency upgrade does not replace an existing app's bundled tool or source files.

Do not use `down --volumes` against application data. Downgrading binaries does not
undo database migration. Recovery restores the complete consistent backup set unless
the transition publishes and validates a more specific recovery path. Mixed-version
workers, rolling deployments and zero-downtime upgrades are not certified in 3.2.0.

## Certification scope and evidence

Five mandatory profiles run outside the repository, using exact packed artifacts,
fresh application/package extraction directories, explicit package sources and no
workspace/source fallback. Download caches may be shared within the same candidate;
the clean-starter profile and registry smoke also use fresh download caches.

| Profile | Evidence |
| --- | --- |
| clean-starter | Installed template, real login/role lifecycle/audit, admin contract, worker job, health/logs, migration sequencing; no MinIO |
| generated-extension | Fresh generated app plus custom catalog, Runtime validator/handler, PostgreSQL provider and app migration |
| core-upgrade | Frozen published 3.1.0 app, maintenance stop, target migration on the same state, preserved data/grants/audit/history, continued custom behavior and queued-job idempotence |
| notes-upgrade | Existing and new Notes, history and authorization; Attachments disabled and no MinIO |
| attachments-upgrade | Existing/new attachment metadata and objects, matching download SHA-256, authorization and private MinIO |

The source fixture's files and registry provenance are immutable. Candidate patches
are recorded separately. Infrastructure container/volume identities remain constant
across each upgrade. The original UI flow runs before the version change; recreating
target state is not an upgrade test.

The fixture migration `V2026_10_06_0100__certification_checkpoint.sql` belongs to
the certification app, not to the default starter or platform migration pack.
During upgrade certification its Migrator applies it to the disposable
`certification` database on local port 5183 before creating source-version data.
The same database is then reused for the target-version migration and preservation
checks. The generated-extension profile installs the example's DDL separately.
Normal template creation does not create either checkpoint table. Following the
extension tutorial intentionally adds them to that application's own database.

Documents, relationships, accounting, operational/reference registers, reporting and
projections retain the complete existing regression gates. They are not newly
claimed external upgrade scenarios in this release. PM, Trade, Agency Billing and
CRM remain covered by their complete backend/frontend aggregates and stable volume
and performance-tooling tests. The candidate's Compose gate builds the generated
starter. It does not build the existing vertical Docker images, including CRM;
those have a separate container-build gate after package publication.

`quality/external-consumer/matrix.json` binds 20 candidate gates and two
post-publication gates to commands and assertions. `quality-inventory.json`
classifies measured executable helpers, generated code, static content and process
orchestration. Coverage thresholds remain 100%.
Process orchestration uses real behavior and failure tests, not synthetic coverage.

## Certified package identity

Certification records exact NuGet/npm package identities, the frozen source
fixture and the upgrade matrix. Registry verification checks published NuGet
signatures and canonical payloads, npm integrity and a clean starter restoring
only from registries. A release satisfies the upgrade contract only after both
certification and registry verification pass.
