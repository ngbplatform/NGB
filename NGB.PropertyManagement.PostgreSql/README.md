# NGB.PropertyManagement.PostgreSql

This project is the **Property Management** migration pack (Evolve).

## Where migrations live

- Place SQL scripts under `db/migrations/`.
- Scripts are embedded resources.

The pack starts with a PM baseline and applies subsequent versioned migrations:

- `V2026_03_13_1000__ngb_pm_final_clean_baseline.sql`
- `V2026_05_17_0100__ngb_pm_platform_perf_read_path_indexes.sql`
- `V2026_08_26_0100__ngb_pm_read_path_indexes.sql`
- `V2026_09_15_0100__ngb_pm_queue_seek_index.sql`

The pack covers:

- `pm.party`
- `pm.property` (Building | Unit, parent_property_id, unit_no, DB-computed display)
- `pm.accounting_policy`
- `pm.receivable_charge_type`
- `pm.lease` + `pm.lease.parties`
- `pm.rent_charge`
- `pm.receivable_charge`
- `pm.receivable_payment`
- `pm.receivable_returned_payment`
- `pm.receivable_credit_memo` (standalone credit source)
- `pm.receivable_apply` (generalized credit_document_id + charge_document_id)
- `pm.bank_account`
- `pm.party` role flags (`is_tenant`, `is_vendor`)
- `pm.payable_charge`
- `pm.payable_payment`
- `pm.payable_credit_memo`
- `pm.payable_apply` (generalized credit_document_id + charge_document_id)
- PM DB guards / triggers / computed display / numbering refresh hooks

## Seed / init

Business defaults are intentionally **not** created by SQL migrations.
Use the PM migrator command instead:

- `seed-defaults`

This idempotent setup flow is responsible for initial CoA defaults, PM accounting policy,
default charge type, and operational register setup.

## Notes

- The PM migration pack depends on the `platform` pack.
- PM bootstrapper also installs standard typed-document immutability guards via
  `ngb_install_typed_document_immutability_guards()`.
- New databases apply the baseline and all subsequent migrations. Existing databases retain their
  migration history and apply pending scripts; do not delete or edit applied versioned migrations.
