# NGB v3.2.0 — External App Template & Upgrade Certification
## Production Implementation Brief for Codex (GPT-6 Astra Extra High)

This document records the agreed design and acceptance requirements. It is not an
operator runbook or evidence that a release has passed. Current commands and
implementation limitations are documented in
[External applications and upgrades](../../docs/architecture/external-app-upgrades.md),
the [platform publishing runbook](../../packaging/PUBLISHING.md) and the
[certification maintainer runbook](README.md). The paths in Section 27 are a suggested
layout; the implemented gate bindings are in [matrix.json](matrix.json).

> **Target:** implement NGB v3.2.0 feature **External App Template & Upgrade Certification** as a production-ready, externally consumable application lifecycle contract for NGB.
>
> **Repository:** `ngbplatform/NGB`
>
> **Baseline:** before implementation, audit the actual repository state and select the latest supported published stable `3.1.x` release. Freeze its exact package versions, source fixture, configuration and lockfiles before changing platform code. Do not silently substitute current-source builds for published source-version packages. If implementation requires an incompatible public change, **stop and surface it explicitly before making it**. Do not silently introduce a breaking change into `3.2.0`.
>
> **Agreed decisions:** the concrete starter, upgrade procedure, mandatory certification profiles and quality scope below are implementation requirements. Section 31 is the canonical acceptance matrix; other sections define its behavior and evidence. Do not reinterpret mandatory profiles as optional or weaken acceptance through a narrower implementation.

---

# 1. Purpose

This feature is **not primarily about `dotnet new`**.

The goal is to prove and enforce that NGB is a genuinely independent, externally consumable platform rather than a framework that only works from inside its monorepo.

NGB v3.2.0 must establish and continuously verify this lifecycle:

```text
CREATE
  ↓
EXTEND
  ↓
BUILD
  ↓
RUN
  ↓
UPGRADE
  ↓
PRESERVE
```

A third-party developer must be able to:

1. create a new NGB application from an official template;
2. restore only published/candidate NGB NuGet and npm artifacts;
3. build outside the NGB source tree;
4. run with PostgreSQL + Keycloak;
5. use real platform functionality;
6. extend the application with custom definitions/runtime/persistence;
7. upgrade an existing external application to a newer NGB version;
8. preserve data, permissions, audit history, custom extensions, background processing, and runtime behavior.

The implementation must make this lifecycle **automatically testable and release-gated**.

---

# 2. Core architectural rules

Preserve the existing NGB architecture.

Do not weaken existing boundaries just to make the template easier to build.

Mandatory rules:

- .NET 10.
- PostgreSQL.
- Vue 3 + TypeScript frontend.
- Keycloak authentication.
- Use the established NGB Guid v7 convention (`Guid.CreateVersion7()` where applicable).
- Platform Runtime remains SQL-free.
- Dapper/Npgsql/SQL remain in provider/infrastructure layers.
- Definitions remain provider-neutral.
- API, Background Jobs and Migrator are composition roots, not dumping grounds for business logic.
- Platform code remains vertical-agnostic.
- No hardcoded `pm`, `trade`, `ab`, `crm`, or other vertical-specific behavior in platform libraries.
- No vertical-to-vertical dependency.
- No new infrastructure component without a concrete justification.
- No duplicate services/scripts/extension points where a correct implementation already exists.
- No environment/process-specific concerns in reusable Runtime/application layers.
- Reuse the existing migration-pack model; do not invent a second migration system.
- Preserve existing document lifecycle, Accounting, OR, RR, Audit, Security, feature-flag, hosting, and Administrator semantics.
- Do not add a new abstraction merely to make layering look cleaner.
- Do not move certification/test concerns into Core/Runtime production code.

---

# 3. No monorepo leakage

An external consumer must not depend on:

- `ProjectReference` to NGB source projects;
- npm workspace aliases into NGB source;
- `../` imports of NGB build/config files;
- root `Directory.Build.props` from the NGB repository;
- repository-local configuration implicitly inherited from the checkout;
- local package caches hiding undeclared package dependencies;
- monorepo-only shell scripts;
- shared Tailwind/PostCSS files outside the published npm package;
- unpublished source files;
- an implicit working directory inside the NGB repository;
- any NGB vertical source project.

Candidate-artifact validation must execute in an **isolated external directory/container** receiving only the artifacts and fixture/application source intentionally copied into it.

Application-owned configuration files are allowed, including a frozen local Tailwind/PostCSS configuration needed by the 3.1 source fixture. They must live inside the fixture and require no NGB checkout. This does not authorize copying platform implementations into the application or supplying missing candidate package files from repository source.

---

# 4. Mandatory pre-implementation audit

Before changing production code, inspect the actual repository.

Do not begin by inventing new abstractions.

Audit at minimum:

## Backend / packaging

- `Directory.Build.props`
- `Directory.Build.targets`
- `packaging/PUBLISHING.md`
- `packaging/nuget/*`
- all current `NGB.Platform.*` packable projects
- package dependency graph
- package validation / ApiCompat configuration
- symbol packages
- candidate package flow
- registry-only NuGet configuration
- release workflows
- host composition extensions
- migration discovery and migration packs
- Keycloak/auth integration
- authorization and Administrator semantics
- health endpoints
- structured logging
- Background Jobs host and platform jobs
- feature flags
- Attachments/Notes configuration
- current architecture/layering tests

## Frontend / npm

- `ui/ngb-ui-framework/package.json`
- generated public npm manifest
- complete `exports` map
- `src/index.ts`
- every public entry point
- CSS packaging
- public-assets packaging
- Vite public-assets plugin
- Tailwind integration
- PostCSS integration
- peer dependencies
- API-compatibility scripts
- current package-consuming applications
- workspace-only assumptions
- npm candidate packaging
- npm release workflow

## Existing external-consumer evidence

Audit CRM and other package consumers, but **do not treat an in-monorepo consumer as proof of external independence**.

Known v3.1-era issue to verify:

- CRM imported Tailwind config from `../tailwind.shared.config.js`.
- CRM imported PostCSS helper from `../postcss.shared.config.js`.
- those files were not part of the public `@ngbplatform/ui` package.

If this remains, eliminate the leak using an additive supported public frontend contract.

## Upgrade baseline

Audit:

- current 3.x API compatibility baseline;
- previous-release compatibility behavior;
- migration guides;
- current release notes;
- release-train/versioning policy;
- supported database migration behavior;
- feature flags and optional subsystems.

Record the important findings before implementation. Resolve source versions and per-package compatibility baselines once, record their exact versions and artifact identities, and prove/freeze the source fixture before changing production platform code. Establish compatibility gates before changing public contracts.

---

# 5. Deliverable A — Official `NGB.Platform.Templates`

Create an official .NET template package.

Expected UX:

```bash
dotnet new install NGB.Platform.Templates
dotnet new ngb -n MyBusinessApp
```

The exact internal layout may follow existing repository conventions, but the public experience must remain simple and stable.

## 5.1 Template goals

The generated application must be:

