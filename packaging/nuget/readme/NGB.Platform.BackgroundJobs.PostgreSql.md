# NGB.Platform.BackgroundJobs.PostgreSql

PostgreSQL adapter for NGB Platform background jobs. It contains the Hangfire PostgreSQL storage factory and optimized PostgreSQL recurring-job state reader, keeping provider-specific dependencies out of the generic BackgroundJobs and PostgreSql packages.

## Install

```bash
dotnet add package NGB.Platform.BackgroundJobs.PostgreSql
```

Pass the storage factory to the background host and register the inspection adapter after the
core PostgreSQL services:

```csharp
using NGB.BackgroundJobs.Hosting;
using NGB.BackgroundJobs.PostgreSql;
using NGB.BackgroundJobs.PostgreSql.DependencyInjection;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.DependencyInjection;

var bootstrap = builder.AddNgbBackgroundJobs(PostgresHangfireJobStorageFactory.Create);
await bootstrap.EnsureInfrastructureAsync(new PostgresDatabaseProvisioner());

builder.Services
    .AddNgbPostgres(bootstrap.ApplicationConnectionString)
    .AddNgbPostgresBackgroundJobsAdapter();
```

The host also composes Runtime, startup validation, its vertical modules, and PostgreSQL HTTP/health
integration. The storage factory alone does not register those application services.
