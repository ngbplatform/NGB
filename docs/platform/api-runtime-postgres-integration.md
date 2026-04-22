---
title: API runtime PostgreSQL integration
description: How the API host composes NGB.Api, NGB.Runtime, NGB.PostgreSql, and the vertical modules.
---

# API runtime PostgreSQL integration

## What the API host verifies directly

The verified Property Management API host shows the canonical platform composition:

- use the reusable `NGB.Api` host library;
- register runtime with `AddNgbRuntime()`;
- register PostgreSQL provider with `AddNgbPostgres(connectionString)`;
- add vertical modules;
- add controllers, auth, health checks, and external links;
- build and run the ASP.NET Core app.

## Why the split matters

### NGB.Api

Provides the reusable host/web surface for ASP.NET Core concerns.

### NGB.Runtime

Owns orchestration, document workflow, reporting flow, explainability surfaces, and business execution.

### NGB.PostgreSql

Provides the concrete provider implementation that satisfies runtime persistence/reporting contracts.

## Integration rule

The API host should compose the three layers. None of them should collapse into one giant project.

## Related pages

- [HTTP → Runtime → PostgreSQL Execution Map](/architecture/http-runtime-postgres-execution-map)
- [Runtime Execution Core Dense Source Map](/platform/runtime-execution-core-dense-source-map)
- [PostgreSQL Source Map](/platform/postgresql-source-map)