- independently buildable;
- independently runnable;
- based only on packaged NGB artifacts;
- minimal but complete;
- production-oriented;
- free of fake/demo business verticals;
- free of placeholder Customer/Product/Invoice entities;
- free of source-tree dependencies;
- explicit about how architecture grows when custom logic is added.

## 5.2 Default generated workload

The default starter should include:

- API host;
- Migrator;
- Background Jobs host;
- Vue web application;
- PostgreSQL;
- Keycloak.

Do **not** include by default:

- Watchdog;
- Seq;
- MinIO;
- pgAdmin;
- PM;
- Trade;
- Agency Billing;
- CRM;
- demo verticals;
- fake storage providers.

### Migrator semantics

Migrator is a one-shot operation:

```text
migrate
  ↓
exit
```

It is not a continuously running workload.

Compose may model it as a one-shot service used for sequencing, but this must remain clear operationally.

## 5.3 Template lifecycle

Pin generated NGB NuGet and npm dependencies to the exact supported release train. Record the template version used to generate an application.

Updating the installed template package affects future generation only. Existing applications upgrade their dependencies and apply documented source/configuration changes. Regenerating a template over user-owned source is not a supported upgrade procedure.

---

# 6. Starter acceptance scenarios

Fullness is determined by completed workflows, not by the number of enabled subsystems.

## Authentication and shell

Prove:

- Keycloak starts/configures successfully;
- a user can authenticate;
- the Vue application loads the NGB shell;
- navigation works;
- API authentication and authorization work;
- every visible navigation item resolves to an available, functioning capability.

Do not show links to absent verticals, Watchdog, Seq, pgAdmin or disabled optional capabilities.

## Users, roles and Administrator bootstrap

Document and test the exact first-administrator bootstrap procedure, including the Keycloak realm/client configuration required for login and the existing Keycloak Admin API integration required for user management. Reuse existing supported mechanisms without importing a vertical's demo/bootstrap behavior.

Prove all of the following in API authorization and the corresponding UI access state:

- an authenticated, active platform user with an explicitly registered application Administrator role code receives effective full access;
- the trusted Keycloak role `ngb-admin` provides administrator access without a pre-existing platform user record;
- inactive accounts remain blocked, including a linked inactive platform account with a trusted Keycloak administrator role;
- editable role display names, including `Administrator`, do not confer administrator status;
- Administrator protection semantics remain intact;
- newly registered permissions become available to active administrators without manual grants;
- ordinary users do not automatically receive newly registered permissions;
- disabled deployment feature flags remain disabled for administrators as well;
- user/role management works and existing grants survive an upgrade.

## Platform-owned end-to-end object flow

Use the existing ordinary application-role management screen and its audit history.

Required browser flow:

```text
create a nonadministrator role
  ↓
save
  ↓
reopen
  ↓
modify
  ↓
save
  ↓
verify persisted state
  ↓
open the existing audit panel and verify the changes
```

Use existing platform UI and behavior. Do not create artificial entities or UI to satisfy certification. If audit of the implementation reveals a blocker in this selected flow, report it explicitly instead of silently substituting a weaker scenario.

## Background Jobs

Use the existing `platform.schema.validate` job.

Execute it through the real Background Jobs scheduling/worker pipeline. Calling its implementation method directly from a test does not satisfy this scenario. Trigger the existing job deterministically without waiting for a nightly schedule or adding a test-only production endpoint.

Prove:

- host starts;
- job is registered;
- worker executes the job;
- result is observable using normal platform mechanisms/logging/job state;
- failure is diagnosable.

Do not create `HelloWorldJob` or add synthetic business data to make the starter job run.

## Health and logs

Without Watchdog/Seq, operators must still understand state through:

- existing health endpoints;
- structured application logs;
- actionable migration/startup/job errors.

Do not add a second observability stack.

---

# 7. Attachments and Notes in starter

## Attachments

Default:

```text
Attachments = disabled
```

API and UI must behave coherently when disabled.

There must be no visible action that inevitably fails because object storage is missing.

Document how to enable Attachments using the existing supported storage-provider integration, currently MinIO.

Do not add:

- local-filesystem storage as a fake production provider;
- in-memory storage;
- temporary provider created only for the starter.

## Notes

Notes remain independently configurable from Attachments.

Do not make Notes depend on MinIO.

Default: `Notes = disabled`, following the current feature-flag defaults. Both Notes and Attachments are therefore disabled in the minimal starter.

The mandatory Notes certification profile enables Notes without MinIO and proves preservation and continued operation across the upgrade. Feature defaults must not be changed merely to make a profile pass.

---

# 8. Generated layering model

Do **not** generate empty projects merely because they might be useful later.

A new application does not automatically need empty:

- `MyApp.Runtime`
- `MyApp.Definitions`
- `MyApp.PostgreSql`

But documentation and tests must define strict growth rules.

## First custom definition

When the app introduces its own metadata/business definition, create/use an independent application/module definition responsibility.

Do not place definitions in API.

## First custom orchestration/handler

When custom handlers, orchestration, posting logic, validation, or application services appear, create/use Runtime/application layer.

Runtime stays provider-neutral.

## First SQL / migration / provider implementation

**The first SQL statement is the point where provider-specific code belongs in the PostgreSQL/provider project.**

Do not wait until the app is “large enough”.

Provider concerns include:

- SQL;
- Dapper;
- Npgsql;
- DB-specific repository implementations;
- application DB migrations;
- DB-specific bootstrap code.

## Composition roots

API, Background Jobs, and Migrator compose modules.

They do not become owners of business implementation.

---

# 9. External architecture tests

Create tests that protect third-party application boundaries.

Reuse the intent of existing `BackendLayeringArchitectureTests`, adapted to an external consumer.

At minimum enforce:

- Runtime must not depend on the PostgreSQL provider project.
- Definitions must not depend on Runtime/persistence implementation.
- API must not own SQL/business persistence.
- Migrator must not own SQL implementation.
- provider project may depend on appropriate contracts/definitions/persistence abstractions and required provider infrastructure, including `NGB.Platform.PostgreSql`, Dapper and Npgsql; it must not depend on the application Runtime orchestration implementation.
- hosts compose modules instead of implementing business logic.
- no NGB source `ProjectReference`.
- no NGB monorepo-relative paths.
- no vertical cross-dependency.
- direct production dependencies are explicitly declared where required.

Certification tooling can enforce stricter repository-isolation rules than the generated user project itself.

---

# 10. Deliverable B — Public frontend tooling contract

External npm consumption is a first-class v3.2.0 deliverable.

The frontend must build from the **packed npm artifact**, not from workspace source.

## Tailwind

If current external consumption still requires a monorepo Tailwind file, expose a supported public entry point, for example:

```text
@ngbplatform/ui/tailwind-preset
```

The preset must preserve existing NGB design tokens/theme rules.

Do not redesign the theme.

Do not perform a Tailwind major migration unless independently required and explicitly approved.

Tailwind class discovery must correctly cover:

- the external application;
- the installed `@ngbplatform/ui` package.

No NGB source-tree-relative path.

## PostCSS

