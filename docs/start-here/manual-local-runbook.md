---
title: Manual local runbook
description: Manual local developer runbook for bringing up NGB services without relying only on docker-compose.
---

# Manual local runbook

<div class="doc-badge-row">
  <span class="doc-badge doc-badge--verified">Verified</span>
  <span class="doc-badge doc-badge--template">Template</span>
</div>

This runbook is the manual companion to `docker-compose.pm.yml`. Use it when you want to run parts of the stack locally from source instead of letting Docker Compose do everything.

## Verified anchors

```text
README.md
docker-compose.pm.yml
.env.pm
NGB.PropertyManagement.Api/Program.cs
NGB.PropertyManagement.BackgroundJobs/Program.cs
NGB.PropertyManagement.Watchdog/Program.cs
NGB.PropertyManagement.Migrator/Program.cs
docker/pm/migrator/seed-and-migrate.sh
ui/package.json
```

## Recommended boot order

1. PostgreSQL
2. Keycloak
3. Migrator
4. API host
5. Background Jobs host
6. Watchdog host
7. Web client

That is the same logical order used by the compose environment.

## 1. Start PostgreSQL

You can either use the compose PostgreSQL service or run your own local PostgreSQL instance.

The application expects:

- one app database;
- one Keycloak database;
- proper app/database users;
- the application connection string to point to the app database.

## 2. Start Keycloak

The compose files and `.env.pm` show the local development convention:

- local hostname for Keycloak;
- one shared realm for the demo solution;
- separate clients for API, web, background jobs, watchdog, and tester.

If you are running Keycloak manually, mirror those values in your local realm and client setup.

## 3. Run the migrator

The verified `seed-and-migrate.sh` script shows the intended sequence:

```bash
# migrate schema
dotnet run --project NGB.PropertyManagement.Migrator --   --connection "Host=...;Port=...;Database=...;Username=...;Password=..."   --modules pm   --repair

# seed defaults
dotnet run --project NGB.PropertyManagement.Migrator --   seed-defaults   --connection "Host=...;Port=...;Database=...;Username=...;Password=..."

# optional demo seed
dotnet run --project NGB.PropertyManagement.Migrator --   seed-demo   --connection "Host=...;Port=...;Database=...;Username=...;Password=..."   --dataset demo   --skip-if-dataset-exists true
```

## 4. Run the API host

```bash
ASPNETCORE_ENVIRONMENT=Development ConnectionStrings__DefaultConnection='Host=...;Port=...;Database=...;Username=...;Password=...' dotnet run --project NGB.PropertyManagement.Api
```

## 5. Run the Background Jobs host

```bash
ASPNETCORE_ENVIRONMENT=Development ConnectionStrings__DefaultConnection='Host=...;Port=...;Database=...;Username=...;Password=...' dotnet run --project NGB.PropertyManagement.BackgroundJobs
```

## 6. Run the Watchdog host

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project NGB.PropertyManagement.Watchdog
```

## 7. Run the web workspace

From `ui/`:

```bash
npm install
npm run dev:pm-web
```

## Certificates and HTTPS

The compose environment mounts ASP.NET development certificates from the user profile. If you run the hosts manually with HTTPS, make sure your local development certificate exists and is trusted.

## Fast reset workflow

For a clean local reset:

1. drop and recreate the app database;
2. rerun migrator schema step;
3. rerun `seed-defaults`;
4. rerun `seed-demo` if needed;
5. restart API, background jobs, and watchdog.

## Related pages

- [Run Locally](/start-here/run-locally)
- [Host composition](/start-here/host-composition)
- [Migrator Deep Dive](/platform/migrator-deep-dive)
- [Configuration reference](/reference/configuration-reference)
