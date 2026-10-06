---
title: Configuration reference
description: Concrete configuration keys, environment variables, and precedence rules for NGB local and containerized environments.
---

# Configuration reference

<div class="doc-badge-row">
  <span class="doc-badge doc-badge--verified">Verified</span>
  <span class="doc-badge doc-badge--inferred">Operational guidance</span>
</div>

## Verified anchors

```text
.env.pm
docker-compose.pm.yml
NGB.PropertyManagement.Api/appsettings.Development.json
NGB.PropertyManagement.BackgroundJobs/appsettings.Development.json
NGB.PropertyManagement.Watchdog/appsettings.Development.json
ui/ngb-property-management-web/.env
NGB.Migrator.Core/PlatformMigratorCli.cs
NGB.Migrator.Core/README.md
```

## Scope of this page

This page documents the concrete configuration keys in the Property Management example. The same
patterns are used by Trade, Agency Billing, and CRM with vertical-specific prefixes and port values.

## Configuration shape

In the verified PM example, configuration is split across four surfaces:

1. `appsettings*.json` for host defaults and structured sections.
2. `docker-compose.pm.yml` for container-time overrides and secrets wiring.
3. `.env.pm` for local port, image-tag, bootstrap, and secret values consumed by Docker Compose.
4. UI `.env` files for Vite runtime configuration.

For the migrator CLI, command-line flags are primary and selected environment variables are supported as fallbacks.

## Environment file groups (`.env.pm`)

| Group | Example keys | What they control |
|---|---|---|
| Build and certificates | `BUILD_CONFIGURATION`, `ASPNET_CERT_PASS`, `ASPNET_CERT_PATH` | Container build mode and HTTPS certificate mounting |
| Host ports | `PM_API_HTTP_PORT`, `PM_API_HTTPS_PORT`, `PM_BACKGROUNDJOBS_HTTPS_PORT`, `PM_WATCHDOG_HTTPS_PORT`, `PM_WEB_HTTP_PORT` | Published local ports for PM hosts |
| Observability | `SEQ_IMAGE_TAG`, `SEQ_HTTP_HOST_PORT`, `SEQ_URL`, `SEQ_API_KEY` | Seq image/version and ingestion target |
| PostgreSQL | `POSTGRES_HOST_PORT`, `POSTGRES_ADMIN_USER`, `PM_DB_NAME`, `PM_DB_USER`, `PM_DB_PASSWORD` | Database server, admin, and app-database credentials |
| Demo bootstrap | `PM_DEMO_SEED_ENABLED`, `PM_DEMO_DATASET`, `PM_DEMO_SEED_FROM`, `PM_DEMO_SEED_TO` | Local demo seeding behavior for PM |
| Keycloak | `KEYCLOAK_PUBLIC_URL`, `KEYCLOAK_REALM`, `KEYCLOAK_PM_API_CLIENT_ID`, `KEYCLOAK_PM_WEB_CLIENT_ID` | Realm, client ids, admin bootstrap, and public URLs |
| Content features | `FEATURE_ATTACHMENTS`, `FEATURE_NOTES`, `ATTACHMENTS_UPLOAD_EXPIRATION_ENABLED` | API feature availability and database-only expiration of pending uploads |
| MinIO | `MINIO_IMAGE_REPOSITORY`, `MINIO_IMAGE_TAG`, `MINIO_IMAGE_DIGEST`, `MINIO_BUCKET`, `MINIO_INTERNAL_ENDPOINT`, `MINIO_PUBLIC_ENDPOINT`, `MINIO_CORS_ORIGINS` | Local storage image, bucket, service/browser endpoints and permitted browser origins |
| Supporting tools | `PGADMIN_HTTP_HOST_PORT`, `PGADMIN_DEFAULT_EMAIL` | Optional local support tools |

## API host

The PM API host uses the following configuration keys in the verified development and Docker Compose setup.

| Key | Source | Meaning |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | `appsettings.Development.json`, `docker-compose.pm.yml` | Primary PostgreSQL application connection string |
| `KeycloakSettings:Issuer` | `appsettings.Development.json`, `docker-compose.pm.yml` | JWT issuer / Keycloak realm URL |
| `KeycloakSettings:ClientIds[]` | `appsettings.Development.json` | Accepted audiences for bearer tokens |
| `KeycloakSettings:RequireHttpsMetadata` | `docker-compose.pm.yml` | Local-development toggle for Keycloak metadata retrieval |
| `ExternalLinksSettings:HealthUiUrl` | `appsettings.Development.json`, `docker-compose.pm.yml` | External menu link to Watchdog UI |
| `ExternalLinksSettings:BackgroundJobsUiUrl` | `appsettings.Development.json`, `docker-compose.pm.yml` | External menu link to Hangfire dashboard |
| `ASPNETCORE_ENVIRONMENT` | `docker-compose.pm.yml` | ASP.NET Core environment name |
| `ASPNETCORE_URLS` | `docker-compose.pm.yml` | HTTP/HTTPS host binding |
| `ASPNETCORE_HTTPS_PORTS` | `docker-compose.pm.yml` | Published HTTPS container port |
| `ASPNETCORE_Kestrel__Certificates__Default__Path` | `docker-compose.pm.yml` | Mounted certificate path |
| `ASPNETCORE_Kestrel__Certificates__Default__Password` | `docker-compose.pm.yml` | Mounted certificate password |
| `Serilog__WriteTo__1__Args__serverUrl` | `docker-compose.pm.yml` | Seq ingestion URL |