Eliminate dependence on a neighboring monorepo helper.

Use a normal explicit PostCSS configuration owned by the generated application. The current helper's responsibility does not justify adding a separate public PostCSS API.

Do not import a monorepo helper or create an abstraction without a real responsibility.

## Validate all public npm surfaces

Do not validate only the root `index.ts`.

Validate every currently supported public entry point, such as the actual equivalents of:

```text
@ngbplatform/ui
@ngbplatform/ui/contracts
@ngbplatform/ui/editor
@ngbplatform/ui/layout
@ngbplatform/ui/lazy
@ngbplatform/ui/navigation
@ngbplatform/ui/work-center
@ngbplatform/ui/styles
@ngbplatform/ui/vite-public-assets
@ngbplatform/ui/tailwind-preset
```

Use the real export map after audit.

Validate:

- export presence;
- TypeScript signatures/contracts;
- CSS;
- public assets;
- Vite integration;
- Tailwind integration;
- peer dependencies;
- production Vite build.

## Packed-artifact build

Required flow:

```text
pack candidate @ngbplatform/ui
  ↓
copy .tgz to isolated consumer
  ↓
npm install
  ↓
typecheck
  ↓
Vue compile
  ↓
Tailwind processing
  ↓
CSS output
  ↓
public-assets handling
  ↓
Vite production build
```

Forbidden:

- workspace resolution to NGB source;
- source aliases into `ui/ngb-ui-framework`;
- `../tailwind.shared.config.js`;
- `../postcss.shared.config.js`;
- hidden fallback to local workspace package.

---

# 11. Deliverable C — External clean-install certification

Create an isolated certification harness, for example:

```text
quality/external-consumer/
```

Use repository conventions if a better existing quality location exists.

## Artifact-first rule

The NGB checkout may build candidate artifacts.

The external environment should then receive only intentionally exported artifacts such as:

- `.nupkg`;
- `.snupkg` when needed;
- npm `.tgz`;
- template package.

The external application must not compile against NGB source.

## Required flow

```text
build candidate artifacts
  ↓
create isolated temp directory/container
  ↓
install template package
  ↓
dotnet new ngb
  ↓
restore from candidate/public feeds
  ↓
npm install packaged UI
  ↓
start PostgreSQL + Keycloak
  ↓
run Migrator
  ↓
start API
  ↓
start Background Jobs
  ↓
build/start web
  ↓
authenticate
  ↓
run real platform scenarios
  ↓
validate health/logs/jobs
```

## Isolation negative tests

Add tests that fail when independence is broken, for example:

- generated project references a parent/repository path;
- package misses transitive dependency;
- npm export exists in source but not `.tgz`;
- frontend expects workspace-only config;
- restore succeeds only because a local NGB feed leaked in;
- external build receives undeclared source dependency.

Do not rely only on happy-path tests.

## Extend the newly generated application

In a separate mandatory scenario, generate a fresh application from the candidate template and apply the documented technical extension example to that application.

Introduce the module's Definitions/contracts, Runtime and PostgreSQL responsibilities as described by the growth rules. Prove its custom definition, handler, provider persistence and application migration through an isolated build and runtime scenario.

Reuse the small technical module's responsibilities from the upgrade fixture where practical. Do not replace the generated host/configuration with the historical application's source, and do not add a demo business vertical to the default template.

The historical-fixture upgrade test does not substitute for this CREATE → EXTEND test.

---

# 12. Compatibility is separate from upgrade certification

Use these terms consistently.

## Compatible

Public contracts satisfy the SemVer compatibility policy of the current major line.

## Certified upgrade

A specific source-version → target-version application/database transition was actually executed and passed the certification suite.

## Supported upgrade path

A documented sequence of certified transitions and/or explicitly documented major-version migration steps.

A deliberately limited upgrade matrix **must not weaken the SemVer/API compatibility promise**.

---

# 13. NuGet compatibility gates

Historically, the 3.x line uses `3.0.0` as the major baseline.

That alone cannot detect removal of APIs first added in 3.1.

Example:

```text
3.0: Foo()
3.1: Foo() + Bar()
3.2: Foo()
```

`3.2` vs `3.0` can pass while breaking 3.1 consumers using `Bar()`.

Therefore v3.2 needs two compatibility views where applicable.

## Major-line baseline

Validate against the earliest applicable stable package of the current major line.

Purpose:

> preserve contracts promised since the beginning of the current major.

## Previous-release baseline

Also validate candidate packages against the exact previous supported stable release selected for that package. Resolve this version when freezing the release matrix; do not resolve a moving `latest` value during a certification run.

Purpose:

> prevent silent removal/incompatible changes to APIs introduced after the major baseline.

This is especially important for packages first published after 3.0.

## Per-package baseline inventory

Record each runtime package ID, its first stable version in the current major line, and its selected previous-release baseline. Use the actual first published version when a package did not exist in 3.0.

A package may omit a baseline only for its first publication. A historical `NgbPlatformHasApiBaseline=false` flag must not permanently exempt an already released package such as Attachments or Notes.

Treat `NGB.Platform.Templates` as a template-content package, separately from runtime libraries. Verify template contents, installation and generation; do not require a runtime DLL ApiCompat baseline or an obligatory `.snupkg` for that package. Update package inventories and verification rules without weakening checks for existing runtime packages.

## SemVer rules

For minor/patch releases:

- compatible additions are allowed;
- incompatible removals/changes are forbidden;
- do not add ApiCompat suppressions merely to make CI green;
- intentional incompatible public changes require a new major version, baseline update, release notes, and migration guide.

If this work reveals a genuinely required public break, do not silently implement it in 3.2.0. Surface it for explicit versioning/architecture approval.

---

# 14. npm/TypeScript compatibility gates

The current root-export-name snapshot is insufficient by itself.

## A. Package surface

Verify that every documented export and static asset is actually present in the packed npm artifact.

Protect the supported public surface:

- export names, subpath imports and export-map resolution;
- public functions, interfaces, generic constraints and overloads;
- public Vue component props, emits and slots;
- documented CSS tokens and public assets;
- peer dependencies and the documented supported toolchain versions.

## B. Type contract compatibility

Maintain a reviewable representation of the public type contracts for all public entry points and compare it against the frozen released baseline.

Prefer the TypeScript compiler/type checker or another lightweight deterministic mechanism compatible with the current toolchain. Avoid heavyweight API-analysis tooling unless necessary.

Export-name equality or a single type-assignability check is not sufficient. Account for both application consumption and implementation of public extension contracts, including Vue component contracts. Compatible additions are allowed; updating a snapshot must not silently accept a breaking change.

## C. Previous-consumer compilation

Compile the frozen previous-version external consumer against the candidate UI package. Include Vue compilation and production Vite build.

This catches practical consumer breaks but does not replace public-contract comparison across the supported surface.

## D. Negative tests of the compatibility checker

Use controlled test artifacts to prove that the checker rejects at least:

- a removed export or subpath;
- a removed public overload;
- an incompatible public type or generic constraint;
- a newly required Vue prop;
- an incompatible emitted-event payload or public slot contract;
- a removed documented CSS token or asset;
- an unsupported peer/toolchain requirement change under the declared compatibility policy.

