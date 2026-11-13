# Document input validation in the agent evaluation

Candidate `release-candidate-local-20261112-232336` passed the Kit application
profile in 458.8 seconds and then failed the Codex root `existing-document-change`
acceptance. Six of its seven frozen HTTP tests passed. Creating a Draft with a
2001-character `completion_note` returned HTTP 500 instead of HTTP 400.

The agent added metadata `MaxLength: 2000` and a database CHECK, but no
`IDocumentDraftPayloadValidator`. In SDK 3.4.0, generic document Draft persistence
does not enforce metadata string length. The Post validator cannot reject Draft
input. The existing reference mentioned this distinction, but the short document
skill and working example did not demonstrate the complete head-field path.
The independent source review also accepted reliance on metadata validation.

## Correction

- The document skill and root guidance now route bounded text to the existing
  public payload-validator contract. The reference explains both methods, DI
  registration, raw head JSON versus typed parts, partial updates and HTTP 400.
- `ChecklistPayloadValidator` demonstrates head and part length validation on
  create/update using the limit already declared in its metadata. Incomplete
  Draft values remain valid. No SDK source or migration was changed.
- Independent source review explicitly checks the application payload path and
  DI registration for document input limits. Behavioral acceptance is unchanged.
- Unit and public HTTP tests cover the exact limit, oversized head/part input,
  case-insensitive names, nullable text, partial updates and preservation of
  document content, parts, version, audit and count after rejection.

## Focused evidence

[Diagnostic artifacts](../../../artifacts/ai-devkit/diagnostics/payload-validation-20261113)
contain 25 passing checklist unit tests and nine passing checklist HTTP tests.
Thirteen targeted guidance/source-review tests and the skill validator passed.
Focused coverage of `ChecklistPayloadValidator` is 23/23 lines and 26/26 branches.

A separate copy of the failed agent output first reproduced the same frozen
HTTP failure. Adding only a completion-note payload validator and its DI binding
then passed all seven frozen document tests and the four authored completion-note
tests. This diagnostic repair is not counted as successful independent agent
inference. The original failed attempt and candidate evidence remain unchanged.

A requested single fresh Codex scenario with independent Claude review did not
start: automatic approval review rejected the additional token usage and external
source transfer without separate confirmation. No new model acceptance is claimed.
FULL Certification, full Backend/Frontend suites and full coverage remain
maintainer-run checks; these focused results do not certify the whole release.

## Subsequent maintainer run

Candidate `release-candidate-local-20261113-061611` subsequently passed Codex root
`existing-document-change` inference, independent review and acceptance. The
candidate-bound result is retained under
[acceptance/682dd709-581d-4e20-a8fd-ed8abc9395e8](../../../artifacts/ai-devkit/evaluation-runs/a2e567b74e0bfbebc6cbdfe6795a455a826d4639e6abb3960776ec91a4239322/work/codex/root/existing-document-change/acceptance/682dd709-581d-4e20-a8fd-ed8abc9395e8).
That full run later stopped in a different scenario's frontend tests; see
[browser bootstrap testing](browser-bootstrap-2026-11-13.md).
