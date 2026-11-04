# NGB Platform v3.4.0 — AI Development Kit

**Implementation specification**

- **Status:** Approved design, revised 2026-10-25; implementation and release certification pending.
- **Baseline:** Published NGB v3.3.0 (`main` at preparation time: `be691b30a86ae6f636134d5ff15f75a678939e28`).
- **Target:** Production-ready v3.4.0, covering 100% of NGB's **supported public application-development capabilities**.
- **Source repository:** https://github.com/ngbplatform/NGB

> **Your assignment is to implement the complete feature, not merely produce an architecture proposal.** Inspect the actual latest checkout, design the smallest coherent changes consistent with its established architecture, implement them end to end, add exhaustive tests, run the applicable quality and certification suites, fix failures, and report evidence. Follow the existing repository conventions and supported public contracts. When this document presents illustrative names or paths, confirm them against the codebase rather than treating illustrations as existing APIs.

This document records the agreed AI Dev Kit v3.4.0 requirements. New commands, files and packages described here are target contracts, not claims that the feature has already been implemented or published. Its scope is the public platform SDK, Kit tooling and independent application examples. Overall platform-release regression requirements are owned by release verification outside the Kit. Apply the current repository instructions and the user's authorization boundaries when implementing it.

---

## 1. Objective and non-negotiable product contract

Deliver **NGB AI Development Kit (AI Dev Kit) v3.4.0** as a first-class, versioned, IDE-independent, agent-agnostic developer capability distributed with official NGB external applications.

A developer must be able to:

1. Install `NGB.Platform.Templates@3.4.0` and run `dotnet new ngb -n ExampleApplication` (or use the existing root `ngb.mjs create` workflow).
2. Open the generated application in **Rider, Visual Studio, VS Code, another IDE, or a terminal**. No IDE plugin is a required dependency.
3. Use a compatible coding agent, especially **Codex** and **Claude Code**, with **zero manual copying of NGB architectural documents or skill files**.
4. Ask the agent to develop complete NGB business functionality through the published platform SDK and the application's own codebase: backend, frontend, database migrations, security, reports, effects, tests, and deployment configuration as applicable.
5. Have the agent discover task-specific guidance through concise project instructions, progressively loaded skills, version-matched references, verified examples, and the existing NGB CLI.
6. Run the **same deterministic** validation and test commands manually and in CI without an LLM.
7. Upgrade both the NGB SDK and the AI Dev Kit through a **documented, version-specific, previewable, safe migration workflow** preserving developer-owned changes.
8. Review auditable evidence of exactly what was generated and which architecture, build, test, security, compatibility, and functional acceptance gates passed.

**This is code-first development, not a proprietary low-code environment.** The output is developer-owned, buildable .NET/TypeScript/Vue source code in an independent external repository; it must use published NuGet/npm packages, not secret dependencies on the NGB source monorepo.

**100% capability coverage** means every supported public NGB feature relevant to building, extending, configuring, operating, or integrating external NGB applications has **discoverable, version-accurate guidance, an applicable workflow, verified usage examples or validated contract references, and objective verification**. Do not confuse this with an impossible promise that arbitrary internal implementation details are public extension points, or that AI is infallible. Identify genuine public extensibility gaps explicitly. Record the affected workflow, existing contracts and proposed change for a separate scope decision; do not invent support, expose private implementations, or silently expand platform business behavior to make an AI scenario pass.

**100% test coverage** is a separate strict Kit release requirement for its executable code, official starter/helper code and independent reference implementations: backend **lines/branches/methods = 100%**, frontend **lines/branches/functions/statements = 100%**, with per-file completeness and narrowly documented generated-code exemptions. Preserve the repository's applicable tooling coverage policy. Do not lower thresholds, hide sources, exclude reachable production code, skip failing tests, or change coverage metrics to make the gate green.

### 1.1 Strict exclusions

Do **not** introduce:

- A second competing developer CLI. Extend existing `ngb.mjs` and its shared implementation.
- A separate `ngb-tooling` template. Distribute the same CLI implementation through the existing application template and the official `@ngbplatform/cli` npm package.
- A mandatory VS Code/Rider/Visual Studio plugin, proprietary IDE integration, or cloud development service.
- A hosted LLM, built-in prompt execution service, vector database, RAG service, or agent runtime solely to implement this kit.
- A separate AI database or persistent user-chat system.
- A duplicate document/catalog/report framework, bespoke business-application generator, or generic effects table.
- Dependencies from provider-neutral runtime/domain layers to PostgreSQL, ASP.NET Core, Node.js, MCP, AI Dev Kit, application API hosts, or UI internals.
- Arbitrary SQL or new write access in the **v3.3.0 runtime MCP** endpoint. Its read-only, current-user-authorized semantics remain intact.
- Compatibility hacks for obsolete approaches or hidden fallback to NGB monorepo sources in external applications.

Treat the existing external application starter and developer tooling as the foundation; improve them without inventing a parallel system.

### 1.2 Consumer independence

The Kit knows public platform capabilities and extension contracts, not a fixed set of consuming products. Do not embed existing applications' names, project lists, business definitions, reports, configuration, data or workflows in its instructions, skills, references, capability registry, validators or acceptance criteria. Capability applicability is determined by public contracts, feature prerequisites and application-owned configuration, never a product-name mapping.

Examples are minimal, independently authored consumers of published SDK/UI contracts. They must compile and run without existing application implementations or source access. Application-specific guidance belongs in that application's `ai/custom/` content.

Dependency direction is one-way: applications consume the platform and Kit; external release verification consumes Kit evidence alongside its own application-regression evidence. The Kit does not import that regression inventory or require those applications to validate its own contracts. Repository-wide release gates remain mandatory under the external release policy.

---

## 2. Establish the actual v3.3.0 baseline before editing

Inspect current `main`, the v3.3.0 release, working-tree status, and existing tests. **Do not assume** project names, package APIs, registration signatures, or source layout beyond what the checked-out repository verifies.

These verified v3.3.0 anchors are especially important:

| Area | Current source anchor / behavior |
|---|---|
| Template package | `packaging/templates/NGB.Platform.Templates.csproj` packs `packaging/templates/content/**/*` |
| Template definition | `packaging/templates/content/.template.config/template.json`, `shortName: ngb`, `sourceName: NgbApplication` |
| Repo CLI | `ngb.mjs` delegates to `packaging/templates/content/infrastructure/ngb/cli.mjs` |
| Generated app CLI | `packaging/templates/content/infrastructure/ngb.mjs` delegates to `infrastructure/ngb/cli.mjs` |
| Existing CLI code | `packaging/templates/content/infrastructure/ngb/{cli,application,contracts}.mjs` |
| Existing CLI verbs | `create`, `upgrade`, `start`, `deploy` |
| Current upgrade | Staged isolated dependency/lockfile validation, operation lock, preserved user source, backup/rollback of changed files |
| Existing certification | `quality/external-consumer/`, `quality/upgrade-certification/`, `quality/external-consumer/matrix.json` |
| Documentation | `docs/architecture/external-app-upgrades.md`, `docs/guides/extend-external-application.md`, `docs/guides/platform-extension-points.md` |
| MCP and Assistant | `NGB.Mcp*`, `NGB.AiAssistant*`, `docs/guides/mcp-server.md`, `docs/guides/ai-assistant.md` |
| Shared UI | `@ngbplatform/ui` and public entry points, declarations, Tailwind preset |

The v3.3.0 public SDK and external starter already include MCP Server and AI Assistant support. These are **existing platform capabilities** that the new AI Kit must document and support through their public contracts; do not reimplement them.

Read current public SDK documentation and APIs, packaging/release workflows, independent consumer fixtures, test utilities, applicable coverage verifiers, supported upgrade transitions, npm exports and NuGet package lists before designing. Produce an internally traceable inventory of verified public contracts and unsupported extension areas. Existing application implementations are not Kit dependencies or authoritative public API definitions.

Use the repository's **actual** `main` state if it advanced after this document was authored, and account for the delta explicitly.

---

## 3. Target architectural shape

### 3.1 Logical topology

```text
NGB monorepo (canonical authoring and verification)
│
├── Platform and public SDK sources (existing source of truth)
├── docs/                         (existing human documentation)
├── ai-devkit/                    (NEW: canonical AI kit authoring)
│   ├── SPECIFICATION.md          (this approved implementation contract)
│   ├── manifests / capability matrix
│   ├── project instructions
│   ├── reusable skills and reference material
│   ├── verified examples and generation adapters
│   └── packaging helpers
├── packaging/templates/content/   (existing external template)
│   ├── generated application projects
│   ├── generated AGENTS.md / CLAUDE.md
│   ├── generated Codex and Claude skills
│   ├── generated local AI reference snapshot / manifest
│   └── infrastructure/ngb.mjs + shared CLI implementation
├── quality/ai-devkit/            (NEW: quality + certification extensions)
├── quality/external-consumer/    (EXISTING: extended, not duplicated)
└── ngb.mjs                      (EXISTING: single root CLI entry)
        │
        ▼ published exact-version NGB.Platform.Templates NuGet package
        │
        ▼ dotnet new ngb -n ExampleApplication

ExampleApplication/ (independent external app)
├── AGENTS.md
├── CLAUDE.md
├── .agents/skills/...
├── .claude/skills/...
├── ai/
│   ├── kit.json
│   ├── vendor/                  (official instructions, skills and references)
│   └── custom/                  (application-owned instructions and skills)
├── ngb.config.json              (application-owned project and test configuration)
├── infrastructure/ngb.mjs + infrastructure/ngb/...
├── ExampleApplication.Api / Migrator / BackgroundJobs / ...
├── web/ (Vue application)
└── solution, configuration, lockfiles, backend/integration/frontend/E2E tests
        │
        ▼ Codex / Claude Code / other compatible coding agents
        │   read project instructions → select skills → inspect SDK contracts
        │   implement code → run existing NGB CLI → review failures → fix
        ▼ buildable, testable, independently deployable business application
```

