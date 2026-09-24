---
title: Migrating from NGB Platform 2.0.0 to 3.0.0
description: Package, host-composition, and deployment changes required for the NGB Platform 3.0 release.
---

# Migrating from NGB Platform 2.0.0 to 3.0.0

NGB Platform 3.0 makes host and provider boundaries explicit and changes public service, DTO, and
reporting contracts. Document Actions and Work Center remain the business model introduced in 2.0,
but custom hosts, service implementations, and API clients require the changes below.

## Compatibility rule

Update every `NGB.Platform.*` dependency, `@ngbplatform/ui`, and each web application to `3.0.0` in
one release train. A mixed 2.x/3.x platform graph is unsupported. Keep exact versions for the shared
UI package so the committed lockfiles and deployed web bundles use the same contract.

## Update package references

Keep the platform packages already used by the host and add the adapters required by its role.
Typical API hosts need:

```xml
<PackageReference Include="NGB.Platform.Api" Version="3.0.0" />
<PackageReference Include="NGB.Platform.Hosting.AspNetCore" Version="3.0.0" />
<PackageReference Include="NGB.Platform.PostgreSql" Version="3.0.0" />
<PackageReference Include="NGB.Platform.PostgreSql.AspNetCore" Version="3.0.0" />
<PackageReference Include="NGB.Platform.Runtime" Version="3.0.0" />
<PackageReference Include="NGB.Platform.Runtime.Hosting" Version="3.0.0" />
```

Background-job hosts that use PostgreSQL-backed Hangfire also need:

```xml
<PackageReference Include="NGB.Platform.BackgroundJobs" Version="3.0.0" />
<PackageReference Include="NGB.Platform.BackgroundJobs.PostgreSql" Version="3.0.0" />
```

Do not add Hangfire, Dapper, Npgsql, or health-response implementation packages directly to an
application merely to recover transitive APIs. Reference the NGB adapter that owns the capability.

## Update namespaces and recompile consumers

Public hosting types moved to new assemblies and namespaces. Updating package versions alone does
not update imports or compiled references:

| 2.0 namespace or type | 3.0 replacement |
| --- | --- |
| `NGB.Api.Models.KeycloakSettings` | `NGB.Hosting.AspNetCore.Identity.KeycloakSettings` |
| `NGB.Api.Sso` authentication extensions and admin-console options | `NGB.Hosting.AspNetCore.Identity` |
| `NGB.Api.Branding` | `NGB.Hosting.AspNetCore.Branding` |
| `NGB.Api.GlobalErrorHandling` | `NGB.Hosting.AspNetCore.ErrorHandling` |
| `NGB.Api.BaseHttpExternalHealthCheck` | `NGB.Hosting.AspNetCore.Health.BaseHttpExternalHealthCheck` |
| `NGB.BackgroundJobs.Infrastructure.HangfireTools.EnsureDatabaseExistsAsync(...)` | `NGB.PostgreSql.Bootstrap.PostgresDatabaseProvisioner.EnsureDatabaseExistsAsync(...)` |

Keycloak administration clients still live in `NGB.Api.Sso`; move only the authentication/hosting
imports. Rebuild all consumers against 3.0 instead of replacing assemblies in an existing 2.0 build.

## Compose API hosts explicitly

Register runtime startup validation, PostgreSQL exception translation, and the database health
check at the application composition root:

```csharp
using NGB.PostgreSql.AspNetCore.DependencyInjection;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Hosting;

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("DefaultConnection is required.");

builder.Services.AddNgbPostgresExceptionMapping();
builder.Services.AddHealthChecks()
    .AddNgbPostgresHealthCheck(connectionString);

builder.Services
    .AddNgbRuntime()
    .AddNgbRuntimeStartupValidation()
    .AddNgbPostgres(connectionString);
```

`NGB.Platform.Hosting.AspNetCore` owns provider-neutral authentication, branding, CORS, health
response formatting, and canonical HTTP error handling. `NGB.Platform.PostgreSql.AspNetCore` adds
only PostgreSQL-specific HTTP and health adapters.

## Compose PostgreSQL background-job hosts explicitly

Pass the provider-owned Hangfire storage factory to the provider-neutral host and register the
PostgreSQL job adapter:

```csharp
using NGB.BackgroundJobs.Hosting;
using NGB.BackgroundJobs.PostgreSql;
using NGB.BackgroundJobs.PostgreSql.DependencyInjection;
using NGB.PostgreSql.Bootstrap;

var bootstrap = builder.AddNgbBackgroundJobs(PostgresHangfireJobStorageFactory.Create);

await bootstrap.EnsureInfrastructureAsync(new PostgresDatabaseProvisioner());

builder.Services.AddNgbPostgresBackgroundJobsAdapter();
```

This keeps SQL and concrete Hangfire PostgreSQL dependencies out of
`NGB.Platform.BackgroundJobs` while leaving the final provider choice in the application host.

The old parameterless `AddNgbBackgroundJobs()` and `EnsureInfrastructureAsync()` calls no longer
compile. Low-level `AddPlatformBackgroundJobsHangfire` now takes a `JobStorage` followed by the
options callback. `PlatformHangfireOptions.PrepareSchemaIfNecessary` was removed; pass that choice
to the storage factory instead. Set the same storage namespace on the factory and options.

