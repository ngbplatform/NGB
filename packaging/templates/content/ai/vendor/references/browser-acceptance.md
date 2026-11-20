# Executed browser acceptance

`node infrastructure/ngb.mjs test --profile e2e` and `--profile all` require
`ai/acceptance.json`. This is application-owned input, included in the execution
source hash. Keep it current as the application grows. An upgraded application
without the file fails acceptance with an actionable missing-file error; this
changes the Kit's quality gate, not a Platform API or SDK contract.

The CLI reads document/catalog metadata, report definitions, main-menu destinations
and security roles from the disposable application, using its trusted active
administrator. It reconciles schema 2 with every registered surface and active
role, including `ngb-admin`. A business application must use schema 2; schema 1
is accepted only for the empty security-only starter. Existing application plans
need an explicit migration, not silent conversion of old evidence. Application
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

Example plan fragment (repeat for every runtime surface and required scenario):

```json
{
  "schemaVersion": 2,
  "documents": [
    { "type": "example.order", "sourceDriven": true, "priced": true, "asynchronous": true }
  ],
  "roles": ["ngb-admin", "example.operator", "example.reader"],
  "surfaces": ["document:example.order"],
  "requirements": [
    {
      "id": "order-source-consistency",
      "description": "Changing an order source replaces the customer and charges; clearing it removes all derived values.",
      "scenarios": ["source-change-clear"],
      "cases": ["order-source-change"]
    }
  ],
  "cases": [
    {
      "id": "order-source-change",
      "target": "document:example.order",
      "role": "example.operator",
      "scenario": "source-change-clear",
      "file": "order.spec.ts",
      "title": "changing and clearing the source resets dependent values",
      "project": "",
      "outcomes": ["replacement-header", "replacement-lines", "cleared-header", "cleared-lines", "cleared-totals"]
    }
  ]
}
```

This abbreviated example deliberately is not a passing matrix. Surface identities
are `document:<documentType>`, `catalog:<catalogType>`, `report:<reportCode>` and
`navigation:<menuItemCode>`. Do not omit hidden registered documents or catalogs.
Each case binds one target, actor and test. Each business requirement declares its
applicable scenarios and executable cases; every case must belong to a requirement.
Required outcomes cannot be left with an empty case list or declared complete in
prose while their tests are absent.

Mandatory scenarios:

- `access` for **each document × role**, including expected denial for roles without
  permission. Exercise direct navigation as well as visible controls; do not merely
  assert that a menu item is absent.
- `draft-edit`: create/save/reopen and edit again, checking persisted inputs and
  derived headers. A restricted editor must verify and explain its restrictions.
- `validation`: missing/invalid input, visible business reason, correction and retry.
- `lifecycle` when Post is supported: successful Post, allowed Unpost, repeated Post
  and restored Effects (`posted`, `unposted`, `reposted`, `effects-restored`). Test
  genuine blocked reversal separately with `withdrawal-blocked`: concrete reason,
  unchanged effects and the permitted correction path. A refusal is not successful
  withdrawal. If the business specification explicitly makes the document
  irreversible and its metadata disables Unpost, demonstrate the authorized
  correction and its effects with `withdrawal-blocked`. Verify version refresh
  and Flow for the business process as well.
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

In addition, every document and catalog needs `search`. Bind `opened-target`
and the actual identity used: `number-or-code` for a numbered document, `name`
for an unnumbered document or catalog. Include a separate `catalog-code` outcome
when the catalog has a business code distinct from its name. Every report needs
`report`, and every destination needs `navigation`. Test denied search separately
with `search-denial` using a restricted actor; it must also find a permitted record.
Classify the business requirements for `numbering-date`, `temporal-restoration`,
`prerequisite-withdrawal`, `dashboard-complete`, `historical-as-of` and `shell`
where applicable. See [business outcome checks](business-outcome-checks.md) for
concrete cases and `infrastructure/ngb/acceptance-outcomes.mjs` for the authoritative
named outcomes. Do not remove a required scenario to make the gate green.

Use the supplied `web/tests/e2e/acceptance.ts` helper with real SDK controls:

```typescript
const probe = await acceptanceProbe(page, testInfo) // before sign-in
await signIn(page, actors['example.operator'])
// Prepare prerequisites through API; perform the behavior under test through UI.
await selectSource(page, replacementSource)
await probe.outcome('replacement-header', async checks => {
  await checks.value(page.getByLabel('Customer'), replacementCustomer)
  await checks.value(page.getByLabel('Currency'), 'USD')
})
await probe.outcome('replacement-lines', async checks => {
  await checks.text(page.getByTestId('charge-total'), '$120.00')
  await checks.count(page.getByTestId('charge-row'), 2)
})
await clearSource(page)
await probe.outcome('cleared-header', async checks => {
  await checks.value(page.getByLabel('Customer'), '')
  await checks.value(page.getByLabel('Currency'), '')
})
await probe.outcome('cleared-lines', checks => checks.count(page.getByTestId('charge-row'), 0))
await probe.outcome('cleared-totals', checks => checks.text(page.getByTestId('charge-total'), '$0.00'))
await probe.finish('order-source-change', 'example.operator')
```

Use the application's actual locators and verify every field in its dependency
chain, not just the illustrative fields above. `outcome` must execute at least one
check and cannot be repeated. The attached outcome set must exactly match the
case plan; a plain assertion or another scenario's outcome cannot fill a gap.
`text`, `value` and `disabled` require visible real locators; `count` proves presence
or absence, and `url` verifies exact navigation. The probe observes trusted browser
input and the real `/api/security/me/access` response. It rejects inactive,
anonymous and wrong-role actors; business-role evidence cannot run with a bootstrap
administrator. Use one actor and one matrix case per test. `file` is relative to
Playwright's `testDir`; `project` matches its report project name (empty for the
starter's unnamed project); `title` matches the test title exactly.

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
