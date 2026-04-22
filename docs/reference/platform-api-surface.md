---
title: Platform API surface
description: Verified API-host shape for NGB, including authentication, health checks, external links, and the document-oriented application boundary.
---

# Platform API surface

<div class="doc-badge-row">
  <span class="doc-badge doc-badge--verified">Verified</span>
  <span class="doc-badge doc-badge--inferred">Scope interpretation</span>
</div>

## Verified anchors

```text
NGB.Api/NGB.Api.csproj
NGB.PropertyManagement.Api/Program.cs
NGB.Application.Abstractions/Services/IDocumentService.cs
NGB.Api/Sso/DependencyInjection.cs
```

## What is directly visible

The verified API host composition shows that the reusable API layer is assembled from:

- health checks;
- Keycloak-backed authentication;
- runtime + PostgreSQL wiring;
- controller-based API endpoints;
- external-link contributions for Watchdog and Background Jobs;
- reporting-access context integration.

## Authentication surface

The verified Keycloak DI layer confirms that the reusable API stack supports:

- JWT bearer authentication;
- issuer validation against `KeycloakSettings:Issuer`;
- audience validation against configured `KeycloakSettings:ClientIds`;
- role extraction from Keycloak claim shapes such as `realm_access`, `resource_access`, `role`, and `roles`;
- an admin-console cookie/OIDC mode for interactive admin scenarios.

This matters because API configuration is not just “turn auth on.” The reusable layer already expects a concrete issuer, client-id set, and role mapping strategy.

## Document-oriented application boundary

The verified `IDocumentService` contract shows that the platform API surface is broader than CRUD. At the application boundary it already supports:

- metadata discovery;
- paging and point reads;
- cross-type lookup;
- draft create, update, and delete;
- post, unpost, and repost;
- mark and unmark for deletion;
- derivation discovery and draft derivation;
- relationship graph access;
- document effects access.

That is the strongest direct evidence that the reusable API layer is built around business operations, not only generic row mutation.

## Host-level integrations visible in PM

The verified PM API program wires in:

- PostgreSQL health checks;
- Keycloak health checks;
- structured logging through Serilog;
- external links for Watchdog and Background Jobs;
- `IMainMenuContributor` and command-palette search integration;
- `IReportVariantAccessContext` replacement for HTTP-aware reporting access.

## What this page intentionally does not claim

This page describes the reusable API host shape and the verified application boundary. It is not a full endpoint-by-endpoint route catalog, because the current verified anchor set is stronger on host composition and service contracts than on controller inventory.

## Related pages

- [API](/platform/api)
- [API source map](/platform/api-source-map)
- [API runtime PostgreSQL integration](/platform/api-runtime-postgres-integration)
- [Configuration reference](/reference/configuration-reference)
