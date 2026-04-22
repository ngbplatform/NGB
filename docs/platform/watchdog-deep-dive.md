---
title: "Watchdog Deep Dive"
description: "How NGB uses a dedicated Watchdog host for health and operability visibility."
---

# Watchdog Deep Dive

> **Page intent**
> This page explains the watchdog host as the operability surface of an NGB deployment.

## Trust level

- **Verified anchors:** `NGB.PropertyManagement.Watchdog/Program.cs`, `docker-compose.pm.yml`
- **Architecture synthesis:** yes
- **Template guidance:** yes

## Chapter navigation

- [Background Jobs Deep Dive](/platform/background-jobs-deep-dive)
- [Host Composition and DI Map](/architecture/host-composition-and-di-map)
- [Runtime Request Flow](/architecture/runtime-request-flow)
## Verified source anchors

Confirmed in:

- `NGB.PropertyManagement.Watchdog/Program.cs`
- `docker-compose.pm.yml`

Verified behavior:

- vertical watchdog host is created through `AddNgbWatchdog(...)`;
- app pipeline uses and maps NGB watchdog middleware/endpoints;
- in docker-compose, the watchdog depends on API, Background Jobs, Web, Keycloak, Seq, and migrator-completed infrastructure;
- health UI entries are configured for watchdog itself, API, and background jobs.

## Architectural role

Watchdog is the platform’s consolidated health and operability host.

It should answer:

- is the deployment alive?
- are core services reachable?
- are operators able to inspect health from one place?

## Recommended design rules

- keep watchdog separate from the main API host;
- aggregate health of the deployment, not only the watchdog process itself;
- make links to operational tooling easy to discover;
- treat it as an ops-facing host, not a business API.

## Related pages

- [Background Jobs Deep Dive](/platform/background-jobs-deep-dive)
- [Migrator Deep Dive](/platform/migrator-deep-dive)
- [Host Composition and DI Map](/architecture/host-composition-and-di-map)