Also prove that compatible additions pass. Keep these mutations in test artifacts, not production package contents.

---

# 15. Deliverable D — Version-neutral upgrade certification

Do not hardcode:

```text
if sourceVersion == 3.1.0 and targetVersion == 3.2.0
```

Build a reusable runner driven by a matrix/configuration containing:

- source version;
- target version;
- fixture;
- supported transition;
- source/target artifacts;
- migration instructions/hooks where necessary;
- expected invariants;
- exact per-package versions, artifact identities and compatibility baselines;
- fixture revision/hash and lockfile identities;
- fixed infrastructure versions and persistent-state locations;
- mandatory profile IDs and the assertions each profile must execute;
- documented source/configuration upgrade patch;
- maintenance-stop and recovery procedure.

Stored matrix entries must use exact versions. Reject floating versions, unresolved placeholders and an incomplete mandatory profile set. Do not silently fall back to a different source package or fixture when an artifact is unavailable.

It must be reusable for future:

```text
3.2 → 3.3
3.3 → 3.4
...
```

and for explicit major migration paths.

---

# 16. Upgrade support policy

## New minor

Certify from the **latest supported patch of the previous supported minor**, selected and frozen as an exact version during release preparation. Policy examples below describe version selection, not executable matrix values or claims of completed certification.

Example:

```text
3.1.latest → 3.2.0
```

## New patch

Certify from the previous supported patch in the same minor line.

Example:

```text
3.2.0 → 3.2.1
```

or the exact previous supported patch according to current release policy.

## Multi-minor upgrades

If:

```text
3.1.latest → 3.2.latest
3.2.latest → 3.3.0
```

are certified, the supported path is:

```text
3.1 → 3.2 → 3.3
```

Do not claim:

```text
3.1 → 3.3
```

is directly certified unless a dedicated direct certification exists.

## Major upgrades

Major upgrades use:

- explicit source-version range/matrix;
- migration guide;
- documented manual/source/config changes;
- automated validation of that documented path where feasible.

Again: limited upgrade certification does not reduce public API compatibility obligations.

## Deployment and recovery scope

v3.2.0 certifies upgrades with a maintenance stop of the old API and Background Jobs workers before target migration. Mixed-version operation, rolling upgrades and zero-downtime upgrades are not certified by this procedure.

Document recovery for each transition. Do not assume that reverting package versions reverses an applied database migration. Restore a consistent backed-up application/state set when the documented recovery requires it; validate any explicitly claimed rollback path separately.

---

# 17. Preserve a real previous-version external fixture

For v3.2.0, create and verify a small external application against the selected exact published supported 3.1 source-version packages before changing platform code.

A baseline consumer authored now against those released packages is valid; record its provenance accurately rather than claiming it existed at the historical release date. It must not be generated from the new v3.2 template.

Prove its source-version scenarios, then freeze its source, app-owned configuration, lockfiles and fixture identity. Subsequent target runs use a working copy with only the documented dependency/source/configuration upgrade patch. Preserve the original fixture and report that patch; do not rewrite business behavior or assertions to conceal a regression.

Allow a frozen application-local Tailwind/PostCSS configuration needed for the 3.1 package. It must work without the NGB checkout and must not copy platform implementation code to bypass package boundaries.

Suggested area:

```text
quality/upgrade-certification/fixtures/<exact-source-version>/
```

Use the actual supported patch chosen by the release matrix.

## Fixture purpose

The fixture is not a demo vertical.

It proves that real third-party extension points survive an upgrade.

Include a very small technical application module containing enough responsibilities to exercise:

- one custom definition/entity;
- one custom runtime handler/validator/orchestration path;
- one application migration;
- one provider-specific persistence implementation;
- custom persisted data;
- user/role/permission state;
- auditable activity;
- a deterministic background/runtime interaction that allows work to be queued before upgrade and its effect verified after upgrade.

Do not turn it into a second product.

## Correct fixture layering

Arrows below mean project dependencies, not runtime execution order:

```text
Runtime → Definitions / Contracts / persistence abstractions
PostgreSql → appropriate Definitions / Contracts / persistence abstractions
PostgreSql → NGB PostgreSql / Dapper / Npgsql as required

API / Background Jobs / Migrator → libraries required by each host
```

Hosts are composition roots. Runtime must not reference the PostgreSQL provider implementation. The provider must not reference application Runtime orchestration.

No SQL in API/Runtime/Migrator.

No provider dependency in Definitions.

No NGB source references.

---

# 18. Upgrade certification flow

Upgrade the **same application and the same persistent state**.

Required flow:

```text
frozen external source at exact source version
  ↓
restore exact published source-version packages and build
  ↓
start source infrastructure
  ↓
run source migrations
  ↓
start source API / workers / frontend
  ↓
create source data and prove source scenarios
  ↓
prepare a deterministic pending job and capture pre-upgrade invariants
  ↓
stop old API and workers, settle in-flight work, preserve the pending job
  ↓
capture the consistent recovery state required by the transition
  ↓
apply documented source/configuration changes to a working copy
  ↓
replace dependencies with exact target candidate artifacts and build
  ↓
run target migrations on the SAME database
  ↓
start target API / workers / matching frontend
  ↓
verify preservation, including all persistent stores
  ↓
verify continued operation and the pre-upgrade job's effect
  ↓
run Migrator again and verify unchanged migration history and data effects
```

Do not start the source application before its initial migrations. Prevent the old API and workers from writing during target migration or competing with target workers. Make pending-job setup deterministic; do not depend on a race with a running worker.

A target migration failure must block target startup. Include a failure scenario that proves this sequencing and the documented retry/recovery behavior. A repeated Migrator returning exit code zero is insufficient: verify applied migration identities/history and the absence of duplicate data effects.

## Persistent-state boundary

Preserve across source and target:

- the application PostgreSQL database;
- Keycloak identity/realm state and stable links through `AuthSubject`;
- Background Jobs/Hangfire storage, including a separate database if configured;
- user settings, role assignments and permission grants;
- attachment objects and metadata in the dedicated Attachments profile.

Do not recreate/reseed these stores to make the target pass. Keep PostgreSQL, Keycloak, MinIO and other infrastructure versions unchanged during the transition unless their upgrade is explicitly included and separately evidenced by the matrix.

Do not substitute a newly generated v3.2 app for the upgraded source application.

---

# 19. Upgrade invariants

Do not merely compare row counts.

Validate both **preservation** and **continuation**.

## Preservation

The core fixture must validate stable IDs, custom data, identity links, users/roles/permissions, Administrator behavior, audit, migration history, provider persistence, extension registration and Background Jobs state. Notes and Attachments preservation are mandatory in their dedicated profiles.

For document lifecycle, relationships, Accounting, OR/RR and reports/projections, explicitly declare which assertions the external upgrade fixture exercises and which remain covered only by platform/vertical regression. Do not claim external upgrade certification for an unexercised subsystem. Record this distinction in the matrix and release evidence.

