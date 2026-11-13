# Physical text types in development evaluations

Candidate `release-candidate-local-20261113-080232` passed the Kit application
profile in 381.1 seconds. Codex root document-change acceptance passed its frozen
HTTP cases, but the selected regression batch finished with 254 passed and one
failed case: `BuiltInMaintenanceJobsTests.Maintenance_jobs_report_healthy_schema_audit_and_accounting_on_an_initialized_application`.

The generated migration added `completion_note varchar(2000)` for a metadata
`ColumnType.String` field. SDK 3.4.0 maps that type to PostgreSQL `text` and rejects
`character varying` in schema validation. Ordinary writes succeeded, so the
mismatch was only discovered by the later maintenance job. This is a generated
application migration error, not a new defect in the platform validator.

The same candidate's generated `new-catalog` source used `varchar(64)` and
`varchar(200)` for the category code and name. Its previous HTTP acceptance passed
without checking metadata schema compatibility. The shared host correction closes
that gap for catalog acceptance as well.

The maintained report starting fixture had the same type mismatch. Its original
category migration is preserved, and a new `category_string_columns` migration
converts both fields to `text` while retaining their database length constraints.
The current overlay manifest includes the additive migration. Its original
verification candidate is labelled as historical provenance, not as evidence for
the corrected fixture.

## Correction

- The document, catalog and persistence skills give the physical type rule directly:
  `text`, with a `CHECK` for a database length limit. The document reference no
  longer mentions `varchar(n)` as an apparent alternative. Application payload
  validation on both Draft paths remains required for HTTP 400 responses.
- The independent source-review instructions check the same installed contract.
- The shared frozen HTTP host calls both public catalog/document schema validators
  after migrations and Setup, before exercising generated HTTP behavior. All four
  development-oracle identities include the changed host. Authored maintenance
  tests retain their additional core-schema checks.

No SDK source, applied migration, historical evaluation receipt or test assertion
was changed. Generated migrations were repaired only in disposable diagnostic
copies, leaving the original inferences and their receipts intact. The document
repair replaces `varchar(2000)` with `text` and a named length CHECK; the catalog
repair does the equivalent for code/name while preserving collation and uniqueness.
All other generated source and tests are byte-identical.

## Verification

[Diagnostic artifacts](../../../artifacts/ai-devkit/diagnostics/document-schema-20261113)
contain the exact original source identity, isolated copies, scripts and receipts.
The negative control runs the first independent Draft HTTP test against the
original generated schema. It now fails in `InspectionHost.StartAsync` with the
same type mismatch, in 21.2 seconds including compilation, before any HTTP request
or authored regression test executes.

The oracle integrity, source-review, fixture-contract and guidance suites passed
27 tests. All three changed skill validators passed.

The corrected diagnostic copy passed complete document-scenario acceptance in
342.9 seconds: seven frozen HTTP tests, 119 unit tests, five new integration tests
and all 255 selected regression cases, including the originally failing maintenance
job. Its 18 frontend component tests and ten E2E tests passed with no skips;
frontend coverage remained 100% for lines, branches, functions and statements
across all ten source files. The independent runner verified the source inventory
after execution. See `corrected-acceptance/acceptance.json` and the retained TRX files.

The generated catalog's original type mismatch also failed at host startup, in
21.4 seconds including compilation. Its corrected copy passed all six frozen
catalog HTTP tests through the normal independent runner in 48.3 seconds,
including repeated public Migrator execution and preservation of saved records.
An earlier ad-hoc diagnostic run passed five cases but could not locate the
Migrator because its directory layout differed from the runner's expected
`application/` layout; that setup failure is retained alongside the final result.

The current report starting fixture was prepared from sealed packages with the
updated overlay, built without monorepo references and passed the same six frozen
catalog tests plus the maintenance-job regression. The latter executes all six
platform schema validators. Its original source inventory remained unchanged;
receipts are under `report-seed/`. This checks the report task's starting schema,
not a newly generated report implementation or model evaluation.

The CLI and `NGB.Platform.Templates` 3.4.0 packages were rebuilt with the updated
guidance. SDK package contents were preserved.

A manually repaired diagnostic application is not a successful model inference
and cannot replace the original failed receipt. FULL Certification and the full
platform Backend/Frontend/coverage suites were not run. No new Codex/Claude calls
were made.