### PM API development defaults

| Setting | Example value |
|---|---|
| `ConnectionStrings:DefaultConnection` | substituted from `.env.pm` / Compose |
| `KeycloakSettings:Issuer` | `${KEYCLOAK_PUBLIC_URL}/realms/${KEYCLOAK_REALM}` |
| `KeycloakSettings:ClientIds[]` | `ngb-pm-api`, `ngb-pm-web-client`, `ngb-tester` |
| `ExternalLinksSettings:HealthUiUrl` | `https://localhost:7075/health-ui` |
| `ExternalLinksSettings:BackgroundJobsUiUrl` | `https://localhost:7074/hangfire` |

### Attachments, Notes and feature flags

The same keys are used by all four API hosts. Environment-variable equivalents use double underscores, for example `FeatureManagement__Attachments` and `Attachments__MinIO__Bucket`.

| API key | Compose `.env` key | Meaning |
|---|---|---|
| `FeatureManagement:Attachments` | `FEATURE_ATTACHMENTS` | Enable attachment operations; defaults to `false` |
| `FeatureManagement:Notes` | `FEATURE_NOTES` | Enable notes independently of storage; defaults to `false` |
| `Attachments:UploadExpirationEnabled` | `ATTACHMENTS_UPLOAD_EXPIRATION_ENABLED` | Keep pending upload expiration active when Attachments is disabled; defaults to `false` |
| `Attachments:MinIO:InternalEndpoint` | `MINIO_INTERNAL_ENDPOINT` | S3 endpoint reached by the API for verification and server-side copy |
| `Attachments:MinIO:PublicEndpoint` | `MINIO_PUBLIC_ENDPOINT` | Browser-reachable S3 endpoint used to sign upload/download URLs |
| `Attachments:MinIO:Bucket` | `MINIO_BUCKET` | Private attachment bucket for this vertical |
| `Attachments:MinIO:AccessKey` | `MINIO_APP_ACCESS_KEY` | Application storage account, separate from the MinIO root account |
| `Attachments:MinIO:SecretKey` | `MINIO_APP_SECRET_KEY` | Application storage secret |
| `Attachments:MinIO:AllowInsecureHttp` | `MINIO_ALLOW_INSECURE_HTTP` | Local-development HTTP opt-in; defaults to `false` |

The checked-in local `.env` files explicitly enable both features and upload expiration. MinIO credentials are injected through Compose; they are not stored in API `appsettings.Development.json`. An IDE launch needs equivalent environment variables or .NET User Secrets because ASP.NET Core does not read Compose `.env` files automatically.

Feature state is fixed at API startup. After changing `.env`, recreate the API container with `docker compose ... up -d`; restarting an existing container does not replace its environment. Reload the browser after updating all replicas. Administrator permissions do not override a disabled feature.

Upload expiration runs inside the API host, not Hangfire. It changes database metadata and writes AuditLog events; it never deletes MinIO objects. MinIO configuration is required only when the Attachments feature is enabled, although the provided full Compose stacks still start storage.

