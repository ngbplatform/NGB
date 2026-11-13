# Independent example interruption on 12 November 2026

Candidate `release-candidate-local-20261112-202354` stopped in the Kit application
profile. The saved evidence is under
[application/dbc07f5b-f12a-483d-8748-551499e440e2](../../../artifacts/release-candidate-local-20261112-202354/certification/ai-kit/application/dbc07f5b-f12a-483d-8748-551499e440e2).
The candidate and its unsuccessful reports remain unchanged.

## Observed failure

The independent example's integration test process reached its 1,800,000 ms
deadline. It did not produce a complete TRX or coverage report. Before the timeout,
`InspectionAuthorizationTests.Limited_inactive_and_display_name_only_actors_are_denied`
reported HTTP 401 instead of the required HTTP 403 for `inactive-administrator`.
The host suppressed authentication logs, so that receipt does not establish why
authentication failed. It does not establish a platform authorization defect.

The example's 102 unit tests, 15 frontend tests and ten browser tests passed.
The subsequent capabilities failure was a consequence of incomplete example
evidence, not an independently discovered missing capability.

## Changes and targeted verification

- Independent example test projects now have the same 45-minute deadline as
  authored integration acceptance. The enclosing command remains bounded at one
  hour. There are no automatic test retries or relaxed acceptance expectations.
- Every command saves its deadline, exit status, elapsed time, stop reason and
  redacted output. Console test output includes individual case results. Complete
  TRX and coverage evidence remains mandatory for acceptance.
- The isolated example host retains warnings from JWT/Keycloak authentication.
  SDK code, token validation, authorization rules and the expected HTTP 403 are
  unchanged.
- Capabilities is recorded as `not-run` when examples fail; final candidate
  integrity is still checked and the application profile remains unsuccessful.

Five targeted Node tests passed. They cover success, nonzero exit, timeout,
cancellation before launch, and successful/failed prerequisite execution. The new
`example-execution.mjs` module has 100% line, branch and function coverage in that
targeted run.

The three denied-actor cases passed against the same candidate packages. A second
run of seven authorization/navigation/role-reuse cases also passed. After applying
the final logging filter, those seven cases passed again in 75.5 seconds, including
the inactive administrator's HTTP 403. The final isolated run's
[TRX](../../../artifacts/ai-devkit/diagnostics/authorization-20261113/final-tests/authorization.trx)
and [log](../../../artifacts/ai-devkit/diagnostics/authorization-20261113/final-test.log)
are retained with the diagnostic receipts.

The original HTTP 401 has not been reproduced and is not claimed as fixed.
These focused checks do not establish complete application acceptance. FULL
Certification, full platform Backend/Frontend tests and full coverage were not
run; the maintainer runs them independently. The updated template and CLI must be
used in a newly sealed candidate because source and package bytes have changed.
