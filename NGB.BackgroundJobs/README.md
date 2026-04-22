# NGB.BackgroundJobs

Infrastructure module for platform background job scheduling and execution.

**Scheduler:** Hangfire (only).

## Responsibilities

- Define the platform job catalog (fixed ids).
- Provide contracts for scheduling (`IJobScheduleProvider`) and job execution (`IPlatformBackgroundJob`).
- Register Hangfire (PostgreSQL storage) and install recurring jobs on startup.

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
services.AddPlatformBackgroundJobsHangfire(o =>
{
    o.ConnectionString = configuration.GetConnectionString("Hangfire")!;
    // optional: worker count, queues, etc
});
```

If `IJobScheduleProvider` is not registered, all jobs remain unscheduled by default.