Background-job web hosts must also register PostgreSQL exception mapping and health explicitly,
using `bootstrap.ApplicationConnectionString` and `bootstrap.Options.PostgresHealthCheckName`.
See `NGB.CRM.BackgroundJobs/Program.cs` for the complete composition.

## Update custom services and repositories

Existing implementations and decorators must implement newly required interface members. Examples:

| Interface | Required change |
| --- | --- |
| `ICatalogService` | Implement `GetHeadItemsByIdsAsync(...)`. |
| `IGeneralJournalEntryUiService` | Implement `GetCursorPageAsync(...)`. |
| `IWorkCenterTaskService` | Implement `CompleteByDeduplicationKeysAsync(...)`. |
| `IChartOfAccountsRepository` | Implement `GetAdminPageAsync(...)`. |
| `IPlatformUserRepository` | Replace `GetAllAsync(...)` calls with `GetPageAsync(...)`; implement `GetPageAsync(...)` and `GetByEmailsAsync(...)`. |

Recompile custom persistence providers as well as application services: the provider contracts now
include additional batch and page operations. The in-repository PostgreSQL implementations show
the expected behavior. A newly added interface member with a default implementation, such as
`IReportSpecializedPlanExecutor.PrepareExecution(...)`, does not itself require an implementation.

## Update DTO consumers

`PageRequestDto` adds `Cursor` and `IncludeTotal`; `PageResponseDto<T>` adds `HasMore` and
`NextCursor`. These are positional records: optional constructor arguments preserve many source
calls, but the old constructor and four-value `Deconstruct` signatures are no longer present.
Recompile callers and replace old tuple deconstruction with property access or the new arity.
The same consideration applies to extended reporting records such as `ReportExecutionRequestDto`,
`ReportSheetRowDto`, and `ReportPlanPredicate`.

`AccountCardReportPage.TotalDebit`, `TotalCredit`, and `ClosingBalance` are now `decimal?`.
Handle unavailable totals explicitly; `null` does not mean a zero balance.

## Update reporting hosts and HTTP clients

`ReportControllerBase` now requires `IReportDownloadService` in its constructor instead of
`IReportExportService`. Update derived controllers and their dependency injection. Full XLSX
downloads use a disposable prepared download and stream the response; the bounded
`IReportEngine.ExecuteExportSheetAsync(...)` helper is not the complete-download path.

For `POST /api/reports/{reportCode}/execute`:

- send `offset: 0` and leave `disablePaging` false; nonzero offsets and `disablePaging: true`
  now produce HTTP 400;
- use the returned opaque `nextCursor` for continuation and stop when `hasMore` is false;
- allow `total` to be null instead of computing page counts from it;
- use a row's `childrenPath` as `groupPath` to load the next level of a composable report;
- use `/api/reports/{reportCode}/export/xlsx` for a complete download;
- handle HTTP 429 and `Retry-After` when the instance's report request budget is exhausted.

Each page reads live data in its own consistent read session. A cursor is not a stored result set
and must not be reused after changing the report's filters, layout, parameters, or branch.
See [Report Browsing and Direct Downloads](../architecture/report-execution-results.md) for cursor
configuration, admission limits, streaming, and proxy settings.

## Respect bounded requests

Catalog/document offset paging is capped at 10,000, with a maximum page size of 500. Large clients
should follow continuation cursors rather than increasing offsets indefinitely. Record writes now
enforce a maximum of 50 tabular parts, 5,000 rows, and 100,000 cells per payload; split larger imports
into bounded operations. These limits are defined in `PagingLimits` and `RecordPayloadLimits`.

## Update the frontend

Install and commit the exact 3.0 UI dependency and regenerated lockfile:

```bash
npm install --save-exact @ngbplatform/ui@3.0.0
```

Build every vertical web application against the same `@ngbplatform/ui` version. Do not deploy a
3.0 API with a 2.x web bundle.

## Deployment sequence

1. Build and verify all `NGB.Platform.*` 3.0 NuGet packages and `@ngbplatform/ui@3.0.0`.
2. Publish the complete NuGet set before restoring package-consuming verticals.
3. Publish the npm package and regenerate dedicated consumer lockfiles from the published tarball.
4. Deploy migrators, APIs, background-job hosts, watchdogs, and matching web applications as one
   coordinated release.
5. Verify migrations, startup definition validation, PostgreSQL health, canonical error responses,
   Hangfire storage, and representative document/report workflows before restoring traffic.

Apply the committed migration packs through the migrator; keep their existing history. This
release adds read-path indexes to the platform and vertical packs. It does not require rebuilding
databases or deleting the migrations that follow their baseline.

## Rollback

Rollback must restore the complete 2.x application set, including web bundles and host packages.
Do not keep a 3.0 host composition with 2.x platform assemblies, or a 3.0 frontend with a 2.x API.
Database rollback remains migration-specific: inspect the applied migration set and data changes
before reverting binaries.