Preservation checks include:

- stable IDs;
- custom application data;
- users;
- roles;
- permissions;
- Administrator semantics;
- audit history;
- document state/lifecycle if exercised by the declared external profile;
- document relationships if exercised by the declared external profile;
- migration history;
- OR/RR effects if exercised;
- Accounting effects if exercised;
- reports/projections if exercised;
- Notes in the mandatory Notes profile;
- Attachments in the mandatory dedicated MinIO-enabled profile;
- background-job state, including work queued before the upgrade;
- custom extension registration;
- application-specific migration data.

## Continuation

After upgrade prove that the application can still:

- create;
- update;
- save;
- post where applicable;
- execute custom handler/orchestration;
- query persisted data;
- run reports/queries where applicable;
- run Background Jobs and complete a job queued before upgrade with its expected effect neither lost nor duplicated;
- write new audit history;
- authorize users correctly.

A database that survives migration while the application is effectively unusable is a failed upgrade.

---

# 20. Mandatory certification profiles

Optional deployment capabilities do not make their release-certification profiles optional. All five profiles below are release-blocking for v3.2.0. They may share fixtures, infrastructure and runner code, but each must produce explicit evidence.

## Clean starter

Generate and run the minimal application. Prove login, the existing role/audit browser flow, authorization, health and execution of `platform.schema.validate` through the real worker. Notes and Attachments are disabled; MinIO is absent.

## Custom extension

Extend a fresh candidate-generated application according to the documentation. Prove its definition, Runtime handler, provider persistence and application migration outside the NGB repository.

## Core upgrade

Upgrade the frozen external source fixture through the documented maintenance-stop procedure. Preserve data, identity state, grants, audit, migration history, extension behavior and job state. Prove continued operation and completion of pre-upgrade pending work without a lost or duplicate effect.

## Notes without MinIO

Enable Notes while Attachments remains disabled and MinIO is absent. Create a note before upgrade, verify its content and history after upgrade, then edit/create notes successfully. Prove authorization and feature independence through the actual API/UI integration.

## Attachments with MinIO

Start MinIO only in this dedicated profile and use the existing supported storage integration.

Prove:

- feature enablement and authorization;
- upload/complete/download behavior before upgrade;
- preservation of attachment metadata, audit and the same stored objects;
- download of the pre-upgrade file after upgrade with identical contents;
- continued upload/complete/download behavior after upgrade;
- coherence of disabled-feature behavior.

Keep MinIO out of the default starter. Do not satisfy this profile by creating only new attachments after upgrade or by checking metadata without the original file contents.

---

# 21. CI/CD release gates

Separate pre-publication candidate validation from post-publication registry verification.

## Pre-publication candidate gate

Build immutable local candidate artifacts and run:

- platform backend tests;
- vertical backend tests;
- frontend tests;
- backend coverage;
- frontend coverage;
- package verification;
- NuGet compatibility gates;
- npm compatibility gates;
- architecture tests;
- all five mandatory certification profiles: clean starter, custom extension, core upgrade, Notes without MinIO, and Attachments with MinIO;
- integration tests;
- browser/E2E tests;
- security/negative tests;
- relevant performance/regression checks.

A failed mandatory gate blocks release.

## Build once, certify, publish

Build one immutable candidate artifact set. All candidate gates and publication must consume that same set, identified by the release manifest and checksums.

Publication workflows must download/promote the certified artifacts instead of repacking or rebuilding them. An identical Git commit alone does not prove artifact identity. Verify artifact identity again immediately before upload.

For npm, preserve the certified tarball and its integrity. For NuGet, record both archive identity and the package-content identity; repository signing can change archive bytes. Verify registry signatures and content identity rather than requiring an unsigned candidate's whole-file hash to equal the signed registry archive's hash.

## Post-publication registry verification

After publishing, verify actual registry artifacts:

- NuGet from nuget.org;
- npm from npmjs.com;
- exact versions;
- integrity/hashes;
- clean external restore/install;
- clean build;
- registry-only starter runtime smoke, including login, role/audit flow and the real platform job;
- linkage of the downloaded package contents to the certified candidate evidence.

A registry failure cannot retroactively prevent publication.

Until verification passes, block deployment/release promotion.

For transient network errors or registry propagation delays, allow bounded retries of verification. A partially completed publication may resume using the same certified artifacts after verifying the contents of any already published versions.

Never overwrite an already published package version. If the published contents are defective or do not match the certified contents, fix forward with a new version. A transient verification failure alone does not require a new package version.

## Avoid circular release logic

Do not require a version to be published before deciding whether that same version is safe to publish.

Candidate validation must run from local immutable artifacts.

---

# 22. Release/certification evidence

Produce machine-readable release evidence containing at least:

- NGB version;
- Git commit SHA;
- CI/build identifier;
- exact NuGet package IDs/versions, candidate archive hashes, content identities and registry signature verification results;
- npm tarball hash/integrity;
- template package hash;
- compatibility baselines used;
- source/target upgrade matrix with exact versions;
- frozen fixture identity, configuration/lockfile identities and applied upgrade patch;
- fixed infrastructure versions and preserved persistent-state boundaries;
- result and executed assertions for each of the five mandatory profiles;
- explicit distinction between externally certified subsystem behavior and platform/vertical-regression-only coverage;
- clean-install and generated-extension results;
- upgrade-certification, pending-job continuation and repeated-Migrator results;
- coverage/test result;
- relevant runtime/tool versions.

Do not create an unnecessary custom signing system if existing registry/GitHub provenance already solves provenance.

---

# 23. Performance requirements

The implementation must not introduce performance regressions or hidden scalability issues.

Certification concerns belong in build/quality tooling unless they are legitimate runtime validation.

## Runtime performance rules

- no reflection scan on every request;
- no repeated filesystem/package discovery in request paths;
- no synchronous blocking I/O in async request/job paths;
- no new N+1 DB patterns;
- do not repeatedly rebuild immutable definitions/configuration;
- no per-request ad-hoc DI graph construction;
- no package/template validation logic in Core/Runtime request paths;
- preserve bounded-work Background Jobs;
- preserve cancellation tokens;
- preserve async DB/network I/O;
- avoid unnecessary serialization/copying on hot paths;
- keep migration discovery deterministic/bounded;
- keep template generation deterministic and local.

## Database/migration performance

- avoid casual full-table scans on large production tables;
- add indexes only for real query patterns;
- avoid unnecessarily long locks/transactions;
- preserve migration correctness/idempotency semantics;
- avoid rewriting production data without need;
- test the upgrade on representative non-empty fixture DB;
- do not use destructive shortcuts.

## Frontend performance

- preserve code splitting/lazy-loading;
- do not accidentally duplicate Vue/Pinia/router due to wrong dependency classification;
- keep appropriate framework dependencies as peers;
- do not duplicate theme/CSS payload unnecessarily;
- Tailwind scanning must be narrowly bounded to app + required package paths;
- never scan the entire repository/filesystem;
- production Vite build must remain deterministic.

## Regression evidence

Add lightweight stable checks where meaningful:

