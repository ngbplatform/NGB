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

This page documents the concrete configuration keys that are visible in the published Property Management example. The same patterns are repeated across the Trade and Agency Billing verticals with vertical-specific prefixes and port values.

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

- [Manual local runbook](/start-here/manual-local-runbook)
- [Security and SSO](/platform/security-and-sso)
- [Migrator CLI](/reference/migrator-cli)
