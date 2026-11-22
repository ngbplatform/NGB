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
    { "type": "example.order", "sourceDriven": true, "priced": true, "asynchronous": true, "withdrawalDependencies": [] }
  ],
  "roles": ["ngb-admin", "example.operator", "example.reader"],
  "surfaces": ["document:example.order"],
  "businessChecks": [],
  "workflows": {
    "flows": [], "chains": [], "settlements": [], "adjustments": [], "historicalGroups": [],
    "concurrentWrites": [], "replays": [], "dashboards": [], "metrics": []
  },
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

Complete the [server acceptance bindings](server-acceptance.md) for integration evidence.
Populate each `workflows` array with requirement IDs from the original brief. An empty
array means that the application has no such business workflow. Review classification
against the brief before coding. Populate this section when upgrading an existing
schema 2 plan and collect fresh evidence. Bind each metric, replayable operation and
competing write independently. For dashboard access, cover every runtime role using
its own identity. An invalid plan fails before Playwright starts and retains its reason.

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
- `form-labels` for each metadata document or catalog editor with fields. Open the
  actual editor, expand the sections, and verify the visible field names for the
  whole editor using `accessibleFields(editor, [{ label, control }])` in the
  `named-fields` outcome. Scope the locator to the complete `[data-ngb-entity-form]`
  or its containing editor. Include auxiliary picker buttons and readonly inputs.
  Exercise conditional and line fields when present. A passing test requires
  accessible controls from both the application and the shared SDK.
- `pricing` for priced documents: effective date, defaults, permitted override,
  editable reason, rounding and preservation of persisted snapshots.
- `async-race` for asynchronous dependencies: delayed old success and failure after
  source/date replacement, clear, document reload and leaving the editor.
- `document-flow` for business document references in the header or parts, including
  manual entry, and for relationships created by actions. Bind a `document-chain`
  case to each multistep process in `workflows.chains`. Use `flow(type, id, links)`
  with independently seeded endpoint IDs and directed standard relationship codes.
  Open a related record in the tree and check its exact identity with `record(path, id)`.
  Include a named `reference:<field>` outcome for each persisted business lookup.
  Use `reference(type, id, field, expectedSourceIds, code)` to verify the saved
  header or part and the visible edges against the independently seeded sources.
  For a lookup used solely as an informational reference, document its purpose in
  the document entry's optional `informationalReferences` array of `{field, reason}`.
  Use the field key for headers and `partCode.fieldKey` for lines. Mirrored fields
  always require Flow coverage.
- The built-in general journal uses `reversal`. Post, use the journal's Reverse action,
  open the separate reversal, verify the original and reconcile the combined effects.
  This applies to older SDK releases whose generic metadata advertised Unpost as well.

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

Use `settlement`, `partial-adjustment`, `historical-classification`,
`dashboard-access`, `dashboard-complete`, `concurrent-operation` and `request-replay`
for the corresponding workflows. The SDK indirect cash flow statement can use
`summary-report` for its aggregate output. The SDK integrity report can use
`diagnostic-report` for a healthy, empty result. Execute both for the expected
scope. When the brief requires actionable details or drill-down, add a populated
case exercising those details or record the unmet requirement.

Use `figure(locator, expected)` with a precomputed formatted amount or count,
`record(path, id)` for a particular entity and `metricReport(tile, code, parameters, filters)`
to click the dashboard figure and verify the resulting report request. A figure
opening a standard document or catalog list can use `metricList` with the expected
query parameters. Include
every filter, its value and the applicable descendant option. A report-to-report
drill-down may use `reportRecord` with the target record ID in the submitted filters
or parameters. For dashboard roles, use `permissions` to verify allowed values
and omitted restricted fields in the response or a forbidden request. Use
`navigation(control, path)` to open a permitted link or pass null to verify a
restricted link is hidden. Also verify authorization on direct navigation. Use `concurrent` against the same
business key and `replay` followed by `conflictingReplay` against the same endpoint.
Check the committed version, balances, effects and history after reopening.

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

## Document outcomes in schema version 2

Declare each document's `flowAmountField` for a business total, or use null for
a document containing numeric fields without a total. Record the business meaning
of those fields in `flowAmountReason`, including numeric line fields. Use `flowAmount(type, id,
expected, root?)` for `flow-amount:<type>`. Supply the root when checking a
linked document. Compute the expected amount independently of the graph.

Bind `journal-draft` to `journalDraft(memo)` in the typed journal's draft-edit
case. For other configured forms list the declared custom `commands`. In an
administrator draft-edit case call `draftCommands(response, type, id, codes)`
on the loaded editor-state response, including an empty list. For each command
exercise `commandChange(type, id, code, trigger, field, before, after)` with an
observable persisted result. Verify the dirty form protects existing edits too.
The inventory outcome is `commands:<type>/<JSON sorted codes>`; each individual
outcome is `command:<type>/<code>`.

List `operationalRegisters` and `referenceRegisters` by document. Call
`postedRegisters(response, type, id, operational, reference)` on the Effects
response for each distinct posting outcome. Its key includes both sorted arrays,
for example `registers:example.approval/[[],[]]` for a posting with no registers.
Their union must equal the declared inventory. For each reference register use
`registerFields(response, type, id, code, recordId, expected)` for
`register:<type>/<code>`. Supply a field-keyed map of expected labels and display
strings for that row. Check a document reference and a date wherever applicable.
Use `literalText: true` only for business text whose exact stored spelling is
significant, such as a supplier's opaque tracking code.

The raw Playwright report, inventory, source hash and gate results are retained in
`.ngb/reports/<run>/`. A passing browser gate is not FULL Certification. It does not
replace integration tests, per-file coverage, load tests or business review.

## Reference columns, dashboard layout and request failures

Import `test` from the project's shared `tests/e2e/browser.ts` fixture in every
spec. It records uncaught exceptions and rejected promises from the browser
context in `ngb-page-errors`. Intentional HTTP errors can exercise recovery.
The application must still handle the promise and show the required state. Wait
for delayed success and failure responses before ending race tests.

* Bind `report-reference:<display-field>` to `reportReference(field, control, display, target, id)`.
  Select a real detail cell, open the expected document/catalog path, or pass a
  report destination with `code`, `parameters` and `filters` that identifies the record.
* Bind `dashboard-home` to `dashboardHome(control, path)` on the actual sidebar item.
  Use `dashboard-layout:desktop` and `dashboard-layout:mobile` with
  `dashboardLayout(size, toolbar, attention, metrics)`. It captures PNGs at
  desktop and phone widths, checks element placement and checks for overflow.
  Also exercise the existing metric-scope and authorization outcomes.
* Bind `business-calendar` to `businessCalendar([first, second], expectedDate)`.
  Use two authenticated browser contexts at the same instant on different local
  calendar dates. Compare the visible reporting day to the independently known
  business calendar. Check explicitly selected historical dates separately.
* For every journal access case use `journal-create-access` with `journalCreateAccess`.
  Verify Create and Save for a creator. For a reader with actual journal view
  permission, first load the list successfully, then verify the denied direct
  POST, list control and direct new-entry route. A fully unauthorized actor
  should be tested separately where the role model calls for one.
