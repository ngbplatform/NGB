# NGB.Platform.BackgroundJobs.PostgreSql

PostgreSQL adapter for NGB Platform background jobs. It contains the Hangfire PostgreSQL storage factory and optimized PostgreSQL recurring-job state reader, keeping provider-specific dependencies out of the generic BackgroundJobs and PostgreSql packages.

## Install

```bash
dotnet add package NGB.Platform.BackgroundJobs.PostgreSql
```

Register the adapter in the application composition root after the core PostgreSQL services:

```csharp
services
    .AddNgbPostgres(connectionString)
    .AddNgbPostgresBackgroundJobsAdapter();
```
