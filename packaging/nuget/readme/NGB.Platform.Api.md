# NGB.Platform.Api

Reusable ASP.NET Core API building blocks for NGB Platform hosts.

## Install

```bash
dotnet add package NGB.Platform.Api
```

## What It Contains

- Base controllers for catalogs, documents, reports, audit, administration, and security.
- API controllers, Swagger, current-user infrastructure, and Keycloak administration clients.
- Composition helpers that combine API-specific services with provider-neutral hosting services.

## Notes

This package is for reusable API host infrastructure. Deployable vertical API hosts remain application composition roots.

Provider-neutral ASP.NET Core hosting, authentication, branding, health, CORS, and error handling live in `NGB.Platform.Hosting.AspNetCore` so non-API hosts do not need to reference this package. PostgreSQL-specific web integration lives separately in `NGB.Platform.PostgreSql.AspNetCore`.
