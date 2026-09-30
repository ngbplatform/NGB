# NGB.CRM

NGB.CRM is an industry demo vertical for sales pipeline and customer relationship workflows on top of NGB Platform.

The CRM solution intentionally consumes NGB Platform through NuGet packages, including:

- `NGB.Platform.Contracts`
- `NGB.Platform.Definitions`
- `NGB.Platform.Metadata`
- `NGB.Platform.Runtime`
- `NGB.Platform.PostgreSql`
- `NGB.Platform.PostgreSql.AspNetCore`
- `NGB.Platform.Api`
- `NGB.Platform.Hosting.AspNetCore`
- `NGB.Platform.Runtime.Hosting`
- `NGB.Platform.BackgroundJobs`
- `NGB.Platform.BackgroundJobs.PostgreSql`
- `NGB.Platform.Watchdog`
- `NGB.Platform.Migrator.Core`

The CRM projects must not reference platform source projects directly. Internal `NGB.CRM.*` project references are allowed.

## Scope

CRM covers:

- Accounts, contacts, products, and opportunity stages.
- Lead intake, qualification, and conversion documents.
- Opportunity updates, quotes, and activity logs.
- Read-side projections for leads, opportunities, quotes, activities, and reporting.

CRM does not include general ledger, inventory, invoicing, payroll, procurement, or any external CRM API integration.

## Local Package Verification

Before building against unpublished platform code, package the complete platform into the local
feed. From the repository root on macOS/Linux or Git Bash:

```bash
bash packaging/nuget/pack-platform.sh
```

The script refreshes the feed and restores the solution. Then build CRM:

```bash
dotnet build NGB.CRM.Api/NGB.CRM.Api.csproj -c Release --no-restore
```

After the packages are published, the same `PackageReference` entries can restore from NuGet.org.

For native Windows, use the [PowerShell package preparation instructions](../docs/start-here/run-locally.md#prepare-local-platform-packages).

## Docker Compose

The local CRM web image requires `artifacts/npm/ngbplatform-ui-local.tgz` as well as the backend
NuGet packages. On macOS/Linux, install the UI tooling and create the tarball:

```bash
npm --prefix ui ci
npm --prefix ui run pack:platform-ui -- --local-candidate
docker compose -f docker-compose.crm.yml --env-file .env.crm up -d --build
```

On Windows, use the Linux-container packaging command in the linked instructions, then run the
same Compose command. Prepare the HTTPS certificate described in the root README first.
Repack after platform changes: these artifacts are ignored by Git and are not created by Compose.

## Migrations

`NGB.CRM.Migrator` contributes the `crm` migration pack and depends on the platform migration pack.

```bash
dotnet run --project NGB.CRM.Migrator -- --connection "<connection-string>"
```

## Attribution

This demo uses common CRM and sales pipeline concepts. It is not affiliated with Salesforce and does not use Salesforce APIs, proprietary layouts, or branding.
