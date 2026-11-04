# NgbApplication

An independent NGB application: API, one-shot Migrator, Background Jobs and
Vue. It uses official NuGet/npm packages. No NGB repository checkout is required.
The initial UI provides users, roles, permission editing, role audit history and
Work Center. Notes and Attachments are disabled by default.

## AI Development Kit and verification

The matching AI Development Kit is installed locally in `ai/vendor/`, with
`AGENTS.md`, `CLAUDE.md` and skills for Codex and Claude Code. Open this application
in your IDE or terminal. Agent installation and model configuration are separate;
no IDE extension or NGB checkout is required to read the installed guidance.

Keep application instructions in `ai/custom/instructions.md`. Author custom skills
once in `ai/custom/skills/app-*/SKILL.md`; generated adapters direct each agent to
that canonical file so relative references retain their original base directory.
Preview changes before applying them:

```sh
node infrastructure/ngb.mjs ai status --format json
node infrastructure/ngb.mjs ai sync
node infrastructure/ngb.mjs ai sync --apply
```

The CLI preserves text outside the marked blocks in root instruction files.
Locally edited or deleted managed files require review. Use hash-bound choices
with `--resolutions reviewed-resolutions.json` when a conflict is reported.
Unrelated agent skills and application sources remain application-owned.

`ngb.config.json` declares this application's projects, layer roles and test
suites. Update it when adding projects. The same CLI runs locally and in CI:

```sh
node infrastructure/ngb.mjs doctor --format json
node infrastructure/ngb.mjs validate
node infrastructure/ngb.mjs validate --profile build
node infrastructure/ngb.mjs test --profile unit
node infrastructure/ngb.mjs test --profile integration
node infrastructure/ngb.mjs test --profile e2e
node infrastructure/ngb.mjs test --profile all
node infrastructure/ngb.mjs certify --profile external-application
```

Default validation inspects files without restoring dependencies or executing
application code. The build and test profiles execute trusted project tooling.
Unit tests include frontend coverage; backend suites emit coverage reports.
The complete test profile checks the full compiled backend source inventory at
100% lines, branches and methods, and frontend sources at 100% lines, branches,
functions and statements. Missing tests, skipped tests and missing required
evidence fail the profile. Reports and logs are under `.ngb/reports/<run-id>/`.

Integration/E2E use disposable PostgreSQL and Keycloak resources. They do not use
the deployment `.env`. Docker with Compose and Playwright Chromium are required
for browser tests; the CLI installs the browser into its isolated execution home.
Certification additionally requires performance and business acceptance evidence;
a successful build or unit profile alone does not establish certification.

Installed reference examples live in `ai/vendor/examples/`. Copy an example to a
separate directory before restoring/building it, and follow its README. The
document example records the SDK's unsupported zero-effect Post; it does not
change the platform lifecycle or introduce a fictitious effect handler.

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
If the package set is already prepared in another directory, use it directly:

```sh
node ngb.mjs create MyApplication --packages artifacts/release-candidate-next --start --email administrator@example.com
```

Replace `artifacts/release-candidate-next` with your existing candidate directory.
One package set can create multiple independent applications without being repacked.

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

For published packages, run from the application directory:

```sh
dotnet restore NgbApplication.slnx --locked-mode --configfile NuGet.Config
dotnet build NgbApplication.slnx --no-restore -c Release
cd web
npm ci --workspaces=false
npm run build
cd ..
```

For an app created with unpublished local packages, use this sequence instead:

```sh
dotnet restore NgbApplication.slnx --locked-mode --configfile NuGet.Local.Config
dotnet build NgbApplication.slnx --no-restore -c Release
cd web
npm cache add .ngb-packages/ui.tgz
npm ci --workspaces=false
npm run build
cd ..
```

Configure host connection strings, Keycloak issuer/client IDs and the API's
Keycloak admin-client settings through environment variables or user secrets.
Compose does not expose PostgreSQL to the host by default; a host development
configuration must provide a reachable database endpoint.
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

## Optional MCP endpoint

The API includes the public `NGB.Platform.Mcp.AspNetCore` package and the
`AddNgbMcp` / `MapNgbMcp` composition. MCP is disabled by default. To enable it in
the local Compose API, set this in the private `.env` file and recreate the API:

```dotenv
FEATURE_MCP=true
MCP_ALLOW_LOCALHOST_HTTP=true
```

Compose sets `Ngb__Hosting__PublicApiUrl` from `API_PUBLIC_URL`, defaulting to
`http://localhost:5181`, and permits local HTTP only with
`MCP_ALLOW_LOCALHOST_HTTP=true`. MCP derives `/mcp` from that shared API address.
Production sets the common public HTTPS URL and
keeps `Mcp__AllowLocalhostHttp=false`. `Mcp__Resource` is an optional full URL
override for a separately exposed endpoint, not a required second API address.