`ai-devkit/` is the canonical authoring directory in NGB. `ai/` is the installed content directory in an independent application; these paths are not a rename of one another. Canonical authoring **must not** turn into manually maintained copies of the same rules.

Publish the same shared CLI implementation as `@ngbplatform/cli`, exposing the `ngb` executable. The repository entry point, generated application entry point and npm entry point use the same command implementation. Section 7.5 defines delivery and section 8.5 defines target-version acquisition for upgrades.

### 3.2 Ownership and layer boundaries

- `NGB.Definitions`, `NGB.Metadata`, public contracts and abstractions: publicly supported descriptions and interfaces.
- `NGB.Runtime`: business application orchestration; **never** direct database drivers, SQL, HTTP hosting, or agent SDK logic.
- `NGB.Accounting`, `NGB.OperationalRegisters`, `NGB.ReferenceRegisters`: **peer** specialized engines, not a hierarchical accounting-first chain. Standard Post requires a registered handler, which may intentionally produce no movements. A workflow-only document can therefore become Posted with empty effects. Post with no registered handler is a separate unsupported configuration that returns `doc.posting.handler.not_configured`. Preserve both existing behaviors without a platform change.
- `NGB.PostgreSql` / application PostgreSql projects: SQL, specialized read paths, provider storage adapters, embedded immutable migrations.
- API/BackgroundJobs/Migrator hosts: service composition, hosting integration, process lifecycle; no application business rule implementations in host controllers or `Program.cs`.
- Frontend: Vue 3 and public `@ngbplatform/ui` exports. No imports of monorepo-private paths or copying private framework components into external applications.
- AI Kit authoring: developer guidance and source-backed references, **outside runtime business logic**.
- NGB CLI: deterministic orchestration and diagnostics (Node.js may invoke .NET, TypeScript, Docker and existing test tools); not an alternative business engine.
- `quality/`: repository release verification and consumer conformance; does not become a runtime dependency of applications.

Respect the established domain rules and current public semantics: GUID v7 where the platform requires it, UTC instants and `DateOnly` for periods, explicit Draft/Post/Apply/Mark for Deletion actions where applicable, draft-save vs post-time business validation, append-only reversal/storno history, document action idempotency and concurrency, permissions at the backend, bounded reads/writes, and existing authorization semantics. **Always confirm the exact action/behavior is supported by the resolved public contracts and the application's registered definitions rather than inventing one.**

Preserve the public administrator contract in every example and validator: authenticated active application administrators receive all registered permissions, including Attachments, Notes and newly registered capabilities. Recognize explicitly registered application role codes and trusted Keycloak role `ngb-admin`, including administrators without a platform user record; never infer administration from editable display names. Keep API/UI behavior consistent, feature flags independent, and inactive accounts blocked.

MCP and AI Assistant remain adapters over existing authorized application services. Work on their examples or adapters does not authorize changes to business logic, SQL readers, schemas, migrations, reports, domain filters, business contracts or permission rules. Report unsupported scenarios and obtain a separate explicit scope decision for business-layer changes.

---

## 4. Full public capability inventory and coverage model

Implement a **machine-readable AI Dev Kit Capability Registry** rather than an unverified hard-coded claim that 'all features are supported'. For each registered supported capability record at least:

- Stable ID; display name; exact SDK/platform version; capability category.
- Verified public NuGet assemblies/packages, interfaces/types and/or npm public entry points.
- Supported operations (`create`, `extend`, `configure`, `operate`, `inspect`, `test`, `upgrade`) as applicable; mark unsupported operations explicitly.
- Architecture/layer placement and hard constraints.
- Related skills and reference sections; real, compiling consumer examples where possible.
- Required validators and acceptance test IDs, including negative/authorization tests.
- Public contract prerequisites, optional feature dependencies and feature-flag requirements.
- Metadata about source ownership, revision, and automated evidence; no fabricated API names.

Build inventories from **actual source**, public API snapshots, NuGet exports, published npm declarations/exports, registered platform definitions and docs. Cross-check hand-authored metadata against authoritative sources. New/changed public contracts must trigger targeted capability-map and reference verification. Flag missing mappings as blocking errors. Avoid treating every internal type as public SDK.

Use this single registry to track concrete operations within broad domains. For example, one green "reporting" row cannot stand for independently unverified grouping, totals, paging and provider-side execution. Classify relevant discovered public contracts, map supported operations to evidence, and fail on missing classifications or mappings. A contract's public visibility alone does not establish a supported customization workflow. Keep unsupported operations, missing evidence and disabled optional features distinguishable; do not reclassify a supported operation merely to make the coverage percentage pass.

### 4.1 Required comprehensive domains

Cover **every supported public feature**, including but not limited to:

1. External application creation, local environment setup, host composition, module registration and DI.
2. Shared primitives, contract shapes, metadata, definitions and all publicly supported extension points.
3. Catalog metadata, hierarchical/tabular storage where supported, CRUD, filters, validators, type-specific persistence, navigation and presentation.
4. Document head and part tables, metadata, draft save, posting validation, standard lifecycle, mark/unmark for deletion where supported, versions and audit.
5. Document Actions: lifecycle, derivation, custom handlers, context enrichers, authorization and availability evaluators, action definitions, idempotency, optimistic concurrency, semantic targets.
6. Document Flow, relationship mirroring, latest/current Effects and explainability.
7. Operational Registers: dimensions, movement semantics, writes, balances/turnovers, projections, reconciliation and testing.
8. Reference Registers: definitions, append-only write semantics, state/projections and relevant temporal/history behavior.
9. Accounting: chart of accounts, policy-driven posting, entries, General Journal Entries, financial periods, relevant reports and transaction correctness.
10. Posting effect combinations: Accounting/OR/RR as independent peer capabilities; supported Post without movements through an explicitly registered workflow handler; rejection when no handler is registered; reversal/storno rather than destructive regeneration.
11. Reporting: canonical reports, composable datasets, canonical and provider-side execution extension points, filters, sorting, grouping, paging/cursors, totals/subtotals, downloads, drilldown and UI.
12. Search, metadata/list/detail reads, bounded high-volume paths and authorization-constrained access.
13. Users, roles, permissions, Keycloak identity integration, application administration and permission-aware UI/navigation.
14. Work Center: tasks, informational notifications, role-scoped preferences, event policies, outbox, retries, deduplication and UI.
15. Attachments and Notes: feature flags, parent authorization, lifecycle, audit, storage/provider (including MinIO where configured), UI and API.
16. AuditLog and business history, source attribution and safe access to sensitive fields.
17. Background Jobs/Hangfire: job registration, scheduling, storage, idempotency, retries, dead-letter behavior, host health.
18. PostgreSQL/persistence contracts, Unit of Work, transaction boundaries, batching, SQL/index performance, migration packs, deterministic seeds, dry runs and safe schema evolution.
19. API hosting, public controllers, error contracts, feature flags, health/Watchdog and observability.
20. Frontend: published Vue components and props/emits/slots, metadata-driven list/forms/editors, dashboards/report UI, theme/style/tokens, navigation, drawers, responsive behavior, accessibility, built-in Assistant UI configuration.
21. MCP Server v3.3: opt-in host composition, authorized read-only tools, custom typed read contributors, transport/security/limits, usage from compatible clients.
22. AI Assistant v3.3: opt-in composition, provider adapters (`Ollama`, `OpenAI-compatible`, `Anthropic`, `Bedrock`), feature dependencies, assistant context, streaming UI, configuration and tests.
23. Local and production infrastructure: Docker/Compose, secrets, Keycloak, HTTP/TLS/reverse proxy, logging, health, environment management and infrastructure boundaries.
24. Quality: unit/contract/integration/HTTP/browser/E2E tests, performance/k6, test data, coverage, mutation and regression testing.
25. Compatibility and upgrades: supported version transitions, package identity, source contract compatibility, database migration safety, backup/restore, preservation of application customization.
26. Release/publishing: application build, artifacts, platform package publication/certification contracts, CI/CD composition and deployment acceptance.
27. Independently buildable reference applications and workflows that demonstrate public SDK composition without dependencies on existing products.

**Completeness rule:** inventory the real current API/feature surface, split broad domains into as many discrete capabilities as necessary, and expand this list wherever NGB exposes more. A mere matrix row without verified guidance and tests is not coverage. A capability's unsupported external customization must be documented accurately, not simulated by reaching into private platform internals.

**Posting contract clarified 2026-10-26:** no movements and no registered handler are different conditions. The existing SDK supports Post without movements when an explicitly registered handler completes without emitting records. A workflow-only example must prove successful Post, validation, authorization and empty effects through that public contract. Separately, omitting all Accounting/OR/RR handlers must produce `doc.posting.handler.not_configured`, preserving Draft status and effects. The user declined a change to the SDK on 2026-10-25; that decision remains in force and does not prohibit the existing supported workflow. Record the missing-handler limitation and both executable outcomes in the capability registry. Never introduce artificial movements or direct status writes.

### 4.2 Capability coverage enforcement

Make the registry an enforced release contract:

- Every relevant public capability maps to one or more discoverable skills/workflows and versioned references.
- Every example/identifier resolves to a published contract of the targeted version.
- Every external extension workflow has a compiling, **package-only** usage test or a runtime verification, as appropriate.
- Required security/negative tests exist for permission-sensitive surfaces.
- Definitions/registration metadata and public npm contracts are checked, not merely string-matched in Markdown.
- An intentionally not-supported operation is explicit, testable, and documented; silent gaps fail.
- If the inventory changes with SDK/UI evolution, certification detects and reports the gap.
- Present machine-readable coverage evidence with totals, per-capability states, and no unreviewed waivers.

---

## 5. Agent instructions, skills and reference material

### 5.1 Project instruction files

Generate at the external application root:

