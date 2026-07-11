# NGB.Platform.BackgroundJobs

Hangfire-based scheduled execution surface for NGB Platform.

## Install

```bash
dotnet add package NGB.Platform.BackgroundJobs
```

## What It Contains

- Background job contracts, catalog, schedule provider, notifier, and job metrics.
- Hangfire integration and hosting extensions.
- Health reporting for recurring jobs.
- Built-in recurring platform jobs for schema validation, accounting health, register finalization, and maintenance flows.

## Notes

The package includes `hangfire-dashboard.css` as a content file copied to consuming host output.

