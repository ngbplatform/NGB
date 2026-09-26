# Performance Tests Changelog

This changelog covers the private performance test workspace, shared framework, and
vertical scenario packages. Platform releases are documented in the
[repository changelog](../CHANGELOG.md).

## [3.0.0]

### Changed

- Align the root workspace, framework, Property Management, Trade, and Agency Billing
  package versions with the NGB Platform 3.0 release line. This coordinated version
  replaces the previous 1.1.1 package metadata.
- Measure fresh rent-charge posting through the versioned document-action API by
  default. The configured fixture supplies the payload template; each iteration
  creates and posts a new document. An explicit idempotent-replay mode reuses the
  same command and key, with separate counters for fresh postings and replays.
- Require zero HTTP failures for posting and XLSX export in the standard thresholds.
  HTTP 429 responses count as failures.

### Added

- Focused posting-contract, reporting, and read-regression diagnostic probes.
- Report diagnostics by report ID, period, and status; document-operation diagnostics
  and counters for executed lifecycle/posting branches.
- Resolved k6 run options in summaries and run manifests from the macOS/Linux runner,
  including the Git revision, change fingerprint, tool versions, non-secret settings,
  capacity/breakpoint overrides, and optional dataset/API image identities.

### Fixed

- Check dropped iterations globally in multi-scenario workloads and breakpoint so
  unmatched tagged submetrics cannot hide skipped load.
- Count nested lifecycle operation failures and require actual write/posting branch
  execution in write-heavy validation.

### Migration and comparisons

- Target NGB Platform 3.0.x. Trade and Agency Billing still provide smoke scaffolds.
- For posting, select an open period within the template's lease term using
  `NGB_PM_POSTING_FROM_UTC`, `NGB_PM_POSTING_TO_UTC`, and
  `NGB_PM_POSTING_DUE_ON_UTC`. The test aborts if the selected period is closed or
  unavailable; it does not reopen periods.
- Fresh posting creates persistent test data. Restore the same dataset between
  comparable write benchmarks. Legacy runs that repeatedly posted an already-posted
  fixture are not equivalent to the fresh-posting workload.
- Stricter gates can turn a previously passing run into a failure without a new
  application regression. Compare error counts and dropped iterations explicitly.
- Keep historical summaries and manifests unchanged; do not relabel earlier runs as
  version 3.0.0.