- `AGENTS.md` for Codex and agent-agnostic durable project guidance.
- `CLAUDE.md` for Claude Code, with a managed `@AGENTS.md` import and application-owned additions outside the managed block.
- A local `ai/kit.json` with exact platform/kit/CLI/template versions, schema version, file ownership manifest, capability manifest identity, hashes and source package provenance.
- Version-matched `ai/vendor/instructions.md`, `ai/vendor/skills/` and `ai/vendor/references/`.
- Application-owned `ai/custom/instructions.md` and `ai/custom/skills/` as the normal customization locations.

Keep top-level instructions **short**, actionable and low-token: architectural invariants, public SDK boundary, where to find skills, exact build/validate/test commands, permission/security constraints, prohibited shortcuts, release Definition of Done. Task-specific procedures belong in skills; detailed reference APIs in lazily loaded references. Do not dump the entire SDK in `AGENTS.md`.

No secrets, server tokens, account identifiers, direct production credentials, private URLs or internal-only code in generated instructional files.

The root `AGENTS.md` contains a short NGB-managed block delimited by explicit markers. Preserve all application-owned text outside that block. The block connects the official instructions and application-specific additions without loading the entire reference library. Use the same ownership rule for the import block in `CLAUDE.md`. Initial installation previews insertion into existing files; missing, duplicated or modified existing managed markers require an explicit conflict decision.

Generated guidance must require inspecting repository instructions, `.editorconfig`, formatter settings and nearby established code before editing. Preserve existing indentation, spacing, imports, member ordering, blank lines and method structure. Require a final diff review for readability and scoped formatting changes.

### 5.2 Skills: one canonical definition, agent-specific output adapters

Use the common skill authoring source `ai-devkit/skills/...` with correct `SKILL.md` YAML frontmatter (`name`, precise trigger-focused `description`) and useful task steps. Include references/examples/scripts selectively; do not create one giant skill. Support Codex repository discovery (currently `.agents/skills/<skill>/SKILL.md`) and Claude Code repository discovery (currently `.claude/skills/<skill>/SKILL.md`). Verify these installation/discovery conventions against **current official tooling** at implementation time.

Both agent distributions must derive from **the same canonical source** with deterministic rendering. No manually divergent copies of examples, rules or scripts. Shared official references live under `ai/vendor/references/`. Handle relative links correctly from each agent-specific location, including Windows path behavior; packaged output must work when the monorepo is absent. Do not require symlinks. Verify hashes of identical copied content; where agent adapters intentionally differ, verify their deterministic render outputs and shared semantic source identity.

Official skills use `ngb-*` names. Application skills are authored once under `ai/custom/skills/` and rendered as `app-*` skills into both agent directories. `ai sync --apply` without `--to` regenerates them using the installed Kit; it does not discover or download a newer release. Detect edits to generated outputs and preserve them according to section 8 rather than silently overwriting them. Do not claim ownership of unrelated skills already present in either agent directory.

Maintain a versioned compatibility matrix for the tested Codex and Claude Code clients, supported OS/toolchain combinations, root and subdirectory launches, existing project/user instructions, skill-name collisions, discovery, relative references and context budgets. Record actual tested versions. The automatic-discovery guarantee applies to these tested clients; other agents may consume the portable content without an unverified discovery promise. Zero manual copying does not imply automatic agent installation, model configuration or permission changes.

Required skill families (refine names to be descriptive, stable and minimal, not overwhelming):

- Application bootstrap and platform composition.
- Metadata/Definitions and Catalog design/storage/validation.
- Document modeling and lifecycle; Document Actions, derivation, relationships and Effects.
- Operational Registers; Reference Registers; Accounting/posting/GJE.
- Canonical and composable reporting, datasets, paging and UI.
- Security/Keycloak/permissions and feature flags.
- Work Center/outbox/notifications/background processing.
- Attachments/Notes/audit.
- Vue/NGB Shared UI, navigation, editors, styling/accessibility.
- MCP contributions and AI Assistant integration/configuration.
- PostgreSQL persistence, migrations, seeds and performance.
- Testing, coverage, architecture validation, security and E2E.
- Application upgrades, AI Kit sync, compatibility and release/deployment.

For each skill define when to use **and not** use it, prerequisites, proper public extension points, ordered steps, exact CLI tooling, expected artifacts, verification commands, pitfalls and a 'done' checklist. Use **progressive disclosure** and benchmark discovery with Codex and Claude rather than loading all skills simultaneously.

### 5.3 Reliable reference source generation

Human-friendly versioned references may be curated, but **machine-verifiable facts** (API symbols, package names, exports, schemas, compatibility) should be sourced from the actual public artifacts/build metadata whenever possible. Design a deterministic preparation pipeline that:

1. Extracts or verifies public contracts and selected examples for the candidate SDK version.
2. Validates skill references and all internal Markdown links.
3. Verifies selected examples build in isolated external consumers.
4. Checks structured manifests/schema and deterministic content/hashes.
5. Creates the versioned package payload for `NGB.Platform.Templates`.
6. Rejects drift between canonical AI authoring, generated template assets and manifest.

Avoid unchecked generated content overwriting hand-maintained files. Generated output should have documented ownership and reproducible generation. Do not rely on internet requests at IDE invocation time for core functionality; the installed kit must be usable offline (apart from installing/restoring necessary packages and the separately chosen model).

### 5.4 Verified reference implementations

Include useful **complete, executable** reference workflows, not pseudocode with invented `IWhateverService` interfaces. Minimum verified examples:

- End-to-end Catalog with definitions, typed storage, migration, validation, shared UI and permissions.
- Successful Post without Accounting/OR/RR movements through an explicitly registered workflow handler, alongside a separate missing-handler rejection that preserves Draft status and effects.
- Posting document with valid independent Accounting and/or OR/RR effects, transactionality, reporting, audit and proper reversal semantics.
- Custom Document Action/derivation and Work Center task/notification with idempotency, permissions and outbox behavior.
- Canonical and composable reports with provider-side query path, paging, totals and UI.
- Public UI extension through `@ngbplatform/ui` only.
- Scoped custom read-only MCP contributor using existing authorized services; opt-in AI Assistant provider composition.
- Migration and version upgrade including AI Kit customization preservation.

Use publicly supported platform extension points, exact DTO contracts and realistic synthetic seeds/tests. Where full reference applications would duplicate huge amounts of code, use independently buildable fixtures and targeted end-to-end scenarios. Every snippet that purports to compile **must** be compiled/tested in CI; clearly label conceptual diagrams as non-compilable. Do not copy existing products' business implementations or fixtures into the Kit.

---

## 6. Integrate with the existing application template

Update the existing `NGB.Platform.Templates` build/prepare pipeline so `dotnet new ngb -n <Name>` materializes all AI Kit files along with the existing API, Migrator, BackgroundJobs, Vue, application CLI, `.slnx`, configs, lockfiles and the working test foundation described below.

- Preserve `.template.config/template.json` and existing symbol/name substitution behavior.
- Confirm dot-directories (`.agents`, `.claude`) and all nested assets are included in NuGet packing, template installation and generated output on Linux/macOS/Windows; maintain correct encoding/file permissions where relevant.
- Do not include `.env`, local caches, node_modules, binaries, Git state, private credentials, unsealed artifacts or mutable generated test output in the template.
- Keep official Kit assets independent of the application name and exclude them from template name substitution. Preserve name replacement in application projects and tests. Verify the post-template manifest, rendered instruction blocks and every intentionally transformed asset against the declared transformation rules.
- No network call or AI/model invocation is necessary during `dotnet new`.
- The kit must be present even when the developer has not installed any specific coding agent.
- A manual `dotnet new` install must work independently of `ngb.mjs create` and without cloning NGB.
- Ensure `ngb.mjs create` continues to use the same exact published template package instead of silently diverging.
- Do not silently auto-enable MCP, AI Assistant, Notes, Attachments, or other opt-in runtime features just because AI Kit instructions are installed.

For an independent app, generated instructions must call **its own** `node infrastructure/ngb.mjs ...` entry, not the repository-only root CLI.

### 6.1 Working test foundation

Every newly generated application includes executable tests and their configuration:

- A backend xUnit test project for starter-owned executable code and public contracts.
- An integration test project for host composition, migrations and authorization using real disposable PostgreSQL and Keycloak services through the established test stack.
- Vitest tests for the starter frontend.
- Playwright E2E covering login, shell loading, an authorized operation and refusal for a user without the required permission.
- CLI validation assets and an internal .NET validation helper, shipped with the tooling and built separately from application runtime projects.

Use the repository's established testing libraries and pinned toolchain. Include projects in the generated solution, package scripts, lockfiles, profile configuration and source-discovery checks. Tests must run without access to NGB's repository or its `quality/` directory. Do not add empty business projects or sample business entities to the default starter merely to host tests.

The .NET helper is a development tool, not an API/Runtime dependency or a second public CLI. Any NuGet/npm dependencies it needs are explicit and restorable from approved published or sealed candidate sources. It must not build or execute application code in inspection-only profiles.

Official starter and helper executable code remains subject to the applicable strict 100% Kit release coverage requirements. The application's test report identifies its own source scope and declared coverage policy separately from the Kit's own conformance evidence. A starter test cannot certify arbitrary future business behavior.

### 6.2 Application-owned tooling configuration

Generate schema-versioned `ngb.config.json` with project paths, project roles, frontend/test locations, supported test profiles and references to configuration inputs. Keep it application-owned. Validate paths and supported profile entries; do not store secrets, inline shell commands or executable manifest hooks in it.

Determine application layer roles from explicit configuration and resolved contracts rather than project-name suffixes alone. Cross-check configured projects and test suites against discovery so omitting a project or required suite cannot silently make validation pass. Unknown or unevaluated project constructs produce an actionable diagnostic or a requirement to run the build profile.

Built-in required checks are defined by the versioned tooling/profile contract; application configuration cannot silently remove them. Application-specific business acceptance suites may extend those profiles and are identified separately in reports.

---

## 7. Extend the existing `ngb.mjs` — no second CLI

