# NGB.PostgreSql

This project implements the PostgreSQL persistence provider and contains the core **platform**
migration pack (Evolve). ASP.NET Core integration is in `NGB.PostgreSql.AspNetCore`; Hangfire
storage integration is in `NGB.BackgroundJobs.PostgreSql`.

## Where migrations live

- Place SQL scripts under `db/migrations/`.
- Scripts are embedded resources.

The pack starts with a baseline and applies subsequent versioned migrations in order:

- `V2026_02_20_0001__ngb_platform_baseline.sql`
- `V2026_05_14_0100__ngb_platform_document_read_path_indexes.sql`
- `V2026_06_10_0100__ngb_platform_security_rbac.sql`
- `V2026_06_12_0100__platform_users_normalized_email_index.sql`
- `V2026_07_26_0100__ngb_platform_document_actions_work_center.sql`
- `V2026_08_26_0100__ngb_platform_read_path_indexes.sql`

The pack covers:

- shared platform tables and guards
- accounting core tables, indexes, paging indexes, and cash-flow metadata
- documents core tables, lifecycle state/history, guards, and document-relationship mirroring functions
- catalogs, operational registers, and reference registers
- platform users, audit log, and report variants

## Notes

- New databases apply the baseline and every subsequent migration; existing databases apply pending migrations.
- Keep applied versioned migrations and their checksums unchanged. Add a new migration for schema changes.
- Module packs depend on `platform` and extend it with their own migrations.
- Run migrations through the vertical migrator; see [NGB.Migrator.Core](../NGB.Migrator.Core/README.md).
