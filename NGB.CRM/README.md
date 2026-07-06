# NGB.CRM

NGB.CRM is an industry demo vertical for sales pipeline and customer relationship workflows on top of NGB Platform.

The module intentionally consumes NGB Platform through NuGet packages:

- `NGB.Platform.Contracts`
- `NGB.Platform.Definitions`
- `NGB.Platform.Metadata`
- `NGB.Platform.Runtime`
- `NGB.Platform.PostgreSql`
- `NGB.Platform.Api`
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

Before publishing `NGB.Platform.*` packages to NuGet.org, build them into the local feed:

```bash
dotnet pack <platform-project>.csproj -c Release -o artifacts/nuget
```

Then restore/build CRM with the repository `NuGet.config`:

```bash
dotnet restore NGB.CRM.Api/NGB.CRM.Api.csproj --configfile NuGet.config
dotnet build NGB.CRM.Api/NGB.CRM.Api.csproj -c Release --no-restore
```

After the packages are published, the same `PackageReference` entries can restore from NuGet.org.

## Migrations

`NGB.CRM.Migrator` contributes the `crm` migration pack and depends on the platform migration pack.

```bash
dotnet run --project NGB.CRM.Migrator -- --connection "<connection-string>"
```

## Attribution

This demo uses common CRM and sales pipeline concepts. It is not affiliated with Salesforce and does not use Salesforce APIs, proprietary layouts, or branding.
