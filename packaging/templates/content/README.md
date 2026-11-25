# NgbApplication

An independent NGB application: API, one-shot Migrator, Background Jobs, Watchdog
and Vue. It uses official NuGet/npm packages. No NGB repository checkout is required.
The initial UI provides users, roles, permission editing, role audit history and
Work Center. Notes and Attachments are enabled by default in the local deployment.

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

Before implementation, list the authoritative brief and appendices in
`ai/brief-sources.json`, extract their individual rules into `ai/business-obligations.json` and run `node infrastructure/ngb.mjs ai capture --apply`.
Version the source list, briefs, obligations and resulting lock together. Keep a reviewed copy
of the fingerprint outside the generator when comparing independent implementations.
See `ai/vendor/references/server-acceptance.md` for the format and migration steps.

Maintain `ai/acceptance.json` as documents and roles are added. The E2E gate compares
it with the running application's document metadata and active roles, provisions
one isolated user per role, and requires current-run Playwright UI evidence for
every mandatory scenario. See `ai/vendor/references/browser-acceptance.md` and the
supplied `web/tests/e2e/acceptance.ts` helper. The starter already exposes the
standard document API; extend its definitions and reuse its controller instead of
registering another controller with identical routes.

Integration/E2E use disposable PostgreSQL and Keycloak resources. They do not use
the deployment `.env`. Docker with Compose and Playwright Chromium are required
for browser tests; the CLI installs the browser into its isolated execution home.
Certification additionally requires performance and business acceptance evidence;
a successful build or unit profile alone does not establish certification.

Installed reference examples live in `ai/vendor/examples/`. Copy an example to a
separate directory before restoring/building it, and follow its README. The
document example exercises standard lifecycle actions, workflow state and genuine
register effects. It does not introduce fictitious movements solely to allow Post.

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

The first identity has the trusted Keycloak role `ngb-admin`, so it can administer
the application before a platform user exists. On **Users**, create/link the same
email and assign the **Administrator** role. This enables personal Work Center
features as well. Active users assigned the registered `application.administrator`
role receive all registered capabilities, including new ones, without manual grants.
An inactive platform user stays blocked, even with `ngb-admin`. Renaming an ordinary
role to Administrator grants no additional access. Deployment flags remain independent.

Create an ordinary role, assign permissions, save, reopen and edit it, then inspect
its audit tab. These are real platform operations backed by PostgreSQL and Keycloak.

Compose starts the full local stack, including the web application:

| Service | Local address |
| --- | --- |
| Web | http://localhost:5182 |
| API health | http://localhost:5181/health |
| Background Jobs | http://localhost:5184/hangfire |
| Watchdog Health UI | http://localhost:5185/health-ui |
| Seq | http://localhost:5183 |
| pgAdmin | http://localhost:5186 |
| Keycloak | http://localhost:5180/admin/ |
| PostgreSQL | localhost:5187 |
| MinIO S3 / console | http://localhost:5188 / http://localhost:5189 |

Sign in to Jobs and Health with the bootstrap application administrator. Watchdog
collects API and worker health and checks the web client. Run the registered
`platform.schema.validate` job and inspect its outcome. API, worker and Watchdog
send logs to Seq as well as stdout. Seq and pgAdmin use `BOOTSTRAP_EMAIL` with
their separate `SEQ_PASSWORD` and `PGADMIN_PASSWORD`; Keycloak's master console
uses `KEYCLOAK_MASTER_PASSWORD`. MinIO uses `MINIO_ROOT_USER` and
`MINIO_ROOT_PASSWORD`. Read those private values locally from `.env`.
Seq asks you to replace its generated initial password on the first sign-in.
The local pgAdmin configuration accepts `.test` email addresses for development.
Set `PGADMIN_EMAIL` only when a separate pgAdmin administration identity is needed.

Compose starts API and worker only after the Migrator exits successfully, and
the API also waits for private MinIO bucket/account initialization. Watchdog starts
after the application hosts. A failed migration blocks them. The Migrator applies platform migration packs, then
idempotently creates the registered Administrator role. It never seeds business
records. PostgreSQL stores the application, Keycloak realm, and Hangfire state.

## Develop on the host

From the application directory, restore the backend packages from NuGet and the
frontend packages from npm, then build both projects.

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
Compose exposes PostgreSQL only on loopback port 5187. Host development uses this
endpoint; containers use `postgres:5432`.
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

## Default content capabilities

Attachments and Notes are enabled by default. The API registers the public MinIO
adapter at the same SDK version. Compose initializes a private bucket and a separate
application account limited to that bucket's upload/attachment objects. Root MinIO
credentials are used only by initialization and the administration console.
Browser upload/download URLs use port 5188; server I/O uses `minio:9000`. CORS permits
the two local web origins on port 5182. Notes need PostgreSQL and do not use MinIO.
The shared editors expose these actions for supported, saved parent objects; the
empty starter does not invent a business catalog/document just to display them.

To disable a feature explicitly, set `FEATURE_ATTACHMENTS=false` or
`FEATURE_NOTES=false` in private `.env` and redeploy. Parent-object permissions and
administrator behavior remain enforced independently of deployment flags. Never
make the bucket public or replace parent authorization with a separate ACL.

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

The currently registered transition is listed in the guide. The tool requires one
root solution, `NgbPlatformVersion` in `Directory.Build.props`, a mapped
`NuGet.Config`, and the `web` frontend. Other layouts require manual migration.
Preview writes nothing inside the application; verified target CLI acquisition
uses an external cache. Apply prepares matching SDK/UI, CLI/helper and Kit changes
in a private temporary copy and validates builds before committing one transaction.
Review application code, tests and configuration as part of the upgrade.
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
CLI when a registered newer transition requires it. To preview the upgrade from
3.3.0 to 3.4.0, run this command from the existing application's root.

```sh
npm exec --ignore-scripts --package=@ngbplatform/cli@3.4.0 -- ngb upgrade --app . --to 3.4.0
```

Review the plan, then run the command with `--apply`. The migration guide covers
application-specific configuration and tests. Installing a template never
rewrites an existing app.

Before an upgrade, take consistent backups of PostgreSQL, Keycloak and
object storage. Stop API and worker writers. Run the target Migrator against the
same state, then start the matching API, worker, Watchdog and frontend. On migration failure,
keep target hosts stopped, inspect the error and follow the migration guide's
retry/recovery steps. Do not downgrade a migrated database in place.

Use a protected backup destination. For a local rehearsal, create the ignored
project-root directory with `mkdir -p -m 700 backups` before writing dumps.
Restore the same backup set into separate services, obtain a new login, compare
a known business record and download a known attachment. Compare the downloaded
bytes with the original checksum. Record the release used as the migration
baseline and each store actually restored.

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
