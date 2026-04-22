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
- `NGB.Api` — reusable ASP.NET Core host layer.
- `NGB.BackgroundJobs` — reusable background jobs hosting support.
- `NGB.Watchdog` — reusable watchdog hosting support.
- `NGB.Migrator.Core` — reusable migrator CLI core.

## See also

- [Layering and Dependencies](/architecture/layering-and-dependencies)
- [Layering rules](/reference/layering-rules)