- no unbounded external-app startup discovery;
- representative migration remains bounded;
- jobs remain bounded;
- packed UI does not duplicate peer frameworks;
- no unexplained major bundle growth from new tooling.

Do not make fragile microbenchmarks mandatory unless there is a stable baseline and sensible tolerance.

---

# 24. Testing requirements — non-negotiable

Testing must be **strong, dense, production-grade, and complete**.

The target is **100% coverage of platform and vertical production code under the existing NGB quality policy**.

This feature must not reduce current coverage.

## Backend

Maintain/enforce:

- **100% line coverage**
- **100% branch coverage**
- **100% method coverage**

for all production backend assemblies included in the established quality gate.

This includes affected/new code across:

- platform;
- Property Management;
- Trade;
- Agency Billing;
- CRM;
- any other current vertical covered by repository quality policy;
- reusable executable .NET template/certification logic under the explicit coverage inventory below.

Do not exclude difficult new code merely to preserve 100%.

Use exclusions only for genuinely generated/non-executable/tool-generated code according to existing project policy.

## Frontend

Maintain/enforce:

- **100% line coverage**
- **100% branch coverage**
- **100% function coverage**
- **100% statement coverage**

for frontend production code included in the established quality gate.

This includes:

- platform UI;
- Property Management frontend;
- Trade frontend;
- Agency Billing frontend;
- CRM frontend;
- executable generated/template frontend helpers;
- new public Tailwind/assets/tooling logic under the explicit coverage inventory below.

Do not lower thresholds.

## Explicit quality inventory

Before implementing new tooling, record each category and its actual verification entry points:

| Code/content category | Required verification |
| --- | --- |
| Platform and vertical production code | Existing complete backend/frontend gates at their unchanged 100% thresholds |
| Reusable executable .NET/TypeScript/JavaScript template helpers and certification logic | Unit/negative tests and explicit inclusion in the applicable coverage gate at its existing 100% thresholds |
| Generated applications and their executable helpers | Build and runtime/E2E certification from the real template artifact; explicitly instrument owned executable helpers instead of assuming the existing `NGB.*` assembly filter covers arbitrary application names |
| Shell/process orchestration | Automated behavior, failure/exit-code, isolation and sequencing tests through the aggregate quality workflow; do not claim a numeric coverage result that the tooling did not measure |
| Static template/configuration/documentation files | Artifact-content, substitution, generation and documented-workflow checks; no artificial line-coverage percentage for non-executable content |

Do not let a namespace, filename extension or generated-directory location silently remove executable logic from the quality inventory. Record generated-app coverage separately where required to avoid treating an instrumented fixture as platform production coverage.

Bind these categories and the Section 31 gate IDs to real commands/workflows during implementation. A missing command, unmeasured applicable coverage scope or skipped mandatory profile is not a passing gate.

---

# 25. Required test types

Use the right test level rather than over-mocking.

## Unit tests

Cover:

- pure logic;
- matrix/version logic;
- template parameters;
- path/isolation validation;
- config generation;
- compatibility metadata;
- feature flags;
- parser/serializer behavior;
- failure branches.

## Architecture tests

Cover:

- layer direction;
- SQL/provider isolation;
- source-reference prohibition;
- external independence;
- vertical isolation;
- host/composition boundaries.

## Integration tests

Use the established Testcontainers approach for real infrastructure where applicable:

- PostgreSQL;
- MinIO for Attachment-specific certification;
- migrations;
- persistence;
- Background Jobs;
- relevant security integration.

## API integration tests

Cover:

- auth;
- authorization;
- Administrator behavior;
- users/roles;
- health endpoints;
- feature-disabled behavior;
- error responses;
- external custom module behavior.

## Frontend unit/component tests

Cover:

- new public UI utilities/tooling;
- feature-flag UI behavior;
- shell/navigation integration;
- configuration;
- disabled Attachments UX;
- user/role flow pieces;
- public-assets/Tailwind helpers.

## Browser/E2E tests

Cover real flows:

- Keycloak login;
- shell load;
- navigation;
- user/role scenario;
- persistence/reopen;
- audit visibility when part of the selected flow;
- generated external app;
- both mandatory capability profiles: Notes without MinIO and Attachments with MinIO.

## Packaging tests

Test actual `.nupkg` / `.tgz` / template artifacts.

## Upgrade tests

Use the same persistent state and frozen previous-version fixture. Prove the maintenance stop, migration-failure startup block, pre-upgrade pending-job continuation and repeated-Migrator invariants.

## Negative tests

Required examples include:

- Attachments enabled without valid storage;
- missing package dependency;
- forbidden source reference;
- invalid upgrade matrix;
- incompatible public contract;
- migration failure;
- invalid Keycloak config;
- unauthorized user;
- Administrator-degradation attempt;
- invalid template parameter;
- missing npm export;
- missing CSS/public assets;
- monorepo leakage.

## Security tests

At minimum validate affected:

- authentication;
- authorization;
- full-access Administrator semantics;
- role/permission enforcement;
- both Administrator paths, including `ngb-admin` without a platform record;
- inactive-account denial and rejection of display-name-based privilege escalation;
- new permissions granted effectively to administrators but not ordinary users;
- feature flags enforced for administrators;
- authorization in both mandatory optional-capability profiles;
- no accidental anonymous endpoint exposure;
- invalid configuration failure behavior.

## No “coverage-only” tests

Tests must prove behavior/contracts/invariants/failure modes.

Do not add meaningless assertions solely to execute branches.

---

# 26. Vertical regression requirement

Although this feature is platform/quality infrastructure, run the **full platform + vertical regression suite**.

Do not assume packaging/hosting/frontend changes cannot affect verticals.

All current verticals must remain green, including at minimum:

- Property Management;
- Trade;
- Agency Billing;
- CRM.

Run their applicable:

- unit tests;
- integration tests;
- API tests;
- architecture tests;
- browser/E2E tests;
- frontend tests;
- coverage gates;
- build/package checks.

Prefer extending current aggregate quality scripts instead of creating a parallel incomplete test system.

---

# 27. Suggested repository shape

Follow stronger existing conventions if present, but a reasonable organization is:

```text
packaging/
  templates/
    NGB.Platform.Templates/
      NGB.Platform.Templates.csproj
      content/
        ngb-app/
          .template.config/
            template.json
          ...

quality/
  external-consumer/
    ...

  upgrade-certification/
    matrix/
      ...
    fixtures/
      <exact-source-version>/
        ...
    ...

docs/
  start-here/
    create-external-app.md

  guides/
    upgrade-external-app.md

  reference/
    external-compatibility-policy.md
    external-upgrade-matrix.md

.github/workflows/
  external-consumer.yml
  upgrade-certification.yml
```

Keep:

- template concerns in packaging/template tooling;
- certification in quality/CI;
- production platform runtime free of certification-specific dependencies.

---

# 28. Documentation requirements

Documentation is part of the implementation.

## Create external app

Document:

