---
title: Repository Structure
---

# Repository Structure

The repository is a monorepo. Platform modules, vertical solutions, shared UI packages, Docker assets, and documentation live together.

A simplified top-level map looks like this:

```text
NGB.sln
│
├─ Platform core
│  ├─ NGB.Core
│  ├─ NGB.Metadata
│  ├─ NGB.Definitions
│  ├─ NGB.Contracts
│  ├─ NGB.Application.Abstractions
│  ├─ NGB.Runtime
│  ├─ NGB.Accounting
│  ├─ NGB.OperationalRegisters
│  ├─ NGB.ReferenceRegisters
│  ├─ NGB.Api
│  ├─ NGB.BackgroundJobs
│  ├─ NGB.Watchdog
│  ├─ NGB.PostgreSql
│  ├─ NGB.Persistence
│  ├─ NGB.Tools
│  └─ NGB.Migrator.Core
│
├─ Vertical solutions
│  ├─ NGB.PropertyManagement.*
│  ├─ NGB.Trade.*
│  └─ NGB.AgencyBilling.*
│
├─ UI workspace
│  ├─ ui/ngb-ui-framework
│  ├─ ui/ngb-property-management-web
│  ├─ ui/ngb-trade-web
│  ├─ ui/ngb-agency-billing-web
│  └─ ui/ngb-auth-theme
│
├─ Docker
│  ├─ docker/
│  ├─ docker-compose.pm.yml
│  ├─ docker-compose.trade.yml
│  └─ docker-compose.ab.yml
│
├─ Documentation
│  └─ docs/
│
└─ Tests
   ├─ platform unit tests
   ├─ platform integration tests
   ├─ vertical unit tests
   └─ vertical integration tests
```

## How to read the monorepo

A productive way to navigate the repository is to think in layers.

### 1. Shared platform contracts and metadata

These projects describe the public shape of the platform:

- `NGB.Contracts`
- `NGB.Metadata`
- `NGB.Definitions`
- `NGB.Application.Abstractions`
- `NGB.Core`

They are the stable vocabulary used by API hosts, Runtime, PostgreSQL infrastructure, and vertical modules.

### 2. Shared execution and business engines

These projects implement the reusable mechanics of the platform:

- `NGB.Runtime`
- `NGB.Accounting`
- `NGB.OperationalRegisters`
- `NGB.ReferenceRegisters`

If you want to understand how catalogs, documents, posting, registers, report execution, or explainability work, these are the most important projects.

### 3. Infrastructure and hosts

These projects compose or persist the platform:

- `NGB.PostgreSql`
- `NGB.Persistence`
- `NGB.Api`
- `NGB.BackgroundJobs`
- `NGB.Watchdog`
- `NGB.Migrator.Core`

They do not define business meaning on their own. Their job is to surface, persist, schedule, or operate the platform.

### 4. Vertical solutions

Verticals add business-specific meaning without rewriting the platform. They define:

- catalogs;
- documents;
- reports;
- policies;
- posting handlers;
- migrations;
- seeds;
- API / watchdog / background-job hosts for that solution.

### 5. Shared UI workspace

The UI workspace proves that the platform is not backend-only. It contains:

- reusable UI framework components;
- metadata-driven web clients;
- vertical web applications;
- Keycloak theme assets.

## Recommended code-reading order

When you need to understand a feature end to end, the most effective sequence is:

1. read the vertical definition;
2. find the corresponding Runtime orchestration path;
3. inspect the posting / register / report logic;
4. inspect the PostgreSQL implementation;
5. inspect the UI behavior.

That order follows the way NGB is designed: platform meaning is declared high in the stack and then executed through shared runtime and infrastructure paths.

## Repository rules that matter

Several rules are important when changing the codebase:

- keep platform code free from vertical leakage;
- prefer provider-agnostic Runtime orchestration;
- put concrete PostgreSQL behavior in `NGB.PostgreSql` or a vertical PostgreSQL module;
- keep append-only effects explicit;
- keep migrations deterministic and reviewable;
- keep tests close to the behavioral contract you are changing.

The rest of the documentation explains those rules in more detail.
