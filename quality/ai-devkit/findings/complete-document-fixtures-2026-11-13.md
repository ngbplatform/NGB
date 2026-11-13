# Shared complete-document test input

Candidate `release-candidate-local-20261113-071431` passed the Kit application
profile in 448.5 seconds. Codex root document-change acceptance passed its seven
frozen HTTP cases, but one of 89 selected regression cases failed:
`TypedDocumentGuardTests.Posted_document_storage_rejects_direct_deletion_and_rolls_back_head_and_parts(checklist: False)`.

The generated application correctly required a completion note before Post.
Its author updated the shared complete-document helper and several duplicated
positive setups, but missed the storage-guard setup. That test received HTTP 400
while preparing its Posted document, before attempting the protected deletion.
This was missing test input, not a failure of the SDK's typed-document guard.

## Correction

`InspectionRequests.CompleteInspectionPayload` now supplies valid inspection
input for lifecycle creation, batch posting, reports and typed-storage guards.
`CreateCompleteInspectionAsync` uses the same factory. Future Post requirements
can be added in this explicit positive setup instead of being repeated throughout
the suite. The document skill and testing reference explain the distinction.

Generic HTTP/action helpers still send exactly the requested payload. Incomplete
Drafts, invalid-input tests and frozen migration records retain their explicit
data. No assertion, SDK source, migration or independent acceptance condition was
changed.

## Verification

[Diagnostic artifacts](../../../artifacts/ai-devkit/diagnostics/complete-document-20261113)
retain a reproduction against an exact copy of the failed agent source: one
guard case failed with the same missing-note HTTP 400, while the checklist case
passed. The original candidate and model receipts were not modified.

The canonical example passed all 31 tests in the nine affected regression
classes. Thirteen targeted guidance/oracle/review tests and skill validation
passed. A separately reviewed diagnostic copy of the generated application uses
the common factory with the new completion note; its complete document acceptance
is saved independently. A manual diagnostic repair is not a successful model
inference and does not replace the original failed receipt.

The corrected diagnostic copy passed full document-scenario acceptance in 220.7
seconds: seven frozen HTTP tests, 120 unit tests, four new integration cases and
all 89 selected regression cases, including both typed-storage guard variants.
The frontend component and E2E receipts, complete coverage and unchanged source
inventory are retained in `corrected-acceptance/acceptance.json`. The diagnostic
repair changed only nine test source files; generated application business logic
and the candidate's SDK packages were unchanged.

FULL Certification, full platform Backend/Frontend suites and full coverage were
not run. No new Codex/Claude calls were made.