- prerequisites;
- template installation;
- app creation;
- local config;
- PostgreSQL;
- Keycloak;
- migrations;
- startup;
- Background Jobs;
- frontend;
- Administrator bootstrap;
- health/logs.

## Grow the architecture

Document when to introduce:

- Definitions/module;
- Runtime;
- PostgreSQL/provider project;
- custom migration;
- custom Background Jobs integration.

Use the same technical extension example exercised against a freshly generated application in certification. Document project-dependency direction explicitly.

Explicit rule:

> first SQL/provider-specific persistence => provider project.

## Frontend integration

Document:

- exact `@ngbplatform/ui` version policy;
- peer dependencies;
- Vite plugin;
- CSS;
- public assets;
- Tailwind preset;
- class discovery;
- supported entry points.

## Attachments

Document:

- disabled in minimal starter;
- required object storage integration;
- MinIO setup;
- feature enablement.

## Upgrade policy

Document:

- compatibility vs certified upgrade;
- exact source/target matrix;
- chained minor paths;
- major migration-guide semantics;
- rollback expectations;
- unsupported direct paths;
- the maintenance stop and migration-failure startup block;
- persistent Keycloak/Hangfire/object-storage state;
- template updates versus upgrades of user-owned existing applications;
- the exact documented dependency/source/configuration patch;
- external certification scope for each subsystem.

## Publishing docs

Fix stale hardcoded version/package-count examples discovered during audit.

Use version-neutral documentation where appropriate.

---

# 29. Template parameters

Keep CLI options deliberately small.

Mandatory:

- application/project name.

Only add other options if clearly justified, e.g.:

- namespace override;
- ports/compose project name when required to avoid collisions.

Do not build a scaffolding designer.

Do not add switches for:

- vertical selection;
- dozens of optional modules;
- database provider selection;
- auth provider selection;
- UI framework selection;
- architectural style selection.

---

# 30. Explicit out of scope

Do not expand v3.2.0 into:

- plugin marketplace;
- vertical generator;
- generic application designer;
- Workflow Engine;
- MCP;
- AI Assistant;
- Output Templates;
- new UI framework;
- Tailwind major migration;
- new DB provider;
- arbitrary source-code migration engine;
- generic compatibility SaaS/service;
- generic deployment platform;
- Watchdog redesign;
- observability rewrite.

Stay focused.

---

# 31. Canonical acceptance matrix

This is the single normative acceptance matrix. Sections 5–30 define the required behavior; Sections 34–35 describe reporting and completion. Repeated summaries must not introduce weaker alternatives to this matrix.

All rows are mandatory. Candidate rows block publication; registry rows block deployment/release promotion. The five profile IDs correspond to Section 20.

Bind each gate ID below to an actual repository command or workflow in the implementation's checked-in matrix. Include the exact command, expected assertions, artifact/profile identity and machine-readable result. The IDs below are requirements, not claims that commands already exist. Unbound gates or skipped required assertions block completion.

| Gate ID | Verification/profile | Required passing result | Blocks |
| --- | --- | --- | --- |
| template-artifact | Template package checks | Package installs; `dotnet new ngb -n ...` generates an independent application with exact dependencies, valid substitutions and no fake/demo vertical | Publication |
| external-isolation | Isolated restore/build plus negative tests | No NGB source references, workspace fallback, undeclared dependency, leaked feed/cache/configuration or unpublished package asset | Publication |
| clean-starter | Mandatory clean starter profile | PostgreSQL/Keycloak work; migrations precede startup; API/worker/web run; login, ordinary role create/edit/reopen/audit, health and logs pass; Notes/Attachments off; no MinIO | Publication |
| starter-job | Real worker within clean starter | Registered `platform.schema.validate` executes through the worker and exposes success/failure through normal mechanisms | Publication |
| administrator-contract | API/UI security scenarios | Registered role-code and trusted `ngb-admin` paths pass; inactive users denied; display names grant nothing; new permissions and feature flags behave as specified | Publication |
| generated-extension | Mandatory custom extension profile | Documented definition, Runtime handler, provider persistence and application migration work in a fresh candidate-generated app outside the repository | Publication |
| frontend-artifact | Packed npm consumer build | All supported entry points/types, CSS tokens, assets, local PostCSS, public Tailwind preset, class discovery, peers and production Vite build pass without workspace source | Publication |
| nuget-compatibility | Per-runtime-package comparisons | First-stable-major and selected previous-release baselines pass; new package exceptions are explicit; template content is classified separately; no hidden suppression | Publication |
| npm-compatibility | Public-contract comparison and old consumer compilation | Supported type/Vue/tooling contracts remain compatible; negative checker tests reject the specified breaks; compatible additions pass | Publication |
| source-fixture | Frozen source-version baseline run | Exact published source packages run with the recorded immutable source/configuration/lockfiles; source scenarios and extension work before upgrade | Publication |
| core-upgrade | Mandatory core upgrade profile | Documented patch and maintenance stop upgrade the same app/state; data, grants, audit, migrations and custom behavior survive and continue working | Publication |
| upgrade-job | Pending work in core upgrade | Work queued before upgrade completes afterward with the expected effect neither lost nor duplicated | Publication |
| migration-sequencing | Successful, failed and repeated migration scenarios | Initial migrations precede startup; failure blocks target startup; repeated Migrator preserves history and causes no duplicate effects | Publication |
| notes-upgrade | Mandatory Notes without MinIO profile | Existing note content/history survives; post-upgrade create/edit and authorization pass with Attachments disabled and no MinIO | Publication |
| attachments-upgrade | Mandatory Attachments with MinIO profile | Existing objects/metadata/audit survive; original file downloads with identical contents; new upload/complete/download and authorization pass | Publication |
| backend-quality | Existing complete backend aggregate gate | 100% line/branch/method coverage for the explicit production/tooling inventory and all applicable tests pass | Publication |
| frontend-quality | Existing complete frontend aggregate gate | 100% line/branch/function/statement coverage for the explicit production/tooling inventory and all applicable tests pass | Publication |
| regression-quality | Aggregate platform and all vertical checks | Platform, PM, Trade, Agency Billing and CRM unit/integration/API/browser/E2E/architecture/packaging/security checks and relevant stable performance checks pass | Publication |
| tooling-quality | Explicit quality inventory | New helpers/runner logic have measured coverage; process orchestration passes behavior/negative tests; generated/static content receives its specified checks | Publication |
| candidate-evidence | Immutable artifact manifest and release evidence | Exact versions, fixture/patch identities, baselines, profile results, persistent-state evidence and subsystem certification scope are recorded | Publication |
| publish-identity | Publication of certified artifacts | Publication consumes the certified artifact set without rebuilding/repacking; uploaded identities are verified | Publication |
| registry-verification | Registry-only install/build/runtime smoke | Exact registry packages pass content/signature/integrity checks and clean starter smoke; transient failures receive bounded retries; defective published content requires a new version | Promotion |

No green aggregate result may conceal a missing mandatory profile, failed component gate, unsupported claimed transition or unmeasured required coverage scope.

---

# 32. Implementation order

Follow this order. Record any necessary adjustment discovered during audit without weakening the agreed decisions or acceptance matrix.

