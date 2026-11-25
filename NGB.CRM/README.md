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

## Build CRM

From the repository root, restore the backend dependencies from NuGet.org and build the API.

```bash
dotnet restore NGB.CRM.Api/NGB.CRM.Api.csproj --configfile NuGet.Registry.Config
dotnet build NGB.CRM.Api/NGB.CRM.Api.csproj -c Release --no-restore
```

## Docker Compose

Follow the [CRM startup instructions](../docs/start-here/run-locally.md#start-crm)
to build the backend and web images with NuGet.org and npm packages and start the demo.

## Migrations

`NGB.CRM.Migrator` contributes the `crm` migration pack and depends on the platform migration pack.

```bash
dotnet run --project NGB.CRM.Migrator -- --connection "<connection-string>"
```

## Attribution

This demo uses common CRM and sales pipeline concepts. It is not affiliated with Salesforce and does not use Salesforce APIs, proprietary layouts, or branding.