The repository's `ngb.mjs` and the generated application's `infrastructure/ngb.mjs` must remain the **single public command family** and share their implementation consistent with the existing delegation design. Extract helpers/modules under the existing CLI implementation only where responsibilities and tests justify it. Avoid a second `ngb-ai` binary, a parallel extension CLI, or duplicate command parsing.

### 7.1 Maintain existing public operations

Preserve semantics of:

- `create` using published or sealed local packages.
- `start` for initial local composition.
- `upgrade` with preview and explicit `--apply`, isolated build validation, unchanged user source, preserved lockfiles/configuration and supported version transitions.
- `deploy --backup-confirmed` with controlled migrations, no destructive volume manipulation.

Keep currently documented flags, return codes, package integrity validation and unsupported-operation failures intact.

### 7.2 New command contracts

```bash
# Deterministic environment and package diagnostics
node infrastructure/ngb.mjs doctor [--format json]

# Inspection-only architecture, metadata and configuration checks
node infrastructure/ngb.mjs validate [--format json]

# Restore/build and inspection of the resulting public contracts
node infrastructure/ngb.mjs validate --profile build [--format json]

# Backend and frontend unit tests by default
node infrastructure/ngb.mjs test [--profile unit|integration|e2e|all] [--format json]

# Comprehensive independent-application conformance with precise profiles
node infrastructure/ngb.mjs certify --profile external-application [--format json]

# Installed AI Kit health and update status
node infrastructure/ngb.mjs ai status [--format json]

# Read-only preview; application files must remain unchanged
node infrastructure/ngb.mjs ai sync [--to <version>] [--format json]

# Explicit managed update; fails closed on conflicts
node infrastructure/ngb.mjs ai sync [--to <version>] --apply [--resolutions <file>] [--format json]
```

These are **new v3.4.0 contracts**, not pre-existing v3.3.0 commands. Specify precise option syntax, stable JSON report schema, exit status semantics, help, and CLI documentation. Do not introduce an unconditional `create-document`/`create-report` code generator merely because a skill exists; skills guide agents to use the SDK, and CLI provides deterministic checks and lifecycle operations.

The `upgrade` command also accepts `--resolutions <file>` for the coordinated update described in section 8. The `ai sync` target defaults to the installed Kit version; omitting `--to` must never select the latest release.

### 7.3 CLI design requirements

- Run on Windows, macOS and Linux without requiring Bash for generated external-app functionality. Invoke backend/frontend tools via cross-platform process APIs; preserve existing repository Bash quality entry points where they are already authoritative.
- Node runtime version follows the supported template toolchain (v3.3.0 docs currently specify Node 24.x and .NET 10; verify target 3.4.0 requirements).
- Use typed schema validation and actionable diagnostics. **Fail closed** on unsafe/suspicious input.
- Use shell-free child processes with argument vectors (no user-controlled shell concatenation); streaming logs and cancellation propagation with process-tree cleanup.
- Bound runtime, output memory, input sizes, recursion and tool concurrency; do not read/serialize huge source trees eagerly.
- Support deterministic text and `--format json` without conflating diagnostic chatter with structured stdout. Redact secrets and tokens; logs may contain paths/step IDs, not credentials.
- Provide stable per-check IDs, severities, file locations where safe, source/SDK/kit identity, elapsed duration and independent evidence artifacts.
- Use explicit nonzero exit codes for validation/test/certification failure, inconsistent versions and conflict. Document meanings.
- Detect unavailable services/toolchains and **report not-run** instead of claiming green; required gates fail if not run.
- Use existing integrations (dotnet, npm, Docker, Testcontainers, Playwright, k6) instead of custom in-process simulators.
- No unbounded parallel builds, no hidden background work, no automatic production mutations, no fallback from failed checks to misleading success.
- Runtime MCP remains read-only and separate; do not route developers' filesystem/build commands through the production MCP endpoint.

### 7.4 What each command should truly verify

| Command/profile | Required behavior | Execution and side effects |
| --- | --- | --- |
| `doctor` | Check SDK/Kit/CLI alignment, installed tools, approved restore sources, locked package identities, asset presence and profile prerequisites | Read project data and query trusted tool versions/service health; do not restore, build or execute application code |
| `validate` | Inspect dependency declarations, configured layer roles, manifests, file hashes, metadata/configuration schemas and available public-contract information | No restore, MSBuild evaluation, npm scripts, application assembly execution, startup or database access |
| `validate --profile build` | Restore/build with declared package sources and inspect the resolved dependency graph, public APIs/exports and resulting artifacts | Executes project build tooling; writes documented build outputs/caches; no application startup or business writes |
| `test` / `test --profile unit` | Run backend and frontend unit suites, report discovery and applicable coverage | Executes tests and their prerequisite build steps; no shared infrastructure provisioned by default |
| `test --profile integration` | Verify actual startup/DI, migration execution, service integration and backend authorization | Execute against disposable test infrastructure with test-only credentials |
| `test --profile e2e` | Run declared browser workflows and permission-sensitive UI scenarios | Execute against a disposable application deployment |
| `test --profile all` | Run all mandatory unit, integration and E2E suites for the application | Preserve per-suite outcomes; missing required suites or prerequisites fail |
| `certify --profile external-application` | Run the required validation, test and applicable performance checks for the versioned external-app profile | Provision disposable resources and bind evidence to exact source/package identities; enumerate tested business scenarios |
| `ai status` | Check versions, ownership, hashes, generated outputs and customization state | Inspection only; no latest-version discovery by default |
| `ai sync` | Plan regeneration/reconciliation for the installed SDK/Kit, including custom skill adapters | Read-only for the application; required artifact/baseline acquisition uses an external cache |
| `ai sync --apply` | Apply the validated file plan and explicit conflict resolutions | Write only owned files/blocks and recovery state; do not execute application code or touch running services |

Declare required checks, prerequisites, output locations and performance workloads in each versioned profile. Add focused performance profiles where needed; keep each Kit conformance profile complete. Docker, browser or k6 requirements apply to the profiles that need them rather than every local inspection or unit-test command.

Available files alone may be insufficient for a sound static conclusion. Mark such checks `not-run` with their missing prerequisite or required execution profile; never infer success from unavailable resolved metadata. Startup/DI construction belongs to integration/certification, since it executes application code. No magic N+1 detector based solely on string searches.

Use check states `passed`, `failed`, `not-run` and `not-applicable`. A required `failed` or `not-run` check makes the requested profile fail. `not-applicable` is allowed only for an explicitly optional feature whose absence is verified, with a recorded reason. Reports distinguish platform conformance from the application's declared business acceptance and must not certify untested business behavior.

### 7.5 One implementation, template and npm delivery

Publish `@ngbplatform/cli` with a `ngb` executable that invokes the existing shared CLI implementation. The repository wrapper, generated application's `infrastructure/ngb.mjs` and npm wrapper delegate to that implementation. Include all required configuration and validation assets; never depend on their paths in an NGB checkout.

Build the template and npm payloads deterministically from the same sources. Verify shared module/asset identity and command behavior across all entry points. Entry-point-specific path resolution must not create divergent parsers, command semantics or independently maintained implementations.

Treat the CLI tarball as a first-class release artifact: exact version, immutable candidate manifest, hashes/integrity, provenance, registry verification and source-free consumer tests. Publish and promote it with the matching SDK, UI and template; include sealed local-candidate operation before publication. Do not introduce the discarded `ngb-tooling` template or require global CLI installation.

---

## 8. AI Dev Kit versioning, sync, and platform upgrades (mandatory)

This is part of **v3.4.0**, not deferred to another release.

### 8.1 Version ownership and trust

For the first release, use exact synchronized versions:

```text
NGB Platform SDK = @ngbplatform/ui = NGB.Platform.Templates
                 = @ngbplatform/cli = AI Kit = 3.4.0
Manifest schemaVersion = 1
```

Official instruction fixes are released as the next platform patch version; published packages are immutable. Application-owned instructions and skills can change without a platform release. The schema version evolves separately from product versions. Do not introduce open compatibility ranges or an independently drifting Kit update channel in v3.4.

Define `ai/kit.json` containing:

- Exact AI Kit, NGB Platform SDK, UI, template and CLI versions and identities.
- Canonical file inventory with path, role, ownership (managed/user-owned), integrity hash, and baseline for merge.
- Public capability registry version/hash and expected CLI/tooling version.
- Immutable source/candidate/package provenance and compatibility information.
- Manifest schemaVersion and migration rules, with explicit handling of unknown versions.

Prefer bundling approved target kit artifacts into the existing sealed release candidate / NuGet template provenance instead of fetching arbitrary unauthenticated Markdown from network/GitHub. Verify both artifact identities and per-file hashes. Do not use untrusted remote scripts or arbitrary executable hooks to apply updates.

Treat `ai/vendor/`, generated agent adapters and the CLI payload as official managed assets. Treat `ai/custom/`, application code/tests, `ngb.config.json` and root instruction text outside the marked blocks as application-owned. Generated `app-*` adapters are managed outputs derived from application-owned custom skill sources; unrelated files in `.agents/` and `.claude/` remain outside Kit ownership.

The local manifest is not an authority to expand the write set. Compare permitted paths, roles and operations with the verified official package and the validated custom-source mapping. Reject attempts to claim unrelated files, `ai/custom/` sources, credentials or arbitrary project files as official managed assets.

Keep original baseline bytes, not only hashes. Baselines come from the exact verified source package and may be retained in an immutable external cache or managed recovery storage. After a clone, reacquire missing baselines from the exact package; if the package is unavailable, report the missing prerequisite and do not approximate a three-way merge. Existing installed guidance remains usable offline; an upgrade requiring uncached artifacts explicitly requires their acquisition.

Official text assets use declared UTF-8/LF rules, enforced by packaging and scoped Git attributes in generated applications. They are application-name independent and excluded from template substitution. Hash the documented canonical bytes and verify rendered adapters/managed blocks against deterministic rendering rules. Preserve application-owned text and its line endings outside managed blocks.

Store complete package hashes in the external release provenance record. Do not embed a package's own hash inside the bytes being hashed or create a self-referential manifest digest. The installed manifest records package/version/source identity and file-level verification data; the trusted external record binds the full package and manifest payload identities.

