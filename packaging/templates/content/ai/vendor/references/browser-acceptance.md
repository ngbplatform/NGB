# Executed browser acceptance

`node infrastructure/ngb.mjs test --profile e2e` and `--profile all` require
`ai/acceptance.json`. This is application-owned input, included in the execution
source hash. Keep it current as the application grows. An upgraded application
without the file fails acceptance with an actionable missing-file error; this
changes the Kit's quality gate, not a Platform API or SDK contract.

The CLI reads `/api/documents/metadata` and `/api/security/roles` from the disposable
application, using its trusted active administrator. It reconciles the plan with
all application documents and active roles, including `ngb-admin`. Application
documents remain mandatory even when missing from navigation. The SDK always
registers `general_journal_entry`; in the security-only starter this platform
definition is recorded separately in `platformOnly`. Once Accounting navigation
is composed, it joins the mandatory document inventory and must be tested through
its specialized journal UI. This fixed SDK distinction is not an application-owned
exclusion list. Seed the application's
business roles in the test migrator before readiness, using the same role codes as
normal deployment. Do not replace the runtime inventory with a hand-picked list.
Tests may create temporary roles later; those are not business-role inventory.

The disposable identity server has a private, run-specific administration client.
The CLI creates a separate user with exactly one application role for each role in
the inventory through the application's authorized user-management API. Tests read
the role-to-login map from `JSON.parse(process.env.NGB_TEST_ACTORS!)`, and use
`NGB_TEST_PASSWORD` only for this disposable realm. `ngb-admin` maps to the bootstrap
administrator. Do not grant all test users Administrator or bypass authorization.
Direct Playwright execution is useful for debugging; the CLI profile is the
acceptance gate because it owns inventory, identities and evidence reconciliation.

Example plan entry (repeat for every runtime document and required scenario):

```json
{
  "schemaVersion": 1,
  "documents": [
    { "type": "example.order", "sourceDriven": true, "priced": true, "asynchronous": true }
  ],
  "roles": ["ngb-admin", "example.operator", "example.reader"],
  "cases": [
    {
      "id": "order-source-change",
      "document": "example.order",
      "role": "example.operator",
      "scenario": "source-change-clear",
      "file": "order.spec.ts",
      "title": "changing and clearing the source resets dependent values",
      "project": ""
    }
  ]
}
```

This abbreviated example deliberately is not a passing matrix. Mandatory scenarios:

- `access` for **each document × role**, including expected denial for roles without
  permission. Exercise direct navigation as well as visible controls; do not merely
  assert that a menu item is absent.
- `draft-edit`: create/save/reopen and edit again, checking persisted inputs and
  derived headers. A restricted editor must verify and explain its restrictions.
- `validation`: missing/invalid input, visible business reason, correction and retry.
- `lifecycle` when Post is supported: Post, allowed Unpost/Repost, expected blocked
  reversal, version refresh, Effects and Flow for that document's business process.
- `lines` when runtime metadata has Parts: actual cell editing or justified readonly
  derived rows, add/remove where allowed, reference Open, alignment and keyboard use.
- `source-change-clear` and `lookup-eligibility` for source-driven documents: replace,
  clear, retry, empty/ineligible/unauthorized choices and selection after reopening.
- `pricing` for priced documents: effective date, defaults, permitted override,
  editable reason, rounding and preservation of persisted snapshots.
- `async-race` for asynchronous dependencies: delayed old success and failure after
  source/date replacement, clear, document reload and leaving the editor.

Classify these behaviors truthfully from the implementation and business process.
The generator must not turn a classification off to avoid writing its tests. The
checker can verify execution and runtime inventory, not infer every business rule
from arbitrary code. Tests must contain the actual domain assertions for each
scenario, including required reports and cross-document reconciliation. Those
business assertions remain an implementation responsibility.

Use the supplied `web/tests/e2e/acceptance.ts` helper with real SDK controls:

```typescript
const probe = await acceptanceProbe(page, testInfo) // before sign-in
await signIn(page, actors['example.operator'])
// Prepare prerequisites through API; perform the behavior under test through UI.
await page.getByLabel('Discount reason').fill('Approved customer adjustment')
await probe.value(page.getByLabel('Discount reason'), 'Approved customer adjustment')
// Add persistence, totals and lifecycle assertions as appropriate.
await probe.finish('order-source-change', 'example.operator')
```

`text`, `value` and `disabled` verify visible real locators. The probe observes
trusted browser input and the actual `/api/security/me/access` response. It rejects
inactive, anonymous and wrong-role actors; a business-role test cannot run with a
bootstrap administrator. Use one actor and one matrix case per test. `file` is
relative to Playwright's `testDir`; `project` matches the report's project name
(empty for the starter's unnamed project); `title` matches the test title exactly.

The CLI binds each inline `ngb-acceptance` attachment to that exact successful test,
project, current environment run ID and origin. Missing, duplicate, stale, skipped,
retried/flaky and failed cases fail the gate. API-only seed scripts and component
tests with stub controls do not produce acceptable browser proof. Do not mock
security, application HTTP or SDK controls in these E2E cases; delayed responses
for race tests may be held and released, but must originate from the real service.
Never manufacture attachments or treat the probe's single visible assertion as a
substitute for the scenario's complete domain assertions.

The raw Playwright report, inventory, source hash and gate results are retained in
`.ngb/reports/<run>/`. A passing browser gate is not FULL Certification. It does not
replace integration tests, per-file coverage, load tests or business review.
