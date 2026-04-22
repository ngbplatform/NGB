---
title: API source map
---

# API source map

This page explains the role of `NGB.Api` and how to read it through a real host composition root.

## Confirmed source anchors

```text
NGB.Api/NGB.Api.csproj
NGB.PropertyManagement.Api/Program.cs
```

## Why these two files matter

`NGB.Api/NGB.Api.csproj` tells you what the reusable API module really is.

It references:

- `Microsoft.AspNetCore.App`
- authentication / OpenAPI / Serilog / Swagger packages
- `NGB.Contracts`
- `NGB.Application.Abstractions`
- `NGB.Runtime`
- `NGB.Core`
- `NGB.Tools`

That means `NGB.Api` is the shared **HTTP surface layer** on top of runtime/application abstractions.

`NGB.PropertyManagement.Api/Program.cs` then shows how a real vertical host composes that reusable API module with the rest of the platform.

## What Program.cs proves about host composition

The Property Management API host wires together:

- health checks;
- infrastructure configuration;
- `AddNgbRuntime()`;
- `AddNgbPostgres(...)`;
- vertical module registration;
- controllers/OpenAPI;
- global error handling;
- authentication/authorization;
- controller mapping.

This is important because it shows that `NGB.Api` is not the application by itself. It is the shared web/API layer that gets **composed** into a vertical host.

## How to read the API module correctly

A common mistake is to think of `NGB.Api` as "just controllers". The project boundary shows something richer.

It is the platform layer where these concerns meet:

- ASP.NET Core plumbing;
- shared controller conventions;
- auth integration;
- error handling;
- runtime/application service exposure;
- HTTP-ready DTO contracts.

## What the Property Management host is a good example of

`NGB.PropertyManagement.Api/Program.cs` is a valuable source anchor even though it is not in `NGB.Api` itself.

Why? Because it shows the intended contract between reusable modules.

The host does **not** reimplement platform behavior. Instead, it composes:

- platform runtime;
- PostgreSQL provider;
- vertical definitions/runtime/provider modules;
- reusable API stack.

That is exactly how new vertical API hosts should be built.

## Reading order

```text
1. NGB.Api/NGB.Api.csproj
2. NGB.PropertyManagement.Api/Program.cs
```

After that, continue into the controller files and API-specific extension methods inside `NGB.Api` in your local working tree.

## Practical guidance for future vertical API hosts

When you create a new vertical API host, keep the same structure.

- keep host setup thin;
- compose reusable modules explicitly;
- keep business logic in Runtime / vertical runtime services;
- keep provider-specific persistence in PostgreSQL modules;
- keep the API host focused on web concerns and composition.

## Operational note

The current Property Management host uses a completely allowed CORS policy in the checked source. Treat that as a demo/development posture and environment-gate it before production deployment.