### 8.2 Two related, distinct operations

**A. SDK + application upgrade** (`ngb.mjs upgrade --to 3.4.0`): coordinated NGB NuGet/npm package version change, dependency lockfiles, compatible bundled CLI/helper replacement, compatibility guidance, and **AI Kit installation/synchronization** where required. Preview shows the complete file/dependency plan, including CLI and Kit changes. Apply prepares and validates the target in isolation before committing the complete change set. Preserve application-owned sources/configuration and surface any required user changes. This is the normal version-change path and must prevent mixed SDK/Kit/CLI states after handled failures.

**B. AI Kit inspection/synchronization** (`ngb.mjs ai status/sync`): inspect, repair, or reconcile managed AI files **for the exact installed SDK version**. Without `--to`, use the installed Kit and regenerate custom adapters locally. An explicit different target version refuses with guidance to run the coordinated platform upgrade. Repair must respect intentional local changes/deletions and section 8.3 conflict decisions; it is not permission to overwrite them.

### 8.3 Mandatory transitions and acceptance

**v3.3.0 → v3.4.0:** existing external apps have no AI Kit. Add its instructions, skills, manifest and references, alongside exact v3.4.0 SDK/UI packages and the bundled CLI/helper update. Existing user files such as `AGENTS.md`, `CLAUDE.md`, `.agents/`, `.claude/`, `ai/` must **not** be overwritten. Preview bounded root-block insertion while preserving existing text. Other path/content collisions require explicit conflict decisions. Compare existing CLI files against the verified 3.3.0 template baseline; modified copies are not silently replaced.

**v3.4.0 → v3.5.0 and later:** use actual three-way comparison (old verified baseline, current local content, new official baseline). Test the algorithm with synthetic future-version fixtures without claiming certification of an unreleased real transition.

| File state | Required behavior |
| --- | --- |
| Local content equals the old official baseline | Install the new official content |
| Only local content changed | Preserve it and record customization |
| Both local and official content changed | Report a conflict with base/local/incoming content |
| Upstream removed a file and local content is unchanged | Remove only that verified owned file |
| Upstream removed a locally modified file | Preserve it and require a conflict decision |
| A new official path collides with existing unowned content | Report a conflict; do not adopt or overwrite it implicitly |
| A managed file was deleted locally | Preserve the deletion as an explicit local change; report broken required guidance and require a repair/merge decision as applicable |
| Application-owned source or unrelated file | Preserve it |

Do not automatically perform semantic text merges of instructions in v3.4. Disjoint line edits can still introduce contradictory guidance. Deterministic replacement/insertion of known managed blocks is allowed under their ownership rules; application text outside the blocks remains unchanged. Detect independently edited Codex/Claude outputs and report divergence instead of choosing an authoritative copy silently.

Provide conflict IDs and base/local/incoming hashes plus reviewable content. `upgrade` and `ai sync` accept a schema-validated `--resolutions <file>` containing explicit choices or references to reviewed replacement content. It is data, not executable code. Bind each decision to the source/target artifact identities and expected current path hashes; refuse stale decisions after concurrent changes. Revalidate path containment, ownership, rendered output consistency and all required checks before committing. Preserving a customization does not silently mark an unresolved required contract as passed. Never use a blanket force-overwrite mode to bypass conflicts.

Other required properties:

- Strict path containment, traversal and symlink defense, Windows normalization/case collision detection; reject foreign or malformed manifests.
- Operation locking, filesystem race detection, conflict-safe writes with staged preparation, recovery/backups, rollback after injected failures, interruption/retry recovery.
- Do not leave half-applied package+kit upgrades on handled failures; crash recovery has documented, tested semantics. Database migration is separate, with existing backup/deployment contract.
- Dry-run makes **zero** modifications, including ignored files, caches inside the app, lock directories, or downloaded files; if a read-only command needs a cache, use an external temporary location and document this.
- `--apply` requires verified target artifacts, honors user changes, and records provenance, file changes, conflicts, tested operations and unresolved manual actions.
- Treat upgrade of the **bundled CLI implementation** as part of the app/tool version problem. Use the official npm bootstrap in section 8.5 for v3.3 applications and target-version delegation thereafter. `dotnet new install` never rewrites existing application files.
- Preserve the currently supported historical transitions, source fixtures and upgrade history; add an explicit **3.3.0 → 3.4.0** registered path and frozen published 3.3.0 source fixture. Do not retroactively recertify prior states by rewriting their fixtures.

### 8.4 Every future release must document AI Kit upgrades

Update `docs/architecture/external-app-upgrades.md`, release changelog templates, release workflow checks and docs navigation so every **future** release with an AI Kit includes:

- SDK/API compatibility and exact supported source → target transition(s).
- AI Kit added/changed/removed skills, rules, references and capability mappings.
- Required old/new Kit and CLI versions, how to obtain the approved target CLI.
- Preview/apply/sync usage and conflict resolution.
- Preserved application state and migration/backup requirements.
- Certification evidence and known limitations/unsupported direct jumps.

A release must fail its gate if its required AI Kit migration documentation, manifests, or tested transition are missing. Provide both **3.3.0 → 3.4.0 migration docs now** and an enforced future release convention.

### 8.5 Target-version CLI acquisition

After the official 3.4.0 packages are published, run the following from an existing 3.3.0 application's root:

```bash
# Preview using the target version of the same CLI
npm exec --package=@ngbplatform/cli@3.4.0 -- ngb upgrade --app . --to 3.4.0

# Validate in isolation and apply the coordinated update
npm exec --package=@ngbplatform/cli@3.4.0 -- ngb upgrade --app . --to 3.4.0 --apply
```

This bootstrap requires neither an NGB checkout nor a second template. For sealed unpublished candidates, execute the verified candidate CLI payload and matching package set from an external temporary/cache directory; document and certify that acquisition path separately from registry commands.

Starting with the 3.4 launcher, normal `node infrastructure/ngb.mjs upgrade --to <exact-version>` delegates to the exact target CLI through npm acquisition when needed. Do not resolve `latest`, change the application's package manifest just to acquire the tool, or replace the running bundled tool before the transaction is prepared. Cache downloads outside the application for preview. Preserve arguments, cancellation and exit status; bound delegation and prevent recursion. Verify the invoked CLI's identity/version so a stale or shadowing executable cannot perform a transition accidentally.

Use approved official package sources, exact artifact integrity/provenance and the release's trust policy. Package version equality alone is not artifact authentication. Reject modified/corrupt artifacts and unsafe install hooks. Test registry acquisition, an already verified cache, sealed candidate operation and unavailable-artifact failure.

During migration, the target CLI legitimately differs from the installed source SDK: verify this against the registered source-to-target transition. Normal application operations require aligned installed versions. Only the coordinated apply installs the target SDK/UI/CLI/Kit combination, and handled failures restore the source combination. Database migration remains a separate deploy step.

---

## 9. Package/version compatibility and reproducibility

- Keep the existing precise `NgbPlatformVersion`/`@ngbplatform/ui` alignment, extend it to the exact CLI/Kit/template versions, and preserve the official NuGet/npm publication process, versioned public API baselines, source-mapped NuGet restores and lockfile validation.
- The external starter must not reference the NGB monorepo or the build machine's workspace aliases/private paths.
- Check exact package contents, provenance, template payload, generated file hashes and public types/exports as part of certification.
- Prepare the candidate deterministically; fail on drift between authored AI Kit and NuGet template payload.
- Include the `@ngbplatform/cli` tarball in candidate sealing, publication identity and registry-only verification; check shared payload equality with the template and fail promotion on a missing/mismatched CLI artifact.
- Prevent mixed-version `3.3`/`3.4` SDK/UI/Kit usage. Report precisely what must be upgraded instead of quietly bypassing compatibility.
- Never promise that upgrade changes a running database automatically; deploy and migrations retain existing explicit safety policy.
- External consumers must build/run with published or sealed candidate packages in an isolated directory and fresh dependency caches, without source fallback.
- Lockfiles, migration names/checksums and user-owned configuration must be preserved following existing NGB rules.

---

## 10. Architecture validation and static analysis (no layer leaks)

Extend existing architecture validation rather than replacing it. Enforce both platform and external-app reference directions. At minimum:

- Definitions/Metadata never import database drivers, HTTP hosts, NGB Runtime implementations, UI internals or provider-specific projects.
- Runtime may reference approved public contracts, Definitions, provider-neutral abstractions and provider-neutral engines such as Accounting, OperationalRegisters and ReferenceRegisters. Reject PostgreSql/Npgsql/Dapper/ASP.NET Core/application API host dependencies; do not prohibit the existing legitimate peer-engine dependencies.
- PostgreSql adapters reference provider-neutral interfaces and Definitions, not application Runtime orchestration implementations.
- API/worker/migrator compose appropriate dependencies; logic remains below hosts.
- UI may use only supported public `@ngbplatform/ui` exports, not private `ui/ngb-ui-framework/src` paths except as explicitly documented in package tooling.
- Public APIs must match resolved SDK version; use actual compilation/metadata checks, not heuristic symbol-name guesses.
- Verify DI registrations where appropriate with real startup/host construction and failure tests.
- Distinguish platform-internal integration patterns from approved external application extension points.
- Maintain explicit dependency rules for the platform graph and the external application's graph. Use configured project roles and resolved references rather than blindly applying one graph or name-suffix heuristic to both.

Use suitable .NET architecture tests/Roslyn tooling and TypeScript/npm contract checks when that is materially more reliable than grep. Respect pre-existing checks and public contracts. New validators must be **sound, testable and bounded**; suppressions require targeted, reviewed, documented reasons with no blanket exclusions.

---

## 11. Performance and resource-safety requirements

Design all new tools, validators, packagers, scans and test orchestration for large monorepos and external solutions:

