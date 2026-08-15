# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- N/A

### Changed
- N/A

### Deprecated
- N/A

### Removed
- N/A

### Fixed
- N/A

### Security
- N/A

## [2.0.0] - 2026-08-15

### Breaking changes
- Replaced document action flags in `DocumentEffectsDto.Ui` with the canonical `DocumentEditorStateDto.Actions` contract.
- Removed `IDocumentUiEffectsContributor`; verticals now register action definitions, context enrichers, authorization evaluators, and availability evaluators.
- Removed `IDocumentService.GetDerivationActionsAsync`, `DocumentDerivationActionDto`, and the separate generic derivation HTTP routes.
- Changed `/api/documents/{documentType}/{id}/actions/{actionCode}` to require `Idempotency-Key` and `ExpectedVersion`, and to return `ExecuteDocumentActionResultDto`.
- Changed the public `DocumentControllerBase` constructor to receive the action query and dispatcher services.
- Added required transaction-management overloads to `IDocumentPostingService`; custom implementations must be updated.
- Document action execution requires stable concurrency versions; custom `IDocumentRepository` implementations must implement `IncrementVersionAsync`.
- Replaced the shared UI framework's `resolveDocumentActions`, derivation helpers, and UI-effects types with server-driven Document Actions and semantic target resolution.
- Requires coordinated API, background host, migrator, and web-client deployment; mixed `1.3.x`/`2.0.x` action clients are unsupported.

### Added
- Metadata-driven Document Actions registry, editor-state contract, unified dispatcher, optimistic concurrency, and idempotent command execution.
- Transactional platform outbox with retry, dead-letter history, health reporting, and Work Center projections.
- Permission-filtered Work Center tasks and notifications with cursor paging, claiming, snoozing, preferences, and SignalR invalidation.
- Property Management receivable reconciliation actions/tasks and CRM lead qualification/conversion derivations/tasks.
- Role-scoped CRM Work Center preferences for `Qualify lead`, `Convert qualified lead`,
  `Complete CRM activity`, `Lead qualified`, and `Opportunity won`.
- The `Complete CRM activity` task and the `Lead qualified` and `Opportunity won`
  informational notification flows.
- Shared Vue Work Center drawer, full-page view, notification preferences, and generic document-action execution.

### Changed
- Legacy document lifecycle endpoints now delegate to the unified action dispatcher.
- Agency Billing derivations and standard lifecycle actions are exposed through the common action registry.
- Replaced the generic `Task assigned` preference with concrete per-task and per-notification
  checkboxes. Definitions are enabled by default and shown only to users with an applicable role.
- Task and notification preferences are now independent. Disabling a task checkbox prevents that
  task from being created for the user; a task never creates a duplicate assignment notification.
- Platform and UI package versions are aligned at `2.0.0`.
- NuGet package validation compares future `2.x` public APIs with the `2.0.0` compatibility baseline.
- The public `@ngbplatform/ui` export snapshot is verified by package CI and `npm run test:all`.

### Removed
- Document action booleans from effects responses and the `DocumentUiEffects` contract.
- Separate derivation-action loading and the generic derive HTTP route; derivations now execute only through registered Document Actions.
- App-level `resolveDocumentActions` configuration from the shared UI framework.

### Security
- Document actions repeat authorization under the document row lock using a refreshed permission snapshot.
- Work Center source permissions are applied in SQL before counts and pagination.

### Migration
- Follow [Migrating from 1.3.1 to 2.0.0](docs/guides/migrating-to-2.0.md) before updating platform packages.

## [1.3.1] - 2026-07-11

### Added
- Added the CRM vertical with lead intake, qualification, conversion, opportunities, accounts,
  contacts, activities, reporting, demo data, background processing, and complete API and web hosts.
- Added CRM database migrations, migrator, watchdog, Docker Compose stack, integration tests, and
  shared UI consumption.
- Added official NuGet and npm package build, verification, trusted-publishing, and container-image
  workflows for coordinated platform releases.

### Changed
- Aligned platform NuGet packages and `@ngbplatform/ui` consumers on the `1.3.1` release line.
- Added production configuration and deployment fixes required by the CRM vertical.

### Fixed
- Ensured packaged dashboard stylesheets are included in consuming application publish output.
- Corrected CRM deployment configuration and the shared UI framework Vitest root.

## [1.2.0] - 2026-06-20

### Added
- Added platform users, roles, permissions, role assignments, effective-access inspection, and
  access-audit capabilities.
- Added permission definitions and role defaults for platform metadata, reports, accounting, and
  Property Management features.
- Added Keycloak administration integration and resilient user-provisioning operations.
- Added permission-aware document, catalog, report, audit-log, administration, main-menu, and
  command-palette services.
- Added security management UI for users, roles, permission matrices, effective access, and access
  audit, including backend integration, frontend browser, and end-to-end coverage.

### Changed
- Applied permission filtering to application navigation, metadata lists, reports, editor actions,
  audit data, and permission-aware landing pages.
- Added scoped security caching and memoization to reduce repeated permission and metadata work.

### Security
- Enforced server-side authorization for protected platform and vertical resources using the
  current user's effective permission snapshot.

## [1.1.1] - 2026-05-19

### Added
- Grafana k6 + TypeScript performance testing workspace under `performance-tests`.
- Reusable vertical-neutral NGB performance framework with Keycloak auth, typed env parsing, HTTP client wrapper, custom metrics, profiles, thresholds, summary export, and generic NGB API clients.
- Property Management smoke, baseline, business-day, reporting regression, load, stress, spike, and soak performance scenarios.
- Property Management platform-read, platform-read-capacity, platform-mixed-capacity, platform-breakpoint, platform-reporting, document-lifecycle, audit, maintenance, concurrency, and destructive write-heavy performance scenarios.
- Initial Trade and Agency Billing performance smoke scaffolds.
- Performance testing documentation, runner scripts, and CI type-check workflow.
- Reusable multi-scenario workload composition for vertical packages while keeping the shared performance framework vertical-neutral.
- Write-heavy profile overrides in the PM performance `.env.example` and documented `.env.write.local` workflow.

## [1.0.0] - 2026-04-23

### Added
- Initial public release of **NGB Platform 1.0**.
- Core platform modules for building metadata-driven business applications.
- Platform hosts for API delivery, background jobs, health monitoring, and schema deployment.
- Platform engines for accounting, operational registers, reference registers, and append-only business audit logging.
- PostgreSQL-based persistence and migration support.
- Demo industry solutions, including Property Management and Trade.
- Shared UI workspace and web application foundations.

### Notes
- This release establishes the first public baseline of the NGB Platform repository.

[Unreleased]: https://github.com/ngbplatform/ngb/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/ngbplatform/ngb/compare/v1.3.1...v2.0.0
[1.3.1]: https://github.com/ngbplatform/ngb/compare/v1.2.0...v1.3.1
[1.2.0]: https://github.com/ngbplatform/ngb/compare/v1.1.1...v1.2.0
[1.1.1]: https://github.com/ngbplatform/ngb/compare/v1.0.0...v1.1.1
[1.0.0]: https://github.com/ngbplatform/ngb/releases/tag/v1.0.0
