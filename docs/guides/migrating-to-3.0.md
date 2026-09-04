---
title: Migrating from NGB Platform 2.0.0 to 3.0.0
description: Package, host-composition, and deployment changes required for the NGB Platform 3.0 release.
---

# Migrating from NGB Platform 2.0.0 to 3.0.0

NGB Platform 3.0 makes host and provider boundaries explicit. The business contracts introduced in
2.0 remain, while ASP.NET Core lifecycle concerns and PostgreSQL-specific adapters move to packages
owned by those layers.

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

## Rollback

Rollback must restore the complete 2.x application set, including web bundles and host packages.
Do not keep a 3.0 host composition with 2.x platform assemblies, or a 3.0 frontend with a 2.x API.
Database rollback remains migration-specific: inspect the applied migration set and data changes
before reverting binaries.