## Phase 1 — Audit and freeze the source consumer

Audit the repository and published supported 3.1 artifacts. Select exact source versions and per-package baselines.

Build and run a minimal external source consumer against those published artifacts, including the small technical extension and source data scenarios. Use app-owned Tailwind/PostCSS configuration if required by 3.1; allow no NGB checkout dependency.

Freeze fixture source, configuration, lockfiles, provenance and artifact identities before changing platform code. Do not start by polishing the new template.

## Phase 2 — Establish compatibility and acceptance gates

Preserve the first-stable-major baselines, add previous-release protection and establish npm public-contract checks with negative tests before changing public contracts.

Bind the canonical acceptance matrix to planned/implemented command entry points and define the explicit quality inventory. Capture baseline failures without suppressing them.

## Phase 3 — Prove candidate consumption and fix actual leaks

Build isolated consumers against candidate artifacts and fix demonstrated issues such as Tailwind packaging, missing assets/dependencies or additive host composition defects.

Use local app PostCSS configuration. Keep SemVer compatibility and the frozen source fixture intact. If a required fix is breaking, stop and report it.

## Phase 4 — Convert the proven consumer into the official template

Generate the minimal workload with the agreed role/audit scenario, real platform job, coherent navigation and Notes/Attachments disabled.

The template must use a configuration already proven to work independently.

## Phase 5 — Certify clean installation and extension

Run the actual generated application end-to-end in isolation. Separately extend a fresh generated application using the documented technical module and prove its layered behavior.

## Phase 6 — Implement the version-neutral upgrade runner and profiles

Upgrade a working copy of the frozen source fixture using the same persistent stores, fixed infrastructure versions and documented maintenance stop.

Complete the core upgrade, pending-job, migration-failure/repeat, Notes-without-MinIO and Attachments-with-MinIO scenarios. Record the exact external certification scope.

## Phase 7 — Integrate immutable candidate and publication gates

Build once, certify that artifact set and publish that same set. Integrate all five mandatory profiles and aggregate quality gates. Add registry-only verification with content/signature handling and bounded transient retries.

## Phase 8 — Complete documentation and evidence

Document creation, extension, Administrator bootstrap, feature profiles, exact upgrade paths, maintenance/recovery, template lifecycle and publishing. Ensure examples match tested commands and the acceptance matrix.

## Phase 9 — Complete full regression and release readiness

Run the complete platform and vertical regression/coverage gates against the release candidate, all certification profiles and relevant stable performance checks.

Do not mark implementation complete until the canonical acceptance matrix is bound to real commands and every applicable gate has passed. Post-publication promotion remains blocked until registry verification passes.

---

# 33. Engineering rules for Codex

1. Read current code before designing replacements.
2. Reuse existing extension points.
3. Prefer additive changes in v3.2.0.
4. Never silently introduce public breaking changes.
5. Do not add compatibility suppressions just to make CI green.
6. Do not create abstractions without concrete responsibilities.
7. Do not duplicate existing DI/services/helpers/scripts.
8. Keep certification concerns out of production Runtime.
9. Do not weaken layering.
10. Never move SQL into Runtime/API/Migrator.
11. Never hardcode vertical behavior into platform code.
12. Do not add fake entities/jobs/storage providers for demonstrations.
13. External certification must not depend on the NGB checkout.
14. Build once, certify the exact packaged artifacts, and publish that same artifact set without repacking.
15. Preserve release-train package version alignment.
16. Errors must be actionable.
17. Fail fast on invalid configuration.
18. Preserve cancellation and async behavior.
19. Scripts must be deterministic/idempotent.
20. Avoid needless OS-specific assumptions.
21. Never lower coverage thresholds.
22. Do not skip negative/security tests because the happy path passes.
23. Do not mark implementation complete until the full platform + vertical regression suite and all five mandatory certification profiles are green.
24. Freeze the source fixture before platform changes; apply only the documented upgrade patch to a working copy.
25. Enforce maintenance-stop migration sequencing and preserve all persistent stores.
26. Keep Section 31 as the single acceptance authority and bind every gate to real verification commands.

---

# 34. Required final implementation report

When implementation is complete, report:

## Repository audit

- important findings;
- leaks discovered;
- mechanisms reused;
- stale docs/config corrected.

## Production changes

- projects/files added;
- projects/files changed;
- public APIs/exports added;
- template structure;
- certification structure;
- canonical acceptance gate-to-command mapping;
- explicit quality/coverage inventory.

## Compatibility

- major-line baseline results;
- previous-release baseline results;
- npm compatibility results;
- additive public APIs;
- explicit statement whether any breaking 3.x public change was introduced.

If a breaking change is required but intentionally not implemented, state it clearly.

## Clean-install certification

Report:

- generated app;
- package sources;
- Keycloak;
- PostgreSQL;
- migrations;
- API;
- Background Jobs;
- frontend;
- browser scenario;
- extension of a fresh generated app using the documented module;
- both Administrator paths, inactive-account denial and feature-flag behavior.

## Upgrade certification

Report:

- exact source version;
- exact target version;
- frozen fixture provenance/revision and source/configuration/lockfile identities;
- applied dependency/source/configuration patch;
- preservation of application, Keycloak, Hangfire and profile-specific object-storage state;
- unchanged infrastructure versions or explicitly certified infrastructure changes;
- maintenance stop and migration-failure startup block;
- migration result/history;
- preservation assertions;
- continuation assertions, including work queued before upgrade;
- mandatory Notes and Attachments profile results, including original-file content verification;
- externally certified subsystem scope versus regression-only coverage;
- second Migrator run and proof of no duplicate data effects.

## Tests

Report exact results for:

- backend coverage;
- frontend coverage;
- platform;
- each vertical;
- integration;
- browser/E2E;
- architecture;
- packaging;
- compatibility;
- clean install;
- upgrade;
- performance/regression.

Do **not** state “100% coverage” unless the actual repository coverage commands succeeded.

## Remaining risks

List only real unresolved risks.

Do not hide:

- skipped tests;
- flaky tests;
- environment gaps;
- unsupported paths;
- unresolved assumptions.

---

# 35. Definition of Done

The feature is done only when an independent developer can use official artifacts to do this:

```text
install template
  ↓
create app
  ↓
restore
  ↓
configure PostgreSQL + Keycloak
  ↓
migrate
  ↓
run
  ↓
login
  ↓
use real NGB platform functionality
  ↓
add a correctly layered custom module
  ↓
build independently
  ↓
upgrade through a supported path
  ↓
retain data + permissions + audit + extension behavior
```

and CI proves this lifecycle continuously for the exact supported transitions recorded in the release matrix.

Completion requires every candidate/publication gate in Section 31, including the five mandatory profiles, unchanged quality thresholds, frozen-fixture evidence and publication of the certified artifacts. Registry-only verification is mandatory before deployment/release promotion. Existing user applications upgrade through documented dependency/source/configuration changes, not template regeneration.

The result must be a **production-ready external application contract**, not a demonstration that only works from inside the NGB repository.
