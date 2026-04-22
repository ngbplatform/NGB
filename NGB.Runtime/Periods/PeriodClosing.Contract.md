# Period Closing Contract

`Close Month`

- Input period is normalized to the first day of the month.
- The operation is atomic: balances, the closed-period marker, and business audit are committed together.
- The period advisory lock serializes close-vs-close and close-vs-posting races for the same month.
- Month closing is sequential once accounting activity exists: the service closes only the next valid month in the chain and rejects gaps.
- If legacy out-of-sequence closed months exist, new closes are blocked until the chain is repaired from the latest closed month backward.
- A closed month is immutable for subsequent posting/unposting/reposting writes through runtime and DB guards.
- `closedBy` is required and is treated as audit data, not an optional display field.

`Reopen Month`

- Reopen is explicit, audited, and reason-based.
- Only the latest closed month can be reopened.
- Reopen is blocked if fiscal-year close has already been posted for the same month.
- Reopen removes only the closed-period marker; it does not silently rebuild balances or delete fiscal-year documents.

`Close Fiscal Year`

- `fiscalYearEndPeriod` is the open month that will receive the closing entries.
- All prior months from January through `fiscalYearEndPeriod - 1 month` must already be closed.
- Fiscal-year close is blocked if later months are already closed, because closing entries must not be posted "into the past" behind a later frozen month.
- The operation is idempotent per end period via deterministic `documentId = CloseFiscalYear|yyyy-MM-dd`.
- Concurrency is serialized by fiscal-year-window advisory locks (`January .. fiscalYearEndPeriod`, inclusive) plus accounting posting-state dedupe.
- Closing is performed per trial-balance row `(account + dimension set)`; retained earnings uses empty dimensions.
- It is valid for the close to be a no-op from an entry-writing perspective; posting state and audit are still recorded.

`Reopen Fiscal Year`

- Reopen is explicit, audited, and reason-based.
- Reopen is allowed only for a currently completed fiscal-year close.
- Reopen is blocked if any later month is already closed, because later frozen balances would depend on the closing entries.
- If the fiscal year end month is currently closed, reopen opens that month atomically as part of the operation.
- Reopen removes the current fiscal-year close effect by deleting the synthetic closing entries for the deterministic fiscal-year document, rebuilding monthly turnovers from the ground-truth register, deleting the end-month balance snapshot, and clearing only the mutable current posting-state row.
- Immutable history is preserved: posting-state history and business audit rows are never deleted.

`UI / API expectations`

- UX should surface a year calendar with explicit month states, not just isolated action buttons.
- UX should show `Latest contiguous closed`, `Next closable`, and any chain-repair warning if `HasBrokenChain=true`.
- UI / API should derive operator identity from the authenticated current user, not from a free-form "Run As" field.
- UX should treat `period.month.prerequisite_not_met`, `period.month.later_closed_exists`, `period.month.reopen.latest_closed_required`, `period.month.reopen.fiscal_year_closed`, `period.fiscal_year.already_closed`, `period.fiscal_year.not_closed`, `period.fiscal_year.in_progress`, `period.fiscal_year.reopen.in_progress`, `period.fiscal_year.prerequisite_not_met`, `period.fiscal_year.later_closed_exists`, `period.fiscal_year.reopen.later_closed_exists`, and `period.already_closed` as expected operator-facing states.
- Clients should disable duplicate submissions while a close request is in flight.
- Fiscal-year close UX should prefer eligible retained earnings accounts only: active, not deleted, equity-section, credit-normal.