- No N+1 walking of C# symbols through repeated reflection/project rebuilds; group operations and cache stable read-only metadata appropriately.
- No repeated full repository scans for each skill or capability. Index once and perform incremental/targeted validations where safe; full release gate remains exhaustive.
- No loading entire report/document datasets into memory just to generate AI reference summaries.
- Bounded concurrency, cancellation, timeouts, output sizes and process memory; backpressure for child process output.
- No excessive package restores or repeated frontend installs; reuse verified immutable artifacts/caches where allowed without weakening isolated-consumer certification.
- No unbounded data reads or in-memory shaping in generated example reports; use provider-side queries, paging/cursors and appropriate indexes consistent with NGB.
- Validate representative high-volume catalogs/documents, effects, reporting, background jobs and HTTP integration paths. Keep or extend existing k6/performance tests, baseline comparison, query-count/budget evidence and regressions.
- Measure performance and compare against existing baselines rather than declaring 'no performance issues' based on code inspection. Do not increase timeouts or remove tests to hide slowness.
- Watch plugin/agent skill metadata size and context/token overhead. Long guidance belongs in demand-loaded references, not in root instructions or frontmatter.

Implement focused performance regression probes for materially new CLI operations (cold/warm `validate`, application sync preview/apply, reference/packaging generation, candidate certification). Establish acceptable budgets from measured existing infrastructure and publish evidence.

Record workload/data sizes, concurrency, toolchain, hardware/runner class, warmup, repetitions and baseline tolerances before assessing performance. Timing measurements are variable; use a reproducible measurement protocol rather than promising deterministic wall-clock results. Keep offline tooling checks, query-count assertions, volume regressions and live load measurements distinct in the evidence. A passed diagnostic-tools suite cannot substitute for an unexecuted required load profile.

---

## 12. Security and supply-chain requirements

Threat-model adversarial application content, third-party skills or instructions, untrusted filenames, manifests, package feeds, malicious reports, model prompt injection, credential leakage and unauthorized deployment.

- Project instructions/skills are informational; **not an authorization boundary**. No agent permission changes to NGB without developer approval.
- Do not execute arbitrary shell in order to “verify” an untrusted Markdown reference. No generic eval/dynamic code execution in inspection or manifest processing. Build/test execution is confined to the explicit execution profiles below.
- The CLI executes a limited, documented set of known tools with safe argument arrays and validated working directories.
- Strict absolute/relative path checks, symlink/path traversal protection, escape/case normalization, no application-file writes outside the chosen app, no destructive deletes of developer data. Tool-owned external caches, staging and disposable test resources have separate bounded ownership rules; preview writes none of them inside the app.
- No secrets in generated kit, JSON diagnostics, model prompts, logs or certification artifacts. Verify redaction with negative tests.
- Use verified, exact official NuGet/npm/template artifact identities, integrity hashes, pinned source fixtures; keep existing release source and signature verification.
- Do not change runtime MCP's read-only/current-user permissions or grant background agent privileges.
- Deployment/release requires explicit user/CI authorization and existing backup confirmation. `ai sync` and validators must never call production write APIs.
- Consider dependency/license review, package audit, SBOM/provenance hooks if the existing release pipeline supports them; do not introduce brittle or unauthorized external downloads.
- Avoid implicit telemetry transmitting user source or business data. Any optional model-based evaluation needs explicit provider configuration, safe test fixtures and no live customer data.

### 12.1 Inspection and execution trust boundaries

`doctor`, default `validate`, `ai status` and upgrade/sync preview must not run project build targets, package lifecycle scripts, application tests or application assembly initialization. Inspection may query trusted installed tool versions and run the verified tooling's data-only checks. If a resolved artifact is missing, report it instead of implicitly restoring/building the project.

`validate --profile build`, `test`, `certify` and `upgrade --apply` explicitly execute project tooling/code as required by their documented contract. Their invocation authorizes those documented development actions without redundant per-step prompts; it does not authorize deployment, publication or access to production data. `ai sync --apply` performs the controlled file operation and never runs application code.

Shell-free argument vectors and executable allowlists protect command construction; they do not sandbox MSBuild targets, npm scripts or test code. Local execution assumes the developer trusts the project. Evaluate untrusted external projects on disposable CI runners/VMs without production credentials or access to developer home directories. Do not present ordinary container use or an environment-variable filter as a complete sandbox.

### 12.2 Disposable integration and certification resources

- Integration, E2E and certification provision their own databases, containers, Keycloak realm and short-lived credentials, identified by a unique run ID.
- Generate test-only configuration. Do not inherit the working application's `.env`, production connection strings, existing database names or publication/deployment credentials.
- Pass a documented minimum environment to child processes rather than forwarding all of `process.env`; include platform-specific runtime requirements explicitly and verify secret filtering.
- Scope teardown to positively identified resources owned by that run. Never perform blanket volume deletion or attach existing application volumes as test storage. Preserve ownership records for controlled cleanup after interruption.
- Keep the resource-controlling harness separate from the application under test. Do not expose controller credentials, host home directories or orchestration sockets to application containers merely for convenience.
- Redact generated test secrets from stdout, JSON and saved evidence. Negative tests verify attempted connection reuse, leaked environment variables and cleanup of resources owned by another run are refused.
- Production deployment retains the existing separate authorization and `--backup-confirmed` contract. No validation/sync/certification profile may redirect its writes to a running production application.

### 12.3 Data-only update instructions

Kit manifests, `ngb.config.json` and conflict-resolution files are schema-validated data. They cannot supply arbitrary executable hooks. Validate all referenced paths and file sizes before reading/writing; local manifests cannot expand official ownership. Apply only decisions bound to verified package identities and expected file hashes, and recheck the write set immediately before commit.

---

## 13. Production-grade test strategy — **100% mandatory**

**This is a Kit release blocker.** Verify all applicable executable Kit, starter/helper and independent reference-application code. Declare the complete source inventory and do not claim 100% based on a subset of new files. The Kit's test discovery must not enumerate a fixed set of existing consuming products.

### 13.1 Kit, starter and reference-application coverage

Backend gate: **100% lines, 100% branches, 100% methods**, per-file completeness for all applicable production C# code in the Kit helpers, official starter and independent reference applications. Frontend gate: **100% lines, 100% branches, 100% functions, 100% statements**, per-file completeness for the applicable starter and independent reference-application frontend code. Existing generated-only exclusions may remain if auditable. New production files must not disappear from project/workspace discovery or coverage reports.

Provide documented, reproducible Kit quality entry points under `quality/ai-devkit/` and include their results in external release orchestration. Reuse the established coverage verification mechanisms where applicable without embedding the repository's consumer-project inventory in the Kit. The Kit's conformance suite must run with published or sealed candidate platform packages and independently authored fixtures.

Also test application template/CLI/scripts/validation helpers comprehensively with **100% executable-helper coverage where the repository's tooling policy requires it**, deterministic process tests, failure injection and end-to-end behavior. For instrumented paths, no untested branches or error paths.

### 13.2 Required layers of testing

1. **Unit/contract:** every parser, manifest validator, scanner, normalizer, planner, hash/provenance verifier, render adapter, conflict detector and CLI argument combination; valid and invalid cases.
2. **Architecture:** dependency direction, prohibited imports, public extension points, host composition and assembly boundaries; intentionally violating fixture must fail.
3. **Template/package:** published/sealed package install, `dotnet new ngb`, correct name substitution, hidden files, manifests, no secrets, correct starter compilation and UI; cross-platform path behavior.
4. **Public reference examples:** exact 3.4.0 packages, source-free external consumer restore/build, real API/DB/UI as needed, verified type signatures; negative compile/API drift tests.
5. **Skills:** metadata/schema/frontmatter, links, relative references, per-agent discovery, lazy loading, task-to-skill matching, stale/uninstalled/missing skill failures.
6. **External application integration:** DI registration, migration execution/repeatability, actual Keycloak auth, DB storage, posting and effects, report rendering/paging, permissions, Work Center, content/MCP/AI composition where configured.
7. **Frontend:** typecheck, public entry points, unit, browser/component and Playwright E2E (Chromium/Firefox/WebKit where supported), accessibility, UI loading/error states, desktop/mobile interactions.
8. **Upgrade/sync:** from frozen v3.3.0 application, brand-new Kit install, dependency/kit coordination; later-version synthetic fixtures for three-way merge; unchanged/edited/conflicting/untracked/symlink/case-collision files, partial failure, concurrent modification, abrupt interruption, retry, preservation of state.
9. **Security:** invalid input, malicious filenames, symlink and path traversal, shell injection, unauthorized tools, leaked env variables, corrupted/hash-mismatched candidate, missing/disabled feature flags.
10. **Performance:** bounded scans and subprocesses, reproducible timing measurements and query-count profiles, N+1 avoidance, memory/concurrency/timeout/cancellation probes, no regression in Kit tooling and independent public-SDK workloads.
11. **End-to-end acceptance:** actual independent application from empty template; complex custom module; upgrade of a persisted independent application, using only public SDK/UI contracts.
12. **Negative gates:** missing source file, public API drift, broken skill link, missing capability, unsupported version, migration failure, incorrect permission, stale locked package, skipped E2E suite, missing runtime dependency must all fail visibly.

### 13.3 Independent business acceptance — no self-affirming tests

Coverage alone does not prove business correctness. Define **before or independently from generated implementations** expected business outcomes and negative invariants. Examples include:

- Document can save an allowed incomplete Draft but cannot Post if domain invariants fail.
- A Post without any Accounting/OR/RR handler is rejected with the accepted SDK
  limitation, preserving the Draft and leaving effects unchanged.
- A valid document with an explicitly registered handler that emits no records
  becomes Posted with empty Accounting/OR/RR effects through the standard lifecycle.
- A posting with effects writes consistent append-only transaction results; retries/idempotency do not duplicate effects.
- Reversal/correction preserves history via appropriate storno/adjustment semantics.
- Authorization is checked in the backend, including action availability, reports, notes, attachments and MCP tools.
- Work Center outbox-driven tasks/notifications do not duplicate across retries.
- Reports produce known correct totals/rows and enforce paging/snapshot limitations.
- Upgrading preserves existing customer data, permissions, audit, attachments/notes and completed/pending jobs.

