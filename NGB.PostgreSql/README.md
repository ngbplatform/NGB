# NGB.PostgreSql

This project is the core **platform** migration pack (Evolve).

## Where migrations live

- Place SQL scripts under `db/migrations/`.
- Scripts are embedded resources.

This pack now uses a **single clean-slate platform baseline** for recreated databases:

- `V2026_02_20_0001__ngb_platform_baseline.sql`

The baseline contains the final platform schema state for:

- shared platform tables and guards
- accounting core tables, indexes, paging indexes, and cash-flow metadata
- documents core tables, lifecycle state/history, guards, and document-relationship mirroring functions
- catalogs, operational registers, and reference registers
- platform users, audit log, and report variants

## Notes

- This baseline supersedes the older incremental platform migrations that were previously kept in this folder.
- It is intended for recreated / non-production databases where the schema history can be reset cleanly.
- Module packs still depend on `platform` and can extend the schema with their own baselines.
