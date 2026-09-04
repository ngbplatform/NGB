---
title: Platform projects
description: Project-by-project map of the reusable NGB platform.
---

# Platform projects

This page is the project catalog for the reusable platform.

## Foundation

- `NGB.Core` — stable domain primitives and shared low-level platform concepts.
- `NGB.Tools` — normalization, exception, helper, and utility layer.
- `NGB.Metadata` — metadata model for documents, fields, tables, lookups, and UI-facing descriptors.
- `NGB.Definitions` — immutable registered business definitions such as document types, relationship types, and derivations.

## Contracts and application boundary

- `NGB.Contracts` — DTOs and transport contracts.
- `NGB.Application.Abstractions` — application-facing service interfaces.
- `NGB.Persistence` — provider-agnostic persistence contracts.

## Execution core

- `NGB.Runtime` — orchestration center for documents, reporting, derivations, graph/effects, and validation.

## Business engines

- `NGB.Accounting` — accounting domain layer and posting-related primitives.
- `NGB.OperationalRegisters` — operational register domain layer.
- `NGB.ReferenceRegisters` — reference register domain layer.

## Providers and hosts

- `NGB.PostgreSql` — PostgreSQL provider implementations and migration assets.
- `NGB.PostgreSql.AspNetCore` — PostgreSQL-specific ASP.NET Core exception mapping and health checks.
- `NGB.Hosting.AspNetCore` — provider-neutral ASP.NET Core authentication, branding, health, CORS, and error handling.
- `NGB.Runtime.Hosting` — explicit generic-host lifecycle adapters for Runtime, including fail-fast startup validation.
- `NGB.Api` — reusable API controllers, endpoints, and application-facing HTTP composition.
- `NGB.BackgroundJobs` — provider-neutral background-jobs scheduling and hosting support.
- `NGB.BackgroundJobs.PostgreSql` — PostgreSQL-specific Hangfire storage adapter; keeps scheduler dependencies out of the generic PostgreSQL provider.
- `NGB.Watchdog` — reusable watchdog hosting support.
- `NGB.Migrator.Core` — reusable migrator CLI core.

## See also

- [Layering and Dependencies](/architecture/layering-and-dependencies)
- [Layering rules](/reference/layering-rules)
