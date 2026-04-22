# NGB.PropertyManagement.PostgreSql

This project is the **Property Management** migration pack (Evolve).

## Where migrations live

- Place SQL scripts under `db/migrations/`.
- Scripts are embedded resources.

This pack now uses a **single clean-slate PM baseline** for recreated databases:

- `V2026_03_13_1000__ngb_pm_final_clean_baseline.sql`

The baseline contains the final PM schema state for:

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
- For recreated / non-production environments, keep only the final clean baseline and delete the
  older PM versioned migrations that it supersedes.