Tests authored by the same agent should not be the only oracle. Use frozen expected outcomes and independent verifier fixtures.

### 13.4 Test evidence policy

- Treat test skips, missing browser binaries, unavailable Docker or unsupported OS as **not executed**, not passed. Required candidate gates cannot be green without evidence.
- Fail CI on missing test discovery/coverage source entries, partial reports, lower thresholds, source-filter tampering, coverage report corruption or only partial suite execution.
- Preserve successful existing tests; do not remove/rewrite assertions just to satisfy a new implementation.
- Do not merge/publish with known red gates. If the execution environment is unavailable, report precisely what was not run; do **not** claim full certification.
- Evidence should include exact commit, SDK/Kit/tool version, checked profile, command, exit code, test counts, coverage summary, timestamps and artifact/report paths.

Bind evidence to the exact source tree and package set, not only a commit label: include candidate manifest identity, source digest, resolved dependency/lockfile identities, test/validator versions, declared profile and actual execution options. Check the expected suite inventory, discovered tests, skips, coverage source inventory and report integrity. A zero exit code alone is insufficient.

Kit certification must require bound coverage/E2E evidence for its declared source and suite inventories and reject skipped mandatory suites. Retain useful local partial profiles, but never promote their reports to complete Kit conformance evidence. External release orchestration determines the additional repository-wide evidence required for platform promotion.

Reusable evidence is valid only while its declared inputs remain identical. A changed source, package, required check, profile or relevant evaluation input invalidates that evidence. Associate each result with its release stage (candidate, publication or registry promotion); do not report fully certified publication while required later-stage checks are pending.

---

## 14. Kit and independent-consumer certification

Extend `quality/external-consumer/matrix.json` and its orchestrator, preserving existing certification and release/provenance gates. Add **mandatory blocking AI Kit gates** analogous to the existing MCP gates (actual IDs can be adjusted, but each concern must be covered):

| Gate | Minimum evidence |
|---|---|
| `ai-kit-inventory` | Exact public capability registry coverage and drift detection |
| `ai-kit-instructions-skills` | Correct AGENTS/CLAUDE, all skills, links, schema, agent discovery |
| `ai-kit-template` | Exact packed template produces fully bootstrapped external app and full kit |
| `ai-kit-public-contracts` | Every compiling example and reference uses exact public SDK packages |
| `ai-kit-cli` | All commands/profiles, diagnostics, negative paths, shared implementation across repository/template/npm entry points, cross-platform behavior |
| `ai-kit-isolated-consumer` | Fresh package-only app builds, launches and passes supported validations |
| `ai-kit-custom-application` | Independent full-stack Documents/Catalogs/OR/RR/Accounting/Reports/UI/permissions acceptance fixture |
| `ai-kit-upgrade` | Frozen 3.3.0 app → 3.4.0 through npm bootstrap, later target delegation, SDK/CLI/Kit coordination, data/metadata/user changes preserved, conflict resolution and recovery |
| `ai-kit-security` | adversarial paths/manifests/tools, secret redaction, no unauthorized writes |
| `ai-kit-performance` | measured regressions/bounds for tooling and representative business paths |
| `ai-kit-release-identity` | Published/candidate kit hashes, template, manifests, CLI tarball and NuGet/npm identity match |

Require three orthogonal consumer profiles:

1. **Empty external application:** template-only bootstrap, public SDK, correct kit, CLI checks and admin startup.
2. **Custom complex application:** fresh independent app with complete example business implementation including multiple effect patterns, UI, reports, permissions, background processes, integrations and objective acceptance.
3. **Existing application/upgrade:** genuinely persisted application at published 3.3.0, upgraded to 3.4.0 using the registered transition, with source/DB/permissions/history/jobs/attachments preserved and Kit installed without user-file damage.

Use real provider/DB integrations in these independent fixtures; do not certify mocks as real data migrations. Existing product-host regression suites belong to external release verification and are not dependencies of these Kit profiles.

### Agent evaluations: important but not a fragile artifact publication dependency

Test at least **Codex and Claude Code** on a versioned set of representative prompts, recording model, client, task, expected artifacts, agent-produced diff, cost/time where safe, independent acceptance and reasons for failure. Include skill discovery and full application workflows; ensure IDE neutrality by including terminal-based execution. Avoid snapshotting exact generated text as the acceptance standard.

Provide a **deterministic mandatory contract/conformance suite** which runs without an LLM and verifies package contents, discovery contracts, examples and validators. This suite alone does not prove an agent will select or correctly apply a skill. Real-agent evaluation is also required for release readiness under a declared environment/profile, with recorded evidence, while avoiding a fresh remote-model response as a requirement for every repeated packaging operation. If required agent evaluation has not been performed and valid bound evidence is unavailable, report that explicitly and do not claim readiness.

Before running evaluations, freeze task IDs/prompts, fixture inputs, independent expected outcomes, permitted human assistance, attempt counts, time/cost bounds and pass criteria. Include skill discovery, new functionality, changes to existing functionality, permission corrections, report extensions and customized-Kit upgrades. Critical security and architecture invariants must not be bypassed by averaging successful tasks against failed ones.

Record client/model/provider identities, configuration, input Kit/SDK/package hashes, produced diff and independent acceptance results. Evaluate terminal execution and the client/OS combinations promised by the compatibility matrix. Do not expect identical model text or diffs across attempts.

Saved evaluation evidence may satisfy the release gate only when its declared relevant inputs match the candidate. Changes to skills, instructions, examples, adapters, SDK contracts or client profiles invalidate the affected evaluations; define and verify that dependency mapping rather than reusing an unrelated report. Keep deterministic package checks and real-agent task results separate in the release report.

---

## 15. Documentation and developer experience

Deliver high-quality public documentation in `docs/` and appropriate generated app `README.md` content:

- Product overview and architecture: what AI Kit is/is not; how template, skills, CLI and SDK fit together.
- Getting started with `dotnet new ngb -n ExampleApplication`, and alternative `ngb.mjs create`; IDE-agnostic usage in terminal, Rider, Visual Studio and VS Code.
- Supported Codex and Claude Code installation/discovery semantics with realistic examples; support for additional agent clients without claiming features they do not implement.
- Complete public capability matrix, skill catalog, links to supported SDK extensions and examples.
- Precise CLI reference with command semantics, all flags, JSON schemas, exit codes and safety requirements.
- Architecture/layer conventions, append-only business semantics, effect-engine independence, permissions and performance patterns.
- Full verification guidance: build/unit/integration/E2E/coverage/performance/CI and limits of static vs business correctness checks.
- External app v3.3.0 → v3.4.0 migration with first-time AI Kit installation, target CLI acquisition, preview/apply, backup/conflicts/data preservation.
- Exact npm CLI bootstrap, target-version delegation, candidate-package operation and shared implementation identity; no `ngb-tooling` template.
- Future AI Kit synchronization and release versioning policy, including 3-way merge behavior and how modified skills survive upgrade.
- Official/application ownership, managed root blocks, `ai/custom/` authoring, dual-agent rendering, reviewable conflict resolutions and interruption recovery.
- Starter test projects, `ngb.config.json`, inspection/build/test/certification profiles, disposable resources and the limits of each result.
- Troubleshooting/FAQ for agents not finding skills, mismatched SDK/Kit versions, stale reference files, package-only builds, Docker/test prerequisites and permission failures.
- Security: no secrets in prompt/files/logs, trusted packages, explicit execution/release approval and separation from runtime MCP.

Keep existing user guides and docs nav accurate; do not replace source-anchored documentation with unverified AI prose. Update release/changelog/history/version references and published artifact metadata consistently to **3.4.0 when implementing the actual release candidate**, without rewriting historical fixtures or compatibility baselines.

### Example developer flow (target outcome, not existing v3.3 commands)

```bash
# IDE-agnostic bootstrap
dotnet new install NGB.Platform.Templates@3.4.0
dotnet new ngb -n ExampleApplication
cd ExampleApplication

# The AI kit is already installed by the template:
#   AGENTS.md, CLAUDE.md, .agents/skills, .claude/skills, ai/*

# Human or AI agent can use the same tools
node infrastructure/ngb.mjs doctor
node infrastructure/ngb.mjs validate
node infrastructure/ngb.mjs validate --profile build
node infrastructure/ngb.mjs test
node infrastructure/ngb.mjs test --profile all
node infrastructure/ngb.mjs certify --profile external-application

# Configure private local-development credentials, then start (existing commands)
node infrastructure/configure.mjs administrator@example.com
node infrastructure/ngb.mjs start
```

The commands above illustrate separate development profiles; certification may reuse valid evidence with matching inputs rather than rerunning unchanged suites unnecessarily. Integration/E2E/certification use their own disposable configuration. `start` uses the separately initialized local-development configuration; plain `dotnet new` does not initialize credentials, and test provisioning must not create or reuse the application's `.env`.

---

## 16. Concrete implementation approach and sequencing

This is **one v3.4.0 deliverable**, not an excuse to defer public capabilities to another minor release. Work in dependency order to minimize design mistakes:

**Phase A — inventory and first external workflow:** Verify the actual baseline, public surfaces, extension contracts, package compatibility and quality pipeline. Start the capability registry and implement one small package-only external fixture before mass authoring skills/references: an equipment catalog and equipment-inspection document, with an allowed incomplete Draft, required posting fields and successful Post without movements using an explicitly registered workflow handler. Separately verify that omitting all handlers rejects Post, preserves Draft state and creates no effects, following section 4.1. Include migration, backend permissions, UI, unit/integration/E2E tests and the first CLI checks. Define expected outcomes independently of its implementation. Use existing supported services; report any additional platform business-layer gap for a separate scope decision. Keep this fixture separate from the empty starter.

**Phase B — canonical content and preparation:** Implement single authored skills/references/instructions source, manifests, deterministic packaging/rendering and schema/links/example checks. Include agent-specific adapters and platform-version pinning.

