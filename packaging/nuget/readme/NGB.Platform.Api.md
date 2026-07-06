# NGB.Platform.Api

Reusable ASP.NET Core API building blocks for NGB Platform hosts.

## Install

```bash
dotnet add package NGB.Platform.Api
```

## What It Contains

- Base controllers for catalogs, documents, reports, audit, administration, and security.
- Global error handling and ProblemDetails mapping.
- SSO/Keycloak integration helpers.
- Health checks, CORS, Swagger, branding, and current-user infrastructure.

## Notes

This package is for reusable API host infrastructure. Deployable vertical API hosts remain application composition roots.

