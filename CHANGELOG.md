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

## [1.1.1] - 2026-05-18

### Added
- Grafana k6 + TypeScript performance testing workspace under `performance-tests`.
- Reusable vertical-neutral NGB performance framework with Keycloak auth, typed env parsing, HTTP client wrapper, custom metrics, profiles, thresholds, summary export, and generic NGB API clients.
- Property Management smoke, baseline, business-day, reporting regression, load, stress, spike, and soak performance scenarios.
- Property Management platform-read, platform-read-capacity, platform-mixed-capacity, platform-breakpoint, platform-reporting, document-lifecycle, audit, maintenance, concurrency, and destructive write-heavy performance scenarios.
- Initial Trade and Agency Billing performance smoke scaffolds.
- Performance testing documentation, runner scripts, and CI type-check workflow.
- Reusable multi-scenario workload composition for vertical packages while keeping the shared performance framework vertical-neutral.
- Write-heavy profile overrides in the PM performance `.env.example` and documented `.env.write.local` workflow.

## [1.0.0] - 2026-04-14

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

[Unreleased]: https://github.com/ngbplatform/ngb/compare/v1.1.1...HEAD
[1.1.1]: https://github.com/ngbplatform/ngb/compare/v1.0.0...v1.1.1
[1.0.0]: https://github.com/ngbplatform/ngb/releases/tag/v1.0.0
