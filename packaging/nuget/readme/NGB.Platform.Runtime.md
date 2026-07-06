# NGB.Platform.Runtime

Execution core for NGB Platform.

## Install

```bash
dotnet add package NGB.Platform.Runtime
```

## What It Contains

- Catalog and document orchestration.
- Lifecycle actions, posting, unposting, derivations, approval, numbering, validation, security, and audit integration.
- Reporting definitions, planning, rendering, export, and canonical accounting reports.
- Dependency injection extensions such as `AddNgbRuntime()`.

## Notes

Runtime coordinates platform behavior against persistence abstractions. Provider-specific SQL, Dapper, Npgsql, and migration behavior live outside this package.

