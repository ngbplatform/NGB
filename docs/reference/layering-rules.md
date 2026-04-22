---
title: Layering rules
description: Practical layering rules for keeping NGB modular, provider-aware, and vertical-safe.
---

# Layering rules

## Core rules

1. `NGB.Runtime` orchestrates, but does not contain provider-specific SQL.
2. `NGB.PostgreSql` implements provider-specific persistence and reporting execution.
3. `NGB.Definitions` and `NGB.Metadata` describe, but do not host HTTP or provider logic.
4. vertical modules extend the platform without leaking vertical rules into reusable platform projects.
5. hosts compose modules; libraries should not behave like hidden application entry points.

## Practical do-not-cross boundaries

- do not put Dapper/Npgsql code into `NGB.Runtime`;
- do not put vertical literals into reusable platform code where a generic contract belongs;
- do not put ASP.NET concerns into definitions/metadata projects;
- do not make `NGB.PostgreSql` the owner of workflow semantics.

## Why this matters

These rules are what let NGB remain a modular monolith instead of collapsing into either a generic framework or a pile of vertical-specific code.

## Related pages

- [Layering and Dependencies](/architecture/layering-and-dependencies)
- [Platform projects](/reference/platform-projects)
- [API runtime PostgreSQL integration](/platform/api-runtime-postgres-integration)