Use the existing user's API access token. MCP forwards authentication to the
same API JWT bearer handler, including its issuer, signature, lifetime and
configured audience/client checks. In Postman choose **Bearer Token** and paste
the access token without the `Bearer ` prefix. No new Keycloak user, client or
MCP audience mapper is needed to reuse that token. ID/refresh tokens, service
accounts and inactive users are rejected. The normal Administrator policy
remains effective. Remove obsolete `Mcp:Audience` settings.

To obtain tokens through a client's interactive OAuth flow, use Authorization
Code with PKCE S256 and an existing registration that allows its callback URI.
This is separate from connecting with an already issued API access token.

Connect a Streamable HTTP client to that URI. Protected Resource Metadata is
available at `http://localhost:5181/.well-known/oauth-protected-resource/mcp`.
Use the current SDK discovery flow or a supported compatibility client, then
call `ngb_list_metadata` with `{ "limit": 10 }`. The empty starter exposes only
installed platform definitions; custom modules extend generic reads automatically.

For production, use a canonical public HTTPS resource URI and the existing
trusted-proxy configuration. Set `Mcp:AllowedOrigins` only for actual browser
clients. Configure `Mcp:PublicUiOrigin` and `Mcp:UiRoutes` only when your UI has
those routes; absent routes return null links. The
[MCP guide](https://docs.ngbplatform.com/guides/mcp-server) describes limits,
permission checks, optional contributors, OAuth and client compatibility.

## Optional AI Assistant

The shared UI and API include AI Assistant, disabled by default. Enable both
`FEATURE_MCP=true` and `FEATURE_AI_ASSISTANT=true` and run native Ollama on the
host with the `qwen3.5:9b` model. Compose uses
`AI_OLLAMA_ENDPOINT=http://host.docker.internal:11434`; explicitly set
`AI_OLLAMA_ALLOW_INSECURE_HTTP=true` for this local API-to-host connection. An API
running directly on the host uses `http://localhost:11434` without the opt-in.
`AiAssistant:Ollama:AllowInsecureHttp` defaults to `false`; other HTTP endpoints
also require explicit permission. Use HTTPS and leave the option disabled for
production network connections.

The assistant uses the existing user's MCP permissions. Conversations remain in
browser memory. The developer can replace `AddNgbOllamaChatClient` with another
`IChatClient` registration. See the [AI Assistant guide](https://docs.ngbplatform.com/guides/ai-assistant)
for model providers, startup requirements, limits and deployment configuration.

## Upgrade and deploy

Select a supported transition from the [upgrade guide](https://docs.ngbplatform.com/architecture/external-app-upgrades)
and replace `TARGET_VERSION` below with its exact target version. Preview and apply
from this application:

```sh
node infrastructure/ngb.mjs upgrade --to TARGET_VERSION
node infrastructure/ngb.mjs upgrade --to TARGET_VERSION --apply
```

Before publication, select an approved sealed set with `--packages /path/to/candidate`.
The currently registered transition is listed in the guide. The tool requires one
root solution, `NgbPlatformVersion` in `Directory.Build.props`, a mapped
`NuGet.Config`, and the `web` frontend. Other layouts require manual migration.
Preview writes nothing inside the application; verified target CLI acquisition
uses an external cache. Apply prepares matching SDK/UI, CLI/helper and Kit changes
in a private temporary copy and validates builds before committing one transaction.
It also manages ignored local packages, their configuration and `.gitignore`
entries. Application code, tests and configuration require application review.
Originals and recovery receipts are saved in `.ngb/transactions/<id>/`.
Concurrent edits, mixed versions, conditional platform versions and unsupported
transitions stop the upgrade. File backups are not database backups.

After an interrupted update, inspect the recovery plan before applying it:

```sh
node infrastructure/ngb.mjs recover --format json
node infrastructure/ngb.mjs recover --apply
```

Recovery verifies original bytes and refuses to overwrite subsequent edits or
take over a live operation. Existing historical `.ngb/upgrades` records remain
available. Keep the recovery directory until the upgrade has been reviewed.

New apps carry the tool at `infrastructure/ngb.mjs`; it acquires the exact target
CLI when a registered newer transition requires it. Existing 3.3.0 applications
bootstrap through `npm exec --ignore-scripts --package=@ngbplatform/cli@3.4.0 -- ngb
upgrade --app . --to 3.4.0` after publication. Future releases must register
their transitions and document the required tool version. Read the migration guide
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
The deploy command streams migration output from a temporary container that it
removes afterwards. Retain that output and check the command's exit status;
`docker compose logs migrator` may show an older initial startup. Check API and
worker readiness at `/health` after deployment.
Do not use `down --volumes` on application data. Repeating the
Migrator must not duplicate data or reactivate/rename an existing Administrator role.

This Compose file is a loopback-only development environment, including Keycloak
`start-dev` and HTTP metadata. For production, use a managed deployment with TLS,
production Keycloak, secrets management, least-privilege database accounts,
backup/recovery and explicit ingress. Keep JWT issuer validation enabled; the optional
`KeycloakSettings:MetadataAddress` changes only the private discovery address.
Production metadata requires HTTPS unless an explicitly trusted internal transport
is configured. Apply the same deployment controls to the jobs dashboard.
