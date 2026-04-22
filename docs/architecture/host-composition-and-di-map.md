---
title: "Host Composition and DI Map"
---

# Host Composition and DI Map

This page documents how a vertical API host composes the reusable NGB platform.

## Verified source anchor

```text
NGB.PropertyManagement.Api/Program.cs
```

## What this file proves

The vertical API host is intentionally a **composition root**, not the place where core business logic lives.

The validated responsibilities in `Program.cs` are:

- create the ASP.NET Core application builder;
- add Serilog;
- add health checks;
- resolve the PostgreSQL connection string;
- register platform runtime;
- register PostgreSQL provider;
- register vertical platform module layers;
- register controllers, swagger, global error handling, external links, auth;
- build middleware pipeline;
- map controllers.

## Composition order

<script setup>
const flowchart = String.raw`flowchart TB
    A[WebApplication builder] --> B[Health checks]
    B --> C[Infrastructure]
    C --> D[AddNgbRuntime]
    D --> E[AddNgbPostgres]
    E --> F[Add vertical module]
    F --> G[Controllers API]
    G --> H[Error handling]
    H --> I[Authentication]
    I --> J[Authorization]
    J --> K[MapControllers]`
</script>

<MermaidDiagram :chart="flowchart" />

## Why this matters

This composition style keeps the host thin and stable:

- business orchestration lives in runtime;
- persistence implementation lives in PostgreSQL provider;
- vertical specifics are attached through module registrations;
- the host remains mainly a boundary adapter.

## Directly visible dependencies

`Program.cs` confirms imports from:

- `NGB.Api`
- `NGB.Api.GlobalErrorHandling`
- `NGB.Api.Reporting`
- `NGB.Api.Sso`
- `NGB.PostgreSql.DependencyInjection`
- `NGB.Runtime.DependencyInjection`
- vertical dependency injection namespaces

Even where the exact extension file path was not asserted in the current verified anchor set, the host composition pattern is explicit from the program file.

## Practical reading model

When you read a vertical API host in NGB, think in this order:

1. **Host bootstrap**
2. **Platform registration**
3. **Provider registration**
4. **Vertical registration**
5. **Cross-cutting web concerns**
6. **HTTP pipeline**

That is the stable mental model for understanding how new verticals should be assembled.

## Related verified files

```text
NGB.Api/NGB.Api.csproj
NGB.Runtime/NGB.Runtime.csproj
NGB.PostgreSql/NGB.PostgreSql.csproj
```

These project files confirm the expected dependency directions around the host.

## Continue with

- [HTTP → Runtime → PostgreSQL Execution Map](/architecture/http-runtime-postgres-execution-map)
- [API Source Map](/platform/api-source-map)
