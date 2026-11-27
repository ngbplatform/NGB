# Independent resource scheduling exercise

Implement the persistence slice of a PostgreSQL 16 resource scheduling application.

* Each resource has a permanent code, a mandatory nonblank contact, a reporting
  category, a business time zone and irreversible retirement.
* Movements of credits and usage rights are booked by business date. No daily
  closing may have negative funds or rights. Backdated entries and withdrawals
  must respect existing commitments. An earlier effective substitute is permitted.
  Report a resource's closing balance as of the selected business day.
* An assignment opens on a resource, finishes later and occupies a half-open
  interval. Prevent overlaps with existing intervals and refuse new work on a
  retired resource. Charge the agreed daily rate for each local calendar day
  touched, including both billing endpoints. Report by category at commencement.
  Administrative changes to zone or category must preserve charges.
* Each resource has independent history. Scheduling and charge reporting are
  also independent of the movement ledger. Only explicit movements post funds
  or rights. Opening and finishing work do not require or consume either balance.

Use schema `work_example`. Table `resource` has columns `id integer`,
`stable_code text`, `contact text`, `business_zone text`, `category text`,
and `retired boolean` in that order. A caller sets `assignment.finished`
by assignment ID. Provide these public entry points.

* `change_position(integer, date, integer, integer, bigint DEFAULT NULL)`
  returns a movement ID. To withdraw a prior movement the caller passes its ID
  as the last argument, NULL for the date, and zero for both deltas. The
  original event supplies the effective date.
* `credit_balance(integer, date)` returns the numeric closing balance.
* `movement_history(integer, date, bigint, integer)` returns active movements for
  that resource through the cutoff, ordered by ID. Return `movement_id` and
  `amount` (credit delta), starting strictly after the supplied cursor, limited
  to the requested page size. A cursor of zero reads the first page. The closing
  balance includes all qualifying movements across pages.
* A draft in `request` has a generated bigint `id`, an editable `beneficiary`
  (resource ID), a selected `funding_id` (movement ID), an `effective` business
  date, a positive integer `amount` and a nullable `confirmed_id`. Calling
  `confirm_request(bigint)` spends the amount for the current beneficiary,
  records the spending movement ID and returns it. The selected credit must
  belong to that beneficiary and be active and effective on the spending date.
  Refused confirmation leaves the draft and both balances intact.
* `begin_assignment(integer, timestamptz, numeric)` returns the assignment ID.
  Ending one assignment permits a successor at the same instant. Clearing
  the earlier finish after a successor starts must preserve exclusive occupancy.
* `completed_charges` exposes `id`, `captured_category` and `amount`.
* `resource_state(integer, timestamptz)` reports Available, Assigned or Retired
  as of the supplied instant. Preserve the first effective `retired_at` and
  forbid reactivation through the ordinary catalog edit.
* `adjust_position(integer, date, integer, text)` and
  `withdraw_adjustment(integer, bigint, text)` require an entered reason for
  each operation. Record movement ID, operation, database actor, time and
  reason in `adjustment_history` atomically with the change.

Exercise backdated spending with sufficient funds on the attempted date and
at the end, but insufficient funds on an intervening date. Exercise withdrawal
at 50 and 51 uses with the only unsupported use last in dependency order.

Use SQLSTATE 23514 for business refusals. List the obligations you extracted.
Deliver a fresh migration and a separate executable smoke suite covering
successful writes, refusals and a permitted replacement.
