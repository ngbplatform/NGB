---
title: Repository Structure
---

# Repository Structure

The repository is a monorepo. Platform modules, vertical solutions, shared UI packages,
Docker assets, package tooling and documentation live together. Backend `NGB.*`
project directories live at the repository root. `NGB.sln` contains the backend
library, host and test projects; frontend and packaging tooling have separate
entry points.

A simplified map follows. The headings are logical groups, not physical directories.

```text
NGB/
│
├─ Backend solution and shared build configuration
│  ├─ NGB.sln
│  ├─ Directory.Build.props
│  └─ NuGet.config
│
├─ Platform core
│  ├─ NGB.Core
│  ├─ NGB.Metadata
│  ├─ NGB.Definitions
│  ├─ NGB.Contracts
│  ├─ NGB.Application.Abstractions
│  ├─ NGB.Runtime
│  ├─ NGB.Runtime.Hosting
│  ├─ NGB.Accounting
│  ├─ NGB.OperationalRegisters
│  ├─ NGB.ReferenceRegisters
│  ├─ NGB.Hosting.AspNetCore
│  ├─ NGB.Api
│  ├─ NGB.BackgroundJobs
│  ├─ NGB.BackgroundJobs.PostgreSql
│  ├─ NGB.Watchdog
│  ├─ NGB.PostgreSql
│  ├─ NGB.PostgreSql.AspNetCore
│  ├─ NGB.Persistence
│  ├─ NGB.Tools
│  └─ NGB.Migrator.Core
│
├─ Shared content services
│  ├─ NGB.Attachments
│  ├─ NGB.Attachments.MinIO
│  └─ NGB.Notes
│
├─ MCP Server and AI Assistant
│  ├─ NGB.Mcp
│  ├─ NGB.Mcp.AspNetCore
│  ├─ NGB.AiAssistant
│  ├─ NGB.AiAssistant.AspNetCore
│  ├─ NGB.AiAssistant.Ollama
│  ├─ NGB.AiAssistant.OpenAI
│  ├─ NGB.AiAssistant.Anthropic
│  └─ NGB.AiAssistant.Bedrock
│
├─ Vertical solutions
│  ├─ NGB.AgencyBilling.*
│  ├─ NGB.CRM.*
│  ├─ NGB.PropertyManagement.*
│  └─ NGB.Trade.*
│
├─ UI workspace
│  ├─ ui/ngb-ui-framework
│  ├─ ui/ngb-agency-billing-web
│  ├─ ui/ngb-crm-web
│  ├─ ui/ngb-property-management-web
│  ├─ ui/ngb-trade-web
│  ├─ ui/ngb-auth-theme
│  └─ ui/scripts
│
├─ Docker environments
│  ├─ docker/
│  ├─ docker-compose.ab.yml
│  ├─ docker-compose.crm.yml
│  ├─ docker-compose.pm.yml
│  └─ docker-compose.trade.yml
│
├─ Packages and external applications
│  ├─ packaging/nuget
│  ├─ packaging/templates
│  ├─ packaging/PUBLISHING.md
│  └─ ngb.mjs
│
├─ Performance tests
│  ├─ performance-tests/ngb-performance-tests-framework
│  ├─ performance-tests/ngb-property-management-perf
│  ├─ performance-tests/ngb-trade-perf
│  ├─ performance-tests/ngb-agency-billing-perf
│  └─ performance-tests/scripts
│
├─ Tests and verification
│  ├─ NGB.*.Tests / NGB.*.IntegrationTests
│  ├─ ui/**/tests / ui/tests
│  ├─ quality/ai-assistant
│  ├─ quality/mcp
│  ├─ quality/integration
│  ├─ quality/coverage
│  ├─ quality/external-consumer
│  ├─ quality/upgrade-certification
│  ├─ quality/mutation
│  ├─ run-backend-full-coverage.sh
│  └─ run-frontend-full-coverage.sh
│
└─ Documentation and CI
   ├─ docs/
   └─ .github/workflows/
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
- `NGB.PostgreSql.AspNetCore`
- `NGB.Persistence`
- `NGB.Hosting.AspNetCore`
- `NGB.Runtime.Hosting`
- `NGB.Api`
- `NGB.BackgroundJobs`
- `NGB.BackgroundJobs.PostgreSql`
- `NGB.Watchdog`
- `NGB.Migrator.Core`

They do not define business meaning on their own. Their job is to surface, persist, schedule, or operate the platform.

`NGB.Attachments` and `NGB.Notes` provide shared content capabilities;
`NGB.Attachments.MinIO` implements the object-storage adapter.
`NGB.Mcp` and `NGB.Mcp.AspNetCore` expose existing authorized application reads.
`NGB.AiAssistant` orchestrates bounded model/tool turns, while
`NGB.AiAssistant.AspNetCore` provides HTTP/SSE and the MCP client. The Ollama,
OpenAI, Anthropic and Bedrock projects supply replaceable model adapters.
See [Platform Projects](/reference/platform-projects), [MCP Server](/guides/mcp-server)
and [AI Assistant](/guides/ai-assistant).

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

All four verticals include definition, Runtime, PostgreSQL, API, Migrator,
BackgroundJobs, Watchdog and test projects. PM, Trade and Agency Billing reference
platform source projects. CRM consumes `NGB.Platform.*` NuGet packages and keeps
project references within its own vertical. Build the matching local packages
before testing unpublished platform changes through CRM.

### 5. Shared UI workspace

The UI workspace proves that the platform is not backend-only. It contains:

- reusable UI framework components;
- metadata-driven web clients;
- vertical web applications;
- Keycloak theme assets.

AI Assistant belongs to `ui/ngb-ui-framework`; its panel behavior is shared across
verticals. CRM declares a versioned `@ngbplatform/ui` dependency and consumes the
packaged UI in its isolated Docker build; a root UI workspace install can link the
matching local workspace. The other demo web applications use the workspace source.

### 6. Packaging and verification

`packaging/nuget/projects.txt` lists the projects to pack, and
`packaging/templates` contains the external-application starter. The root
`ngb.mjs` delegates to the application CLI maintained with that starter.
Follow [local package preparation](/start-here/run-locally#prepare-local-platform-packages)
and [external application upgrades](/architecture/external-app-upgrades) when
working across package boundaries.

`quality/` contains MCP, assistant, integration, coverage, package-consumer,
upgrade and mutation verification tooling. The root full-coverage scripts produce
reports under `artifacts/coverage/`. Generated artifacts are ignored by Git.
The separate `performance-tests` workspace contains k6 scenarios and diagnostics.

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