**Phase C — generated template and development tests:** Integrate Kit, ownership blocks, custom sources, configuration and executable test projects into the official template. Verify manual `dotnet new` and `ngb.mjs create`, Windows/macOS/Linux behavior and isolated consumer builds.

**Phase D — CLI and npm delivery:** Complete doctor/validate/test/certify/AI status+sync, the documented profiles, internal validation helper, stable JSON, safe paths and bounded execution. Package the same implementation as the template CLI and `@ngbplatform/cli`; verify payload parity and candidate identity without duplicating command implementations. Publication remains a separately authorized release action.

**Phase E — upgrades:** Implement coherent `3.3.0 → 3.4.0` SDK/UI/CLI/AI Kit migration, verified baseline bytes, explicit conflict-resolution data, npm bootstrap/target delegation and operation locking. Test customized instructions, divergent generated adapters, hostile paths, stale resolution plans and interruption/recovery.

**Phase F — complete verified examples:** Populate **all** covered public capability groups, compiling external fixtures, objective domain acceptance and cross-platform documentation.

**Phase G — quality and release:** Complete the Kit's 100% coverage gates, independent-consumer profiles, package identity checks, performance/security probes and frozen agent-evaluation protocol. Expose their results to external release orchestration without importing its consuming-product inventory. Bind complete Kit evidence to the exact candidate and release stage, resolve failures and report reproducible proof.

Avoid premature new abstraction layers; favor focused small modules with explicit contracts and direct tests. Do not create an interface/factory hierarchy for one implementation unless extension requirements or existing patterns justify it.

---

## 17. Definition of Done — hard acceptance checklist

A v3.4.0 implementation is complete **only if every item below is demonstrably true**:

- [ ] Full inventory of supported public NGB capabilities is enforced and complete; no undocumented or unvalidated capability gaps.
- [ ] Instructions, skills, references, registry, validators and conformance profiles are independent of existing products; examples compile and run using only public SDK/UI contracts.
- [ ] Existing `ngb.mjs` remains the single public CLI family, with all old commands preserved and production-ready new commands.
- [ ] `NGB.Platform.Templates@3.4.0` installs a full version-accurate AI Kit automatically through `dotnet new ngb` on an independent filesystem.
- [ ] `@ngbplatform/cli@3.4.0` distributes the same shared CLI; npm bootstrap and target delegation work without an NGB checkout or a separate tooling template.
- [ ] Official and application-owned instructions are separated, root blocks are bounded, and custom skills render into both supported agent distributions without overwriting user edits.
- [ ] The starter includes working backend, integration, frontend and E2E tests, application-owned configuration and package-only validation helpers.
- [ ] Codex and Claude Code correctly load intended project instructions and discover appropriate skills; no duplicated manually maintained canonicals.
- [ ] Every public capability has versioned guidance, verified contract references, applicable acceptance and/or independently buildable example.
- [ ] Generated examples use only supported public NuGet/npm APIs; no source fallback or private UI imports.
- [ ] Architecture tests prove no forbidden layer dependencies or DI/hosting leaks.
- [ ] New `doctor`, `validate`, `test`, `certify`, `ai status`, `ai sync` commands provide consistent behavior, safe errors, predictable exit codes and machine-readable reports.
- [ ] Full v3.3.0 → v3.4.0 coordinated application+kit upgrade is implemented, documented and tested against a genuinely frozen published 3.3.0 application fixture.
- [ ] Future kit synchronization handles managed, customized, conflicting, deleted, corrupt and interrupted states without data loss.
- [ ] Hash-bound conflict resolutions, verified baseline recovery and atomic SDK/UI/CLI/Kit coordination pass failure-injection and concurrent-change tests.
- [ ] Default `dotnet new`, root `ngb.mjs create`, sealed-candidate and registry-only installs produce identical supported kit semantics.
- [ ] No security regressions, unsafe shell paths, credential leaks, untrusted supply-chain fallback or runtime MCP write expansion.
- [ ] Inspection commands never execute application code; integration/certification resources and credentials are disposable, isolated from application deployments and cleaned only by run ownership.
- [ ] No material performance regressions; measurements and enforced bounds provided for new tooling and representative workloads.
- [ ] Real integration/E2E functional acceptance passes for empty starter, complex independent application and upgraded persistent independent app.
- [ ] All applicable Kit, starter/helper and independent reference-application backend and frontend source inventories pass at **100%** of required coverage metrics, using strict per-file verifiers.
- [ ] Node/tooling tests, architecture contracts, template/package tests, negative tests and failure-injection tests pass and preserve their required 100% coverage discipline.
- [ ] All mandatory release certification gates, compatibility checks, package identity checks and pre/post-publication gates pass where applicable to the release stage.
- [ ] Release evidence proves every required suite ran against the identified source/packages, rejects skipped gates, and includes valid bound Codex/Claude evaluations under the declared protocol.
- [ ] Every required new doc, CLI help entry, generated README, supported transition guide and v3.4.0 release note section is included.
- [ ] All actual changes are reviewed for clean layering, maintainability, cross-platform behavior, security, compatibility and performance.

**Never weaken gates, hide test failures or report unexecuted checks as passed.** If environment restrictions prevent a mandatory test, describe the blocker precisely and provide evidence of everything actually run, but do not pronounce the release production-ready or fully certified.

---

## 18. Required Codex execution / delivery report

Work directly in the current repository checkout on an appropriate feature branch; follow existing contributor/release conventions. Start by inspecting and summarizing the verified architecture **in your own internal implementation notes**, then implement without stopping at a plan. Build incrementally, run tests throughout, and remediate regressions. Use an available local test environment and avoid changing production data or pushing/publishing releases unless explicitly authorized.

Before editing production code, tests, project files or configuration, inspect applicable repository instructions, formatter settings and nearby established files. Match their readable formatting and organization. Review the final diff for style consistency and keep unrelated user changes intact. Follow the repository's PostgreSQL MCP requirement for schema inspection, database exploration and safe read-only SQL. This implementation workflow does not make a database MCP connector a mandatory dependency of generated external applications.

When finished, provide a concise, evidence-backed report containing:

1. **Implementation summary:** what was added/changed and why; new paths/projects/commands.
2. **Architectural integrity:** layer/dependency analysis and assurance that runtime MCP remains unchanged/read-only.
3. **Capability coverage:** audited inventory, evidence counts and links to verification artifacts; explicit unsupported public operations, if any.
4. **Developer workflow:** actual commands and generated file structure from a fresh template.
5. **Upgrade behavior:** v3.3 → v3.4 and customized-kit conflict/recovery scenarios with evidence.
6. **Quality:** exact backend/frontend coverage values and source inventories for the Kit, starter/helpers and independent reference applications; tooling coverage; test counts and failed/skipped checks.
7. **Certification:** each relevant mandatory gate/profile, outcome and reproducible artifact IDs.
8. **Performance/security:** profiles run, measured results, regressions and remediation.
9. **File changes and commits:** reviewable diff/commit list and any known limitations.
10. **Release readiness:** identify the assessed release stage and give a binary conclusion **READY** only if every required check for that stage actually passed; otherwise **NOT READY**, with actionable specific blockers. List outstanding later-stage publication/promotion checks explicitly; do not equate candidate readiness with a fully verified published release.

The delivery report covers Kit evidence. Overall platform promotion is decided by external release orchestration using its own additional regression requirements.

**Success criterion:** An independent developer, using any suitable IDE or terminal and an agent such as Codex/Claude, can create a brand-new NGB business application using only officially distributed v3.4.0 packages, local AI Kit content, the existing CLI, and documented public extension points; the resulting application can be objectively built, tested, reviewed, upgraded and released without needing private NGB source access.

---

## 19. Authoritative project references and agent-guidance standards

Project baseline (inspect actual current code before implementation):

- https://github.com/ngbplatform/NGB/releases/tag/v3.3.0
- https://github.com/ngbplatform/NGB/blob/v3.3.0/docs/architecture/external-app-upgrades.md
- https://github.com/ngbplatform/NGB/blob/v3.3.0/docs/guides/extend-external-application.md
- https://github.com/ngbplatform/NGB/blob/v3.3.0/docs/guides/platform-extension-points.md
- https://github.com/ngbplatform/NGB/blob/v3.3.0/docs/guides/mcp-server.md
- https://github.com/ngbplatform/NGB/blob/v3.3.0/docs/guides/ai-assistant.md
- https://github.com/ngbplatform/NGB/tree/v3.3.0/packaging/templates/content
- https://github.com/ngbplatform/NGB/tree/v3.3.0/quality/external-consumer

Agent conventions (verify current compatibility at implementation time):

- OpenAI: https://developers.openai.com/blog/rethinking-skills-and-prompts-for-gpt-6-astra
- Codex project guidance and skill locations: https://learn.chatgpt.com/docs/customization/overview
- Codex skills authoring: https://learn.chatgpt.com/docs/build-skills
- Claude Code skills authoring: https://code.claude.com/docs/en/skills
- Claude Code project memory and `@AGENTS.md` imports: https://code.claude.com/docs/en/memory
- Claude Code guidance: https://claude.com/resources/articles/steering-claude-code-skills-hooks-rules-subagents-and-more

Package delivery conventions:

- npm exact-version command execution: https://docs.npmjs.com/cli/v11/commands/npm-exec/
- npm executable entry points: https://docs.npmjs.com/cli/v11/configuring-npm/package-json/#bin
- .NET template installation and `@version` syntax: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-new-install

**Final directive:** Implement NGB AI Development Kit v3.4.0 completely and correctly on the first release, following production engineering best practices current in 2026. Preserve NGB's architecture and existing compatibility/release contracts. Achieve complete supported-public-capability coverage, strict 100% coverage of applicable Kit/starter/helper/reference-application code, consumer independence and objective package-only conformance. Do not replace implementation with an impressive-looking collection of Markdown files; real correctness must be backed by compiled code, tests and release evidence.
