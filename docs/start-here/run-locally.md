---
title: Run Locally
---

# Run Locally

This page gives two supported local-development patterns:

1. **full Docker Compose bootstrap** — fastest way to get a demo running end to end;
2. **hybrid manual development** — infrastructure in containers, .NET and UI projects started locally for debugging.

The examples below use **Property Management**, because its Docker assets and environment file are complete and explicit. Trade and Agency Billing follow the same pattern with their own compose files and environment files.

## Option 1: Full Docker Compose bootstrap

### What the PM compose environment starts

The Property Management compose environment starts the following services in order:

1. PostgreSQL
2. database bootstrap
3. Keycloak realm bootstrap
4. Keycloak
5. Seq
6. Migrator
7. API
8. Background Jobs
9. Watchdog
10. Web client
11. pgAdmin

### Environment file

The main environment file is:

```bash
.env.pm
```

It includes the local ports and demo credentials used by the stack. Important defaults include:

- Web: `http://localhost:5173`
- API HTTPS: `https://localhost:7071`
- Background Jobs HTTPS: `https://localhost:7074`
- Watchdog HTTPS: `https://localhost:7075`
- Keycloak public URL: `http://pm-keycloak.localhost:7012`
- Seq: `http://localhost:5342`
- pgAdmin: `http://localhost:4882`

Demo user:

- email: `alex.carter@demo.ngbplatform.com`
- password: `DemoAdmin!2026`

### ⚠️ Windows note

Before starting the application in Docker on Windows, ensure that the `$HOME` environment variable is set in the current PowerShell session.

Check the current value:

```powershell
echo "$HOME"
```

If it is not set correctly, define it manually:

```powershell
[System.Environment]::SetEnvironmentVariable('HOME', $env:USERPROFILE.Replace('\', '/'), 'User')
```

### Start the demo

From the repository root:

```bash
docker compose -f docker-compose.pm.yml --env-file .env.pm up --build
```

### Stop the demo

```bash
docker compose -f docker-compose.pm.yml --env-file .env.pm down
```

### Open the running services

- Web: `http://localhost:5173`
- API health: `https://localhost:7071/health`
- Background Jobs dashboard: `https://localhost:7074/hangfire`
- Watchdog UI: `https://localhost:7075/health-ui`
- Keycloak: `http://pm-keycloak.localhost:7012`
- Seq: `http://localhost:5342`
- pgAdmin: `http://localhost:4882`

### What the compose migrator does

The PM compose stack runs the migrator container with this effective sequence:

1. migrate PM packs with `--modules pm --repair`;
2. run `seed-defaults`;
3. optionally run `seed-demo`.

That means a fresh local environment comes up already migrated and seeded.

## Option 2: Hybrid manual development

Use this mode when you want infrastructure bootstrapped quickly but want to debug the .NET or Vue apps locally.

### Step 1: Start infrastructure only

Start PostgreSQL, bootstrap, Keycloak, Seq, and optional pgAdmin:

```bash
docker compose -f docker-compose.pm.yml --env-file .env.pm up   ngb.pm.postgres   ngb.pm.postgres.bootstrap   ngb.pm.keycloak.init   ngb.pm.keycloak   ngb.pm.seq   ngb.pm.pgadmin   -d
```

### Step 2: Run the migrator locally

Create a connection string that matches `.env.pm`:

```bash
export NGB_PM_CONNECTION="Host=localhost;Port=5433;Database=ngb_pm;Username=ngb_pm_app;Password=Password(55)60-stronG-pm"
```

Run migrations:

```bash
dotnet run --project NGB.PropertyManagement.Migrator --   --connection "$NGB_PM_CONNECTION"   --modules pm   --repair
```

Seed defaults:

```bash
dotnet run --project NGB.PropertyManagement.Migrator --   seed-defaults   --connection "$NGB_PM_CONNECTION"
```

Seed demo data if needed:

```bash
dotnet run --project NGB.PropertyManagement.Migrator --   seed-demo   --connection "$NGB_PM_CONNECTION"   --dataset demo   --seed 20260412   --from 2024-01-01   --skip-if-dataset-exists true
```

### Step 3: Run backend hosts locally

You can run the PM API, Background Jobs, and Watchdog projects from your IDE or from the CLI. The exact local settings can be taken from `.env.pm`.

Typical environment variables you will need:

```bash
export ConnectionStrings__DefaultConnection="$NGB_PM_CONNECTION"
export KeycloakSettings__Issuer="http://pm-keycloak.localhost:7012/realms/ngb-demo"
export KeycloakSettings__RequireHttpsMetadata="false"
export Serilog__WriteTo__1__Args__serverUrl="http://localhost:5342"
```

Then run the hosts you need:

```bash
dotnet run --project NGB.PropertyManagement.Api
dotnet run --project NGB.PropertyManagement.BackgroundJobs
dotnet run --project NGB.PropertyManagement.Watchdog
```

### Step 4: Run the web client locally

From the UI workspace root:

```bash
cd ui
npm install
npm run dev:pm-web
```

The UI workspace already exposes dedicated workspace scripts such as:

- `npm run dev:pm-web`
- `npm run dev:trade-web`
- `npm run dev:ab-web`

## Developer notes

### HTTPS certificates

The Docker Compose setup mounts ASP.NET certificates from `${HOME}/.aspnet/https`. On a new machine, generate development certificates first if needed:

```bash
dotnet dev-certs https --trust
```

### Local hostname for Keycloak

Modern browsers generally resolve `*.localhost` correctly. If your environment does not, add a host entry manually:

```text
127.0.0.1 pm-keycloak.localhost
```

### Validation commands

Useful validation commands from the repository root:

```bash
dotnet build NGB.sln
dotnet test NGB.sln
```

From the UI workspace:

```bash
cd ui
npm install
npm run test:all
```

## When to use which mode

Use **full Docker Compose** when you want a quick demo environment.

Use **hybrid manual development** when you are actively coding and want:

- debugger-friendly .NET hosts;
- quick restarts;
- local UI hot reload;
- containerized infrastructure with minimal setup.

That hybrid mode is usually the best day-to-day developer workflow.
