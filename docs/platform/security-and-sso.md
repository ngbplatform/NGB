---
title: Security and SSO
description: Security and SSO overview for NGB local and demo environments.
---

# Security and SSO

<div class="doc-badge-row">
  <span class="doc-badge doc-badge--verified">Verified</span>
  <span class="doc-badge doc-badge--inferred">Inferred</span>
</div>

This page documents the security and SSO shape visible from the verified host/bootstrap files and local environment settings.

## Verified anchors

```text
README.md
docker-compose.pm.yml
.env.pm
NGB.PropertyManagement.Api/Program.cs
```

## What is directly visible

The local/demo environment uses Keycloak as the identity provider.

The verified compose and environment files show:

- one realm for the local demo environment;
- separate clients for API, web, background jobs, watchdog, and tester;
- the API host configured against a Keycloak issuer;
- the web client configured with Keycloak realm, client id, and redirect URLs.

## Platform responsibility versus vertical responsibility

The platform is responsible for:

- SSO integration shape;
- auth middleware wiring in hosts;
- shared conventions around issuer/client configuration.

The vertical solution remains responsible for:

- which business capabilities are exposed;
- role naming and authorization policy specifics;
- domain-level permission checks.

## Practical local-development notes

For local development, keep these aligned:

- Keycloak realm name;
- client ids for each host;
- issuer URL used by the API host;
- redirect/logout URLs used by the web client.

## Related pages

- [Run Locally](/start-here/run-locally)
- [Manual local runbook](/start-here/manual-local-runbook)
- [Configuration reference](/reference/configuration-reference)