See [Feature Flags](/architecture/feature-flags) for rollout and registration, and [Attachments & Notes configuration](/architecture/attachments-and-notes#configuration) for all limits, lifetimes, storage requirements and production configuration.

### Reporting

`NGB.Api/DependencyInjection.cs` binds the report request budget and cursor protection settings:

| Key | Default | Meaning |
|---|---|---|
| `Reporting:Requests:ConcurrentPages` | `12` | Concurrent interactive report requests per API instance |
| `Reporting:Requests:ConcurrentDownloads` | `2` | Concurrent report downloads per API instance |
| `Reporting:Requests:QueuedDownloads` | `4` | Maximum pending downloads per instance; `0` disables waiting |
| `Reporting:Requests:DownloadQueueTimeoutSeconds` | `3` | Maximum FIFO queue wait before report preparation; must be positive |
| `Reporting:Requests:PageTimeoutSeconds` | `30` | Interactive request deadline |
| `Reporting:Requests:DownloadTimeoutSeconds` | `300` | Download execution deadline after admission |
| `Reporting:Cursor:SigningKey` | Ephemeral process key | Base64 of at least 32 random bytes; share across replicas and restarts to preserve cursor validity |

Environment-variable equivalents use double underscores, for example
`Reporting__Requests__ConcurrentDownloads` and `Reporting__Cursor__SigningKey`.
A full download queue or expired queue wait returns HTTP 429 with `Retry-After: 3`.
Queued downloads do not start report preparation or open report read sessions. Page admission
remains immediate. Align the combined queue wait and execution deadline with proxy and client
timeouts. See
[Report Browsing and Direct Downloads](/architecture/report-execution-results).

## Background Jobs host

The PM background-jobs host has both application infrastructure settings and scheduler settings.

| Key | Meaning |
|---|---|
| `ConnectionStrings:DefaultConnection` | Application database connection |
| `KeycloakSettings:Issuer` | Authentication issuer for the host |
| `KeycloakSettings:ClientIds[]` | Allowed background-jobs client ids |
| `BackgroundJobs:Enabled` | Master scheduler enable switch |
| `BackgroundJobs:DefaultTimeZoneId` | Default time zone for job evaluation |
| `BackgroundJobs:NightlyCron` | Shared nightly maintenance schedule |
| `BackgroundJobs:Jobs.<job-id>.Cron` | Per-job cron expression |
| `BackgroundJobs:Jobs.<job-id>.Enabled` | Per-job enable switch |
| `BackgroundJobs:Jobs.<job-id>.TimeZoneId` | Per-job time zone override |

### PM development job schedules

| Job id | Cron | Enabled | Time zone |
|---|---|---|---|
| `accounting.operations.stuck_monitor` | `*/5 * * * *` | `true` | `UTC` |
| `accounting.general_journal_entry.auto_reverse.post_due` | `*/15 * * * *` | `true` | `UTC` |
| `pm.rent_charge.generate_monthly` | `0 5 * * *` | `true` | `UTC` |

## Watchdog host

The PM Watchdog host is configured as a small health aggregation surface.

| Key | Meaning |
|---|---|
| `WebClient` | Browser-facing web URL used by Watchdog |
| `KeycloakSettings:Issuer` | Authentication issuer |
| `KeycloakSettings:ClientIds[]` | Allowed watchdog client ids |
| `HealthChecksUI:HealthChecks[].Name` | Display name of a monitored target |
| `HealthChecksUI:HealthChecks[].Uri` | Health endpoint URI for a monitored target |

### PM development targets

| Name | URI |
|---|---|
| `Watchdog` | `https://ngb.pm.watchdog/health` |
| `API` | `https://ngb.pm.api/health` |
| `Background Jobs` | `https://ngb.pm.backgroundjobs/health` |

## Web client (`Vite`)

The verified Property Management web app `.env` exposes the following runtime keys:

| Key | Meaning | Example |
|---|---|---|
| `VITE_API_BASE_URL` | API base URL for the SPA | `https://localhost:7071` |
| `VITE_KEYCLOAK_URL` | Keycloak server URL | `http://pm-keycloak.localhost:7012` |
| `VITE_KEYCLOAK_REALM` | Keycloak realm name | `ngb-demo` |
| `VITE_KEYCLOAK_CLIENT_ID` | Web client id | `ngb-pm-web-client` |
| `VITE_BACKGROUND_JOB_URL` | Background-jobs dashboard URL | `https://localhost:7074/hangfire` |
| `VITE_WATCHDOG_URL` | Watchdog UI URL | `https://localhost:7075/health-ui` |

The Compose-based PM web service also injects:

- `VITE_KEYCLOAK_ROLE_ADMIN`
- `VITE_KEYCLOAK_REDIRECT_URL`
- `VITE_KEYCLOAK_POST_LOGOUT_REDIRECT_URL`

## Migrator CLI

The shared migrator runner supports both command-line flags and environment-variable fallbacks.

| Input | Meaning |
|---|---|
| `--connection "<connStr>"` | Primary application connection string |
| `NGB_CONNECTION_STRING` | Environment-variable fallback for `--connection` |
| `--schema-lock-mode wait|try|skip` | Schema lock behavior |
| `NGB_SCHEMA_LOCK_MODE` | Environment-variable fallback for lock mode |
| `--schema-lock-wait-seconds <N>` | Explicit schema lock wait |
| `NGB_SCHEMA_LOCK_WAIT_SECONDS` | Environment-variable fallback for lock wait |
| `--application-name <name>` | Explicit migrator application name |
| `NGB_APPLICATION_NAME` | Environment-variable fallback for application name |
| `--k8s` | Enables Kubernetes-oriented defaults |
| `NGB_K8S_MODE=true` | Environment-variable fallback for Kubernetes mode |

## Practical rules

- Keep application settings in structured host sections such as `ConnectionStrings`, `KeycloakSettings`, `ExternalLinksSettings`, and `BackgroundJobs`.
- Keep local ports, image versions, and bootstrap credentials in vertical `.env` files consumed by Docker Compose.
- Prefer environment-variable injection for secrets and deployment overrides instead of editing checked-in `appsettings*.json`.
- Keep UI runtime settings under explicit `VITE_*` keys rather than duplicating host config sections in the SPA.

## Related pages

- [Feature Flags](/architecture/feature-flags)
- [Attachments & Notes](/architecture/attachments-and-notes)
- [Manual local runbook](/start-here/manual-local-runbook)
- [Security and SSO](/platform/security-and-sso)
- [Migrator CLI](/reference/migrator-cli)
