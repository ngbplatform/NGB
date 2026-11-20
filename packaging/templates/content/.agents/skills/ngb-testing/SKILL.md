---
name: ngb-testing
description: Verify NGB application architecture, unit/integration/browser suites, strict coverage and independently specified business acceptance.
---

Use when implementing verification or diagnosing a failed development profile.
Do not infer complete certification from one passing suite or lower a failing gate.

For generated applications, use the [registered-inventory acceptance matrix](../../../ai/vendor/references/application-generation.md#acceptance-evidence).
Run all report endpoints, visible document chains and role scenarios. Test actual
withdrawal rejection with dependent documents; registration of a handler is not
proof that its check is invoked.
Create/edit/calculate/post browser scenarios must operate the real controls. API
setup is for prerequisites, not a substitute for the action being verified. Assert
resulting business values and exactly one navigation per click; a visible shell or
a screenshot does not prove the workflow. Follow the
[interactive acceptance sequence](../../../ai/vendor/references/interactive-workflows.md#browser-acceptance).
Include save/reopen followed by row edits, valid/rejected/cleared source changes,
late responses from every calculation stage, visible dependency-refusal reasons,
reference opening in readonly Lines and keyboard popup geometry. Use real shared
controls for these claims; service-boundary doubles do not prove end-to-end behavior.
Reconcile registered documents and roles with named, executed acceptance scenarios;
100% coverage of a handpicked subset is not complete workflow evidence.

1. Read [verification profiles](../../../ai/vendor/references/testing.md). Inspect `ngb.config.json`,
   discovered production/test projects and the exact source/package versions.
2. Choose `validate` for inspection, `validate --profile build` for resolved build
   checks, and `test --profile unit|integration|e2e|all` for the needed execution.
3. Define expected business/security outcomes independently of implementation.
   Use actual hosts/storage for persistence and permission claims.
   Compare JSON response values, including action replays, with semantic equality;
   serialized object property order is not an application contract.
   In browser bootstrap tests, call and await `startApplication` from
   `src/bootstrap.ts` per case; resetting Vitest modules does not reset native
   browser imports. See the working starter/example tests.
4. Inspect `.ngb/reports/<run-id>/report.json`, test discovery, missing/skipped checks,
   coverage inventory and report hashes. Diagnose gaps rather than hiding sources.
5. Rerun affected checks after fixes. Require `test --profile all` for complete
   starter acceptance and the declared performance checks for certification.

Done: required suites actually ran, per-file coverage is complete, negative cases
fail correctly, and evidence matches unchanged inputs. Report missing prerequisites.

Complete the [executed browser acceptance matrix](../../../ai/vendor/references/browser-acceptance.md) before declaring generation complete.
The E2E CLI gate must reconcile runtime documents and roles with current-run Playwright
UI evidence. Passing test counts or a prose checklist alone are insufficient.

Apply the [business outcome checks](../../../ai/vendor/references/business-outcome-checks.md)
and the named-outcome browser gate before claiming the generated workflow complete.
Classify relationships, dashboard metrics and roles, historical dimensions,
settlement and correction scenarios in the application plan. Give each competing
write and each replayable operation a dedicated executable case.
