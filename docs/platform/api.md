---
title: "API"
description: "How NGB.Api and vertical API hosts expose the platform through ASP.NET Core."
---

# API

<div class="doc-status-row">
  <span class="doc-badge doc-badge-verified">Verified host composition</span>
  <span class="doc-badge doc-badge-inferred">Surface interpretation</span>
</div>

> File-level companion page: [API source map](/platform/api-source-map)

## Verified anchors

- `NGB.Api/NGB.Api.csproj`
- `NGB.PropertyManagement.Api/Program.cs`

## What is directly visible

### Shared API module
`NGB.Api.csproj` shows the shared API layer depends on:

- ASP.NET Core
- contracts/application abstractions/runtime
- JWT authentication, OpenAPI, Swagger, and HTTP resilience packages
- `NGB.Hosting.AspNetCore` for reusable web-host integration

### Vertical API host composition
`NGB.PropertyManagement.Api/Program.cs` shows how a vertical host composes the platform:

- shared infrastructure and logging
- health checks
- runtime registration and explicit `AddNgbRuntimeStartupValidation()`
- PostgreSQL provider registration
- vertical module registration
- controller mapping
- auth/cors/exception pipeline

Authentication, branding, CORS, health responses, and global error handling are owned by
`NGB.Hosting.AspNetCore`. PostgreSQL exception mapping and health checks are owned by
`NGB.PostgreSql.AspNetCore` and must be registered explicitly by the host.

## Practical interpretation

This strongly suggests the intended model is:

- **shared API package** = reusable API building blocks
- **vertical API host** = executable composition root

That is the right split for a multi-vertical platform.

## Why this matters for docs and extension work

When documenting or extending API behavior, keep three concerns separate:

1. **Shared API capabilities**
2. **Vertical host composition**
3. **Runtime business orchestration**

Do not describe host startup code as if it were runtime logic, and do not describe runtime orchestration as if it were an ASP.NET concern.

## Continue with

- [Host Composition and DI Map](/architecture/host-composition-and-di-map)
- [Runtime](/platform/runtime)
- [Host composition](/start-here/host-composition)
