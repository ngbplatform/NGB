---
title: Watchdog
---

# Watchdog

Watchdog is the platform’s health and operability surface.

It gives operators and developers a single place to check whether the environment is alive and whether the major hosts are reachable.

## Why Watchdog exists

In a modular platform, one green API process is not enough.

An environment also depends on:

- the API host;
- the Background Jobs host;
- the Watchdog host itself;
- the web client reachability path;
- external dependencies such as PostgreSQL and Keycloak where relevant.

Watchdog exists to make that health picture easy to inspect.

## What Watchdog typically provides

A Watchdog host usually provides:

- a health endpoint;
- a health UI / aggregate view;
- configured checks for the relevant hosts;
- links back into the environment.

## Local PM example

In the Property Management local Docker Compose environment, the Watchdog host aggregates checks for:

- itself;
- the Property Management API;
- the Property Management Background Jobs host.

The Watchdog UI is exposed at:

```text
https://localhost:7075/health-ui
```

The base health endpoint is:

```text
https://localhost:7075/health
```

## Why a dedicated host is useful

A dedicated Watchdog surface avoids mixing operator diagnostics into the main product UI and gives a stable place to:

- verify environment readiness;
- troubleshoot startup order;
- confirm host-to-host reachability.

## Startup dependency role

In the local Docker environment, Watchdog starts after the critical application pieces are available.

That ordering makes it a good final checkpoint that the whole environment is healthy enough for interactive work.

## What good health checks look like

A good health check should be:

- cheap enough to run regularly;
- meaningful enough to detect a real problem;
- scoped to a dependency that matters;
- not so broad that it becomes noisy or flaky.

## What Watchdog is not

Watchdog is not a substitute for:

- structured logs;
- traces;
- business integrity scans;
- alerting strategy.

It is a visibility surface, not the whole observability stack.

## Developer guidance

When composing a new vertical Watchdog host:

- include the vertical’s API and background jobs;
- keep naming clear and environment-specific;
- expose a clean operator UI;
- avoid checks that are too expensive or too noisy;
- use it as a real operability surface, not a demo-only extra.

A platform with API, jobs, and migrations but no coherent health surface is much harder to operate confidently. Watchdog closes that gap.
