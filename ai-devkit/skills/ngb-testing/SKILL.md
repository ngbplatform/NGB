---
name: ngb-testing
description: Verify NGB application architecture, unit/integration/browser suites, strict coverage and independently specified business acceptance.
---

Use when implementing verification or diagnosing a failed development profile.
Do not infer complete certification from one passing suite or lower a failing gate.

1. Read [verification profiles]({{references}}/testing.md). Inspect `ngb.config.json`,
   discovered production/test projects and the exact source/package versions.
2. Choose `validate` for inspection, `validate --profile build` for resolved build
   checks, and `test --profile unit|integration|e2e|all` for the needed execution.
3. Define expected business/security outcomes independently of implementation.
   Use actual hosts/storage for persistence and permission claims.
4. Inspect `.ngb/reports/<run-id>/report.json`, test discovery, missing/skipped checks,
   coverage inventory and report hashes. Diagnose gaps rather than hiding sources.
5. Rerun affected checks after fixes. Require `test --profile all` for complete
   starter acceptance and the declared performance checks for certification.

Done: required suites actually ran, per-file coverage is complete, negative cases
fail correctly, and evidence matches unchanged inputs. Report missing prerequisites.
