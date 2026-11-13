# Browser bootstrap tests in permission acceptance

Candidate `release-candidate-local-20261113-061611` passed the Kit application
profile in 447.6 seconds and Codex root document-change acceptance. The next
permission-correction scenario passed all five frozen HTTP checks and all three
independent browser permission checks, then failed two authored component tests:
`preserves server catalog write capabilities: false` and the matching `true` case.

The failure was `mount` receiving zero calls. The tests imported `src/main` after
an earlier test had already imported it, relying on `vi.resetModules()` to rerun
startup. [Vitest Browser Mode uses native ESM](https://v4.vitest.dev/guide/browser/);
the browser returns the cached module without repeating startup side effects.
The source example's query-suffixed imports had avoided this for its original
cases, but were not a sound pattern for extending the tests. This failure did not
establish an SDK authorization defect.

## Correction

- The example and starter expose `startApplication` from `src/bootstrap.ts`.
  Their unchanged browser entry point calls it once. Component tests explicitly
  invoke and await startup for each case; a separate test checks the entry point.
- The example retains both server capability cases, all startup error cases and
  strict per-file coverage. Application permissions and SDK sources are unchanged.
- The active permission seed now overlays `bootstrap.ts`, preserving exactly its
  two intentional mistakes. A regression test checks its hashes and verifies
  that correcting those two mistakes restores the current working example.
- The development runner executes frontend build/component coverage before HTTP
  and E2E hosts. It retains readable and JSON output and names failed assertions
  in the terminal error. All later acceptance checks remain required.

## Focused evidence

[Diagnostic artifacts](../../../artifacts/ai-devkit/diagnostics/browser-bootstrap-20261113)
retain the original failure and separate corrected results. The original source
reproduced exactly the two failed cases with 15 other component tests passing.
The corrected example passed 18/18 and starter 8/8; both typecheck/build commands
passed. Every frontend source file has 100% lines, branches, functions and
statements. Twenty-four targeted Node tests and skill validation passed; the
changed frontend evidence module has 100% line, branch and function coverage.

Running the new runner on a hash-verified copy of the original failed output
stopped in 43.9 seconds, named both failed tests and did not start HTTP tests,
authored backend tests or E2E hosts. The old candidate and its reports remain
unchanged. A manually corrected diagnostic copy is tested separately and must
not be counted as a successful independent model attempt.

The corrected diagnostic copy passed the complete permission scenario in 179.3
seconds: five frozen HTTP tests, three frozen browser tests, 111 authored unit
tests, 34 selected integration tests, 18 frontend component tests and 11 authored
browser tests. All ten frontend source files retained 100% coverage. This used
the same SDK/UI candidate packages and preserved the produced source inventory
through execution. The corrected application's source and manual diagnostic
review selection are recorded separately from the original agent attempt.

FULL Certification, full platform Backend/Frontend suites and full coverage were
not run. No new Codex/Claude inference or review was launched.
