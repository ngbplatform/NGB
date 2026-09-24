---
title: Run Locally
---

# Run Locally

This page gives two supported local-development patterns:

1. **full Docker Compose bootstrap** — fastest way to get a demo running end to end;
2. **hybrid manual development** — infrastructure in containers, .NET and UI projects started locally for debugging.

The examples below use **Property Management**. Trade and Agency Billing follow the same pattern
with their own compose and environment files. CRM additionally consumes local platform packages;
follow [Prepare local platform packages](#prepare-local-platform-packages) before starting it.

Use .NET 10 SDK, Docker Compose v2 with Linux containers, and Node.js 22.14+ for local UI work.
Bash examples run from the repository root unless stated otherwise; PowerShell alternatives are
provided for package preparation and certificate setup.

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

Compose reads the `HOME` environment variable, not PowerShell's separate `$HOME` variable.
Set it in the session that launches Compose:

```powershell
$env:HOME = $env:USERPROFILE.Replace('\', '/')
```

For IDE launches, configure `HOME` in the run configuration, or persist it and restart the IDE:

```powershell
[System.Environment]::SetEnvironmentVariable('HOME', $env:USERPROFILE.Replace('\', '/'), 'User')
```

Export the PFX file as described under [HTTPS certificates](#https-certificates) before startup.

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

## Prepare local platform packages

The full solution includes CRM, which consumes `NGB.Platform.*` through NuGet. PM, AB, and Trade
use project references. Before building CRM or the complete solution against unpublished/local
platform code, populate the local feed configured in `NuGet.config`.

### NuGet on macOS/Linux or Git Bash

```bash
bash packaging/nuget/pack-platform.sh
```

The script packages the complete set from `packaging/nuget/projects.txt`, refreshes
`artifacts/nuget`, removes replaced versions from `artifacts/nuget-cache`, and restores `NGB.sln`.

### NuGet in PowerShell

From the repository root, with .NET 10 SDK available:

```powershell
$packageVersion = dotnet msbuild .\NGB.Tools\NGB.Tools.csproj -nologo -getProperty:PackageVersion
if ($LASTEXITCODE -ne 0) { throw "Could not resolve the platform version" }
$packageVersion = $packageVersion.Trim()
New-Item -ItemType Directory -Force .\artifacts\nuget | Out-Null

Get-Content .\packaging\nuget\projects.txt | ForEach-Object {
    if ($_.Trim()) {
        dotnet pack $_ -c Release -o .\artifacts\nuget
        if ($LASTEXITCODE -ne 0) { throw "Failed to pack $_" }
    }
}

# Invalidate only this version of local platform packages after repacking.
Get-ChildItem .\artifacts\nuget-cache -Directory -Filter 'ngb.platform.*' | ForEach-Object {
    $cachedVersion = Join-Path $_.FullName $packageVersion
    if (Test-Path $cachedVersion) { Remove-Item $cachedVersion -Recurse -Force }
}

dotnet restore .\NGB.sln --configfile .\NuGet.config --force-evaluate
if ($LASTEXITCODE -ne 0) { throw "Solution restore failed" }
```

### CRM UI on macOS/Linux

The CRM Compose file requires `artifacts/npm/ngbplatform-ui-local.tgz`. Create it before building
the web image, and recreate it after changing `ui/ngb-ui-framework`:

```bash
npm --prefix ui ci
npm --prefix ui run pack:platform-ui -- --local-candidate
```

The flag allows unpublished local content while keeping the dedicated CRM lockfile unchanged.
For release validation, follow the strict packaging instructions in `packaging/PUBLISHING.md`.

### CRM UI in PowerShell

The current packaging script launches `npm.cmd` directly through `execFileSync`, which does not
work on native Windows. Run the packaging step in a temporary Linux container instead. This also
avoids requiring a host Node.js installation; dependencies live in a disposable Docker volume:

```powershell
docker run --rm `
  --mount "type=bind,source=$($PWD.Path),target=/workspace" `
  --mount "type=volume,target=/workspace/ui/node_modules" `
  -w /workspace/ui `
  node:22.17-alpine `
  sh -c "npm ci --workspaces=false --ignore-scripts && npm run pack:platform-ui -- --local-candidate"

if ($LASTEXITCODE -ne 0) { throw "UI package creation failed" }
```

### Start CRM

After both package preparations and certificate setup:

```bash
docker compose -f docker-compose.crm.yml --env-file .env.crm up -d --build
```

`Unable to find package NGB.Platform.*` indicates that the required NuGet version is absent from
the configured feeds. `ENOENT ... ngbplatform-ui-local.tgz` indicates that the UI packaging step
was skipped. Both artifacts are ignored by Git and must be recreated on a new machine.

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

To create, update, deactivate, and reactivate users from the NGB UI, also configure the Keycloak Admin client for the API:

```bash
export KeycloakAdminClientSettings__BaseUrl="http://pm-keycloak.localhost:7012"
export KeycloakAdminClientSettings__Realm="ngb-demo"
export KeycloakAdminClientSettings__ClientId="<admin-client-id>"
export KeycloakAdminClientSettings__ClientSecret="<admin-client-secret>"
```

Application roles and permissions are stored in NGB, not in the Keycloak token. See [Security and Permissions](../platform/security-and-permissions.md).

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
npm ci
npm run dev:pm-web
```

The UI workspace already exposes dedicated workspace scripts such as:

- `npm run dev:pm-web`
- `npm run dev:trade-web`
- `npm run dev:ab-web`
- `npm run dev:crm-web`

## Developer notes

### HTTPS certificates

Compose mounts `${HOME}/.aspnet/https` and uses `/https/servercert.pfx` by default. Trust and
export a development certificate, using the same password as `ASPNET_CERT_PASS` in `.env.*`.
Trusting the certificate alone does not create the mounted PFX file.

Bash:

```bash
dotnet dev-certs https --trust
mkdir -p "$HOME/.aspnet/https"
dotnet dev-certs https --export-path "$HOME/.aspnet/https/servercert.pfx" --password "<ASPNET_CERT_PASS>"
```

PowerShell:

```powershell
$env:HOME = $env:USERPROFILE.Replace('\', '/')
New-Item -ItemType Directory -Force "$env:HOME/.aspnet/https" | Out-Null
dotnet dev-certs https --trust
dotnet dev-certs https --export-path "$env:HOME/.aspnet/https/servercert.pfx" --password "<ASPNET_CERT_PASS>"
```

### Local hostname for Keycloak

Modern browsers generally resolve `*.localhost` correctly. If your environment does not, add a host entry manually:

```text
127.0.0.1 pm-keycloak.localhost
```

### Validation commands

After preparing the local NuGet feed above, use these commands from the repository root:

```bash
dotnet build NGB.sln
dotnet test NGB.sln
```

From the UI workspace:

```bash
cd ui
npm ci
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
