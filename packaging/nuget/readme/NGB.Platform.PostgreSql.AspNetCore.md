# NGB.Platform.PostgreSql.AspNetCore

ASP.NET Core integration adapters for the NGB Platform PostgreSQL provider.

## Install

```bash
dotnet add package NGB.Platform.PostgreSql.AspNetCore
```

## What It Contains

- A PostgreSQL health check that opens a real connection and executes a minimal probe.
- Canonical, sanitized HTTP mappings for PostgreSQL server and client exceptions.
- Explicit, idempotent dependency-injection registration at an ASP.NET Core composition root.

## Notes

This package is intentionally separate from `NGB.Platform.PostgreSql`. Non-web consumers can use the base provider without taking a dependency on ASP.NET Core, while web hosts opt into these adapters explicitly.
