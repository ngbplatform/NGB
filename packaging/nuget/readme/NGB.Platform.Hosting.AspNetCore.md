# NGB.Platform.Hosting.AspNetCore

Provider-neutral ASP.NET Core hosting infrastructure for NGB Platform hosts.

## Install

```bash
dotnet add package NGB.Platform.Hosting.AspNetCore
```

## What It Contains

- Canonical ProblemDetails error handling and extensible exception mapping.
- SSO/Keycloak authentication, cookie ticket storage, and identity settings.
- Health-check HTTP clients and reusable external-service health checks.
- CORS, Serilog, branding assets, and standalone host themes.

## Notes

This package intentionally has no dependency on NGB Runtime, Persistence, PostgreSQL, API controllers, Background Jobs, or Watchdog. Provider integrations remain in dedicated web-adapter packages such as `NGB.Platform.PostgreSql.AspNetCore`, and application hosts stay composition roots.
