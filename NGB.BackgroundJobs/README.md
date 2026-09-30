# NGB.BackgroundJobs

Infrastructure module for platform background job scheduling and execution.

**Scheduler:** Hangfire (only).

## Responsibilities

- Define the platform job catalog (fixed ids).
- Provide contracts for scheduling (`IJobScheduleProvider`) and job execution (`IPlatformBackgroundJob`).
- Register Hangfire with a host-supplied `JobStorage` and install recurring jobs on startup.
- Keep PostgreSQL storage creation in `NGB.Platform.BackgroundJobs.PostgreSql`.

## How to use (vertical app)

### 1) Provide schedules from appsettings.json

Add an appsettings section (recommended: `BackgroundJobs`).

```json
{
  "BackgroundJobs": {
    "Enabled": true,
    "DefaultTimeZoneId": "UTC",
    "NightlyCron": "0 2 * * *",
    "Jobs": {
      "accounting.operations.stuck_monitor": {
        "Cron": "*/5 * * * *",
        "Enabled": true,
        "TimeZoneId": "UTC"
      }
    }
  }
}
```

Then register the configuration schedule provider:

```csharp
services.AddPlatformBackgroundJobSchedulesFromConfiguration(configuration);
```

Notes:
- If a job has no explicit `Cron`, it will use `NightlyCron` **unless** it is in `NightlyExcludedJobIds`.
- Returning `null` schedule means "do not schedule".

### 2) Register Hangfire

```csharp
using NGB.BackgroundJobs.DependencyInjection;
using NGB.BackgroundJobs.PostgreSql;
using NGB.BackgroundJobs.PostgreSql.DependencyInjection;

var connectionString = configuration.GetConnectionString("Hangfire")
    ?? throw new InvalidOperationException("Hangfire connection string is required.");
var storage = PostgresHangfireJobStorageFactory.Create(
    connectionString, "hangfire", prepareSchemaIfNecessary: true);

services.AddPlatformBackgroundJobsHangfire(storage, o =>
{
    o.ConnectionString = connectionString;
    o.StorageNamespace = "hangfire";
    // Optional: WorkerCount, Queues, ServerName.
});
services.AddNgbPostgresBackgroundJobsAdapter();
```

If `IJobScheduleProvider` is not registered, all jobs remain unscheduled by default.

For a complete ASP.NET Core host, use
`builder.AddNgbBackgroundJobs(PostgresHangfireJobStorageFactory.Create)` and
`await bootstrap.EnsureInfrastructureAsync(new PostgresDatabaseProvisioner())`, then register
Runtime, startup validation, PostgreSQL, its background-jobs adapter, and the vertical modules.
PostgreSQL health checks and HTTP exception mapping are explicit registrations from
`NGB.Platform.PostgreSql.AspNetCore`. See
[the 3.0 migration guide](../docs/guides/migrating-to-3.0.md) and
[the CRM host](../NGB.CRM.BackgroundJobs/Program.cs).
