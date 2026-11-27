import assert from 'node:assert/strict'
import test from 'node:test'
import { acceptancePlan, browserAcceptance, readAcceptanceInventory, reconcileAcceptance } from '../../packaging/templates/content/infrastructure/ngb/acceptance.mjs'
import { businessReferences, reconcileSurfaces, reconcileWorkflows, scenarioOutcomes, validateOutcomes, verifyOutcomeProof } from '../../packaging/templates/content/infrastructure/ngb/acceptance-outcomes.mjs'

function fixture() {
  const inventory = {
    documents: [{ documentType: 'example.order' }],
    catalogs: [{ catalogType: 'example.customer' }],
    reports: [{ reportCode: 'example.balance', description: 'Balances by account.' }],
    menu: [{ code: 'orders' }],
    roles: ['ngb-admin'],
  }
  const surfaces = ['document:example.order', 'catalog:example.customer', 'report:example.balance', 'navigation:orders']
  const bindings = [
    ...['access', 'draft-edit', 'validation', 'lifecycle', 'document-print', 'search'].map(kind => [surfaces[0], kind]),
    [surfaces[1], 'search'], [surfaces[2], 'report'], [surfaces[3], 'navigation'],
  ]
  const cases = bindings.map(([target, scenario], index) => ({
    id: `case-${index}`, target, scenario, role: 'ngb-admin', file: 'business.spec.ts',
    title: `business outcome ${index}`, project: '',
    outcomes: scenario === 'search' ? ['opened-target', target === surfaces[0] ? 'number-or-code' : 'name']
      : [...scenarioOutcomes[scenario]],
  }))
  const plan = {
    schemaVersion: 2, documents: [{ type: 'example.order', withdrawalDependencies: [], sourceDriven: false, priced: false, asynchronous: false }],
    roles: inventory.roles, surfaces, cases, businessChecks: [],
    workflows: { flows: [], chains: [], settlements: [], adjustments: [], historicalGroups: [],
      concurrentWrites: [], replays: [], dashboards: [], metrics: [] },
    requirements: [{ id: 'business-1', description: 'Find and correct an order, retaining its audit trail.',
      scenarios: [...new Set(cases.map(item => item.scenario))], cases: cases.map(item => item.id) }],
  }
  return { plan, inventory }
}

test('business acceptance reconciles hidden catalogs, reports and navigation in addition to documents', () => {
  const { plan, inventory } = fixture()
  acceptancePlan(plan)
  reconcileAcceptance(plan, inventory)
  for (const key of ['catalogs', 'reports', 'menu']) {
    const changed = structuredClone(inventory)
    changed[key].push({ catalogType: 'hidden', reportCode: 'hidden', code: 'hidden' })
    assert.throws(() => reconcileAcceptance(plan, changed), /every runtime/)
  }
  for (const entry of plan.cases.slice(-4)) {
    const changed = structuredClone(plan)
    changed.cases = changed.cases.filter(item => item.id !== entry.id)
    changed.requirements[0].cases = changed.cases.map(item => item.id)
    assert.throws(() => reconcileAcceptance(changed, inventory), /Missing surface acceptance|no executable case/)
  }
})

test('every standard document requires print proof for its own saved record, including the default editor', () => {
  const { plan, inventory } = fixture()
  const print = plan.cases.find(entry => entry.scenario === 'document-print')
  const observed = target => ({ outcomes: print.outcomes, evidence: { 'printed-sheet': ['document-print:' + target] } })
  assert.throws(() => verifyOutcomeProof(print, observed('document:example.invoice')), /document-print:document:example.order/)
  verifyOutcomeProof(print, observed(print.target))

  plan.cases = plan.cases.filter(entry => entry !== print)
  plan.requirements[0].cases = plan.cases.map(entry => entry.id)
  plan.requirements[0].scenarios = plan.requirements[0].scenarios.filter(scenario => scenario !== 'document-print')
  assert.throws(() => reconcileAcceptance(plan, inventory), /example.order\/printed-sheet/)
})

test('a denial cannot stand in for allowed withdrawal and one cleared line cannot stand in for the whole dependency chain', () => {
  for (const scenario of ['lifecycle', 'source-change-clear', 'async-race', 'pricing', 'temporal-restoration',
    'prerequisite-withdrawal', 'dashboard-complete', 'historical-as-of', 'numbering-date']) {
    const entry = { id: scenario, target: 'document:example.request', outcomes: scenarioOutcomes[scenario] }
    verifyOutcomeProof(entry, { outcomes: entry.outcomes,
      evidence: { 'revised-price': ['posted-price:example.request'], 'metric-scope': ['metric-drilldown'], 'reconciled-total': ['figure'], 'current-retry': ['current-retry'] } })
    if (scenario === 'async-race') {
      assert.throws(() => verifyOutcomeProof(entry, { outcomes: entry.outcomes, evidence: { 'current-retry': ['text'] } }), /current-retry/)
    }
    for (const missing of entry.outcomes) {
      assert.throws(() => verifyOutcomeProof(entry, { outcomes: entry.outcomes.filter(item => item !== missing) }),
        /every expected outcome/)
    }
  }
})

test('record search proves the supported identity and report descriptions fit the header', () => {
  const { plan, inventory } = fixture()
  const doc = plan.cases.find(entry => entry.scenario === 'search' && entry.target.startsWith('document:'))
  doc.outcomes = ['opened-target', 'name']
  assert.throws(() => reconcileAcceptance(plan, inventory), /Missing search by number-or-code/)
  inventory.documents[0].presentation = { hasNumber: false }
  reconcileAcceptance(plan, inventory)
  const catalog = plan.cases.find(entry => entry.scenario === 'search' && entry.target.startsWith('catalog:'))
  catalog.outcomes = ['opened-target']
  assert.throws(() => reconcileAcceptance(plan, inventory), /Missing search by name/)
  catalog.outcomes.push('name')
  for (const text of [undefined, ' ', 'x'.repeat(161), 'First line\nSecond line']) {
    inventory.reports[0].description = text
    assert.throws(() => reconcileAcceptance(plan, inventory), /short, single-line description/)
  }
})

test('a document with explicitly disabled withdrawal must prove the refusal and a usable correction', () => {
  const { plan, inventory } = fixture()
  inventory.documents[0].capabilities = { canPost: true, canUnpost: false }
  assert.throws(() => reconcileAcceptance(plan, inventory), /withdrawal-blocked/)
  const entry = plan.cases.find(item => item.scenario === 'lifecycle')
  entry.scenario = 'withdrawal-blocked'
  entry.outcomes = scenarioOutcomes[entry.scenario]
  plan.requirements[0].scenarios = plan.requirements[0].scenarios.map(kind =>
    kind === 'lifecycle' ? entry.scenario : kind)
  reconcileAcceptance(plan, inventory)
})

test('required business outcomes cannot be deferred, unbound or replaced with unrelated green tests', () => {
  for (const mutate of [
    plan => { plan.requirements[0].cases = [] },
    plan => { plan.requirements[0].scenarios.push('historical-as-of') },
    plan => { plan.requirements[0].cases.push('not-run') },
    plan => { plan.requirements = [] },
    plan => { plan.requirements[0].description = '' },
    plan => { plan.requirements = null },
    plan => { plan.requirements[0].id = '' },
    plan => { plan.requirements[0].scenarios = [] },
    plan => { plan.requirements[0].scenarios = ['unknown'] },
    plan => { plan.requirements.push(plan.requirements[0]) },
    plan => { plan.cases[3].outcomes = ['reason'] },
    plan => { plan.cases[3].outcomes.push('posted') },
    plan => { plan.cases[0].target = 'catalog:missing' },
  ]) {
    const { plan } = fixture()
    mutate(plan)
    assert.throws(() => acceptancePlan(plan))
  }
})

test('outcome contracts reject malformed evidence and never invent absent runtime surfaces', () => {
  const { plan } = fixture()
  plan.requirements = []
  plan.cases[0].scenario = 'unknown'
  assert.throws(() => validateOutcomes(plan), /Unknown outcome scenario/)
  assert.throws(() => verifyOutcomeProof({ id: 'case', outcomes: ['posted'] }, {}), /Missing named/)
  assert.throws(() => verifyOutcomeProof({ id: 'case', outcomes: ['posted'] }, { outcomes: ['posted', 'posted'] }))
  reconcileSurfaces({ surfaces: [], cases: [] }, { documents: [] })
  const missing = fixture().plan
  missing.cases = missing.cases.filter(entry => entry.scenario !== 'search')
  assert.throws(() => reconcileSurfaces(missing, fixture().inventory), /Missing surface/)
})

test('schema 2 binds the named outcomes to each successful current-run browser case', () => {
  const { plan, inventory } = fixture()
  const context = { runId: 'current-run', origin: 'http://localhost:3000' }
  const report = { errors: [], suites: [{ specs: plan.cases.map(entry => ({
    ok: true, file: entry.file, title: entry.title, tests: [{ projectName: '', status: 'expected', expectedStatus: 'passed',
      results: [{ status: 'passed', retry: 0, attachments: [{ name: 'ngb-acceptance', contentType: 'application/json',
        body: Buffer.from(JSON.stringify({ schemaVersion: 2, ...context, caseId: entry.id, role: entry.role,
          subject: 'subject', url: context.origin, interactions: 2, assertions: entry.outcomes.length,
          outcomes: entry.outcomes, evidence: { figures: ['figure'], 'source-target': ['record'],
            'opened-target': ['record'], 'printed-sheet': ['document-print:' + entry.target] } })).toString('base64') }] }] }],
  })) }], stats: { expected: plan.cases.length, unexpected: 0, flaky: 0, skipped: 0 } }
  for (const spec of report.suites[0].specs) {
    spec.tests[0].results[0].attachments.push({ name: 'ngb-page-errors', contentType: 'application/json',
      body: Buffer.from('[]').toString('base64') })
  }
  const diagnostics = report.suites[0].specs[0].tests[0].results[0].attachments.at(-1)
  diagnostics.body = Buffer.from('["Unhandled request rejection"]').toString('base64')
  assert.throws(() => browserAcceptance(plan, inventory, report, context), /Unhandled browser page error/)
  diagnostics.body = Buffer.from('[]').toString('base64')
  assert.equal(browserAcceptance(plan, inventory, report, context).cases.length, plan.cases.length)
  const attachment = report.suites[0].specs[3].tests[0].results[0].attachments[0]
  const proof = JSON.parse(Buffer.from(attachment.body, 'base64'))
  proof.outcomes = ['reason']
  attachment.body = Buffer.from(JSON.stringify(proof)).toString('base64')
  assert.throws(() => browserAcceptance(plan, inventory, report, context), /every expected outcome/)
})

test('every persisted business reference requires its own graph evidence, including an unmirrored line lookup', () => {
  const { inventory, plan } = fixture()
  const metadata = inventory.documents[0]
  const lookup = { kind: 'document', documentTypes: ['source'] }
  metadata.form = { sections: [{ rows: [{ fields: [
    { key: 'base_id', lookup, mirroredRelationship: { relationshipCode: 'based_on' } },
    { key: 'info_id', lookup }, { key: 'customer_id', lookup: { kind: 'catalog' } },
  ] }] }] }
  metadata.parts = [{ partCode: 'lines', list: { columns: [{ key: 'source_id', lookup }] } }]
  const informational = [{ field: 'info_id', reason: 'Optional comparison with another document.' }]
  plan.documents[0].informationalReferences = informational
  assert.deepEqual(businessReferences(metadata, informational), ['base_id', 'lines.source_id'])
  assert.throws(() => businessReferences(metadata, [{ ...informational[0], field: 'base_id' }]), /mirrored/)
  assert.throws(() => businessReferences(metadata, [{ ...informational[0], field: 'missing' }]), /Unknown/)
  assert.throws(() => reconcileAcceptance(plan, inventory), /Missing document-flow proof.*base_id/)
  const entry = { ...plan.cases[0], id: 'linked', title: 'source links', scenario: 'document-flow',
    outcomes: [...scenarioOutcomes['document-flow'], 'reference:base_id'] }
  plan.cases.push(entry)
  plan.requirements[0].cases.push(entry.id)
  plan.requirements[0].scenarios.push(entry.scenario)
  plan.workflows.flows.push(plan.requirements[0].id)
  assert.throws(() => reconcileAcceptance(plan, inventory), /lines.source_id/)
  assert.throws(() => verifyOutcomeProof(entry, { outcomes: entry.outcomes,
    evidence: { 'linked-records': ['flow'], 'linked-target': ['record'], 'reference:base_id': ['flow'] } }), /reference:base_id/)
})

test('journal reversal and working-form labels remain mandatory for legacy and corrected metadata', () => {
  for (const canUnpost of [false, true]) {
    const { inventory, plan } = fixture()
    inventory.documents[0] = { documentType: 'general_journal_entry', capabilities: { canPost: true, canUnpost } }
    plan.documents[0].type = 'general_journal_entry'
    plan.surfaces[0] = 'document:general_journal_entry'
    for (const entry of plan.cases.filter(item => item.target.startsWith('document:'))) entry.target = plan.surfaces[0]
    assert.throws(() => reconcileAcceptance(plan, inventory), /general_journal_entry\/reversal/)
    const journal = plan.cases.find(entry => entry.scenario === 'lifecycle')
    journal.scenario = 'reversal'
    journal.outcomes = scenarioOutcomes.reversal
    plan.requirements[0].scenarios = plan.requirements[0].scenarios.map(name => name === 'lifecycle' ? 'reversal' : name)
    assert.throws(() => reconcileAcceptance(plan, inventory), /journal-draft/)
    plan.cases.find(entry => entry.scenario === 'draft-edit').outcomes.push('journal-draft')
    assert.throws(() => reconcileAcceptance(plan, inventory), /Verify journal Create access/)
    for (const entry of plan.cases.filter(item => item.scenario === 'access' && item.target === plan.surfaces[0])) {
      entry.outcomes.push('journal-create-access')
    }
    const target = plan.surfaces[0]
    assert.throws(() => reconcileAcceptance(plan, inventory), /Missing form-labels browser test for document:general_journal_entry/)
    const labels = { ...journal, id: 'journal-labels', title: 'Journal field labels', scenario: 'form-labels', outcomes: ['named-fields'] }
    plan.cases.push(labels)
    plan.requirements[0].cases.push(labels.id)
    plan.requirements[0].scenarios.push(labels.scenario)
    assert.ok(reconcileAcceptance(plan, inventory).includes(target + '/form-labels'))
  }
})

test('summary and empty diagnostic cases only count for the corresponding built-in report', () => {
  for (const [code, scenario] of [['accounting.cash_flow_statement_indirect', 'summary-report'], ['accounting.consistency', 'diagnostic-report']]) {
    const { inventory, plan } = fixture()
    const report = plan.cases.find(entry => entry.scenario === 'report')
    report.scenario = scenario
    assert.throws(() => reconcileSurfaces(plan, inventory), /Missing surface acceptance/)
    inventory.reports[0].reportCode = code
    plan.surfaces[2] = report.target = `report:${code}`
    reconcileSurfaces(plan, inventory)
  }
})

test('each dashboard role and each competing or replayable operation has evidence of its own', () => {
  const { inventory, plan } = fixture()
  inventory.menu[0].route = '/dashboard'
  assert.throws(() => reconcileWorkflows(plan, inventory), /Missing role coverage/)
  const dashboard = { ...plan.cases.at(-1), id: 'dashboard-admin', scenario: 'dashboard-access' }
  plan.cases.push(dashboard)
  plan.requirements[0].cases.push(dashboard.id)
  plan.requirements[0].scenarios.push(dashboard.scenario)
  assert.throws(() => reconcileWorkflows(plan, inventory), /workflows.dashboards/)
  plan.workflows.dashboards.push(plan.requirements[0].id)
  inventory.roles.push('reader')
  assert.throws(() => reconcileWorkflows(plan, inventory), /orders\/reader/)
  plan.cases.push({ ...dashboard, id: 'dashboard-reader', role: 'reader' })
  plan.requirements[0].cases.push('dashboard-reader')
  reconcileWorkflows(plan, inventory)
  for (const [kind, scenario] of [['replays', 'request-replay'], ['concurrentWrites', 'concurrent-operation'], ['metrics', 'dashboard-complete']]) {
    const duplicate = structuredClone(plan)
    duplicate.cases.push({ ...dashboard, id: 'one-test', scenario })
    for (const id of ['first', 'second']) {
      duplicate.workflows[kind].push(id)
      duplicate.requirements.push({ id, scenarios: [scenario], cases: ['one-test'] })
    }
    assert.throws(() => reconcileWorkflows(duplicate, inventory), /dedicated/)
  }
})

test('financial, navigation and graph outcomes reject generic assertions and mismatched check types', () => {
  for (const [outcome, check] of [['remaining-balance', 'figure'], ['final-residual', 'figure'], ['original-group-total', 'figure'],
    ['linked-chain', 'chain'], ['source-target', 'record'], ['metric-scope', 'metric-drilldown'], ['competing-requests', 'concurrent'],
    ['same-request', 'replay'], ['conflicting-request', 'conflicting-replay']]) {
    const entry = { id: 'expected', outcomes: [outcome] }
    for (const value of [undefined, check, ['text']]) {
      assert.throws(() => verifyOutcomeProof(entry, { outcomes: entry.outcomes, evidence: { [outcome]: value } }), /Use the/)
    }
    verifyOutcomeProof(entry, { outcomes: entry.outcomes, evidence: { [outcome]: [check] } })
  }
})

test('full inventory reads actual catalogs and reports and rejects unavailable enabled sources', async t => {
  let reportStatus = 200
  const menu = [{ code: 'orders', route: '/orders' }]
  t.mock.method(globalThis, 'fetch', async url => {
    if (url.endsWith('/token')) return Response.json({ access_token: 'private-token' })
    if (url.endsWith('/documents/metadata')) return Response.json([{ documentType: 'example.order' }])
    if (url.endsWith('/catalogs/metadata')) return Response.json([{ catalogType: 'example.customer' }])
    if (url.endsWith('/report-definitions')) return reportStatus === 200
      ? Response.json([{ reportCode: 'example.balance' }]) : new Response('', { status: reportStatus })
    if (url.endsWith('/roles')) return Response.json([])
    if (url.endsWith('/main-menu')) return Response.json({ groups: [{ items: menu }] })
    return Response.json({ isAuthenticated: true, isActive: true, isBootstrapAdmin: true })
  })
  const environment = { issuer: 'http://localhost/realm', apiOrigin: 'http://localhost:3000', password: 'private-password' }
  const inventory = await readAcceptanceInventory(environment, 2)
  assert.deepEqual(inventory.catalogs, [{ catalogType: 'example.customer' }])
  assert.deepEqual(inventory.reports, [{ reportCode: 'example.balance' }])
  assert.deepEqual(inventory.menu, [{ code: 'orders', route: '/orders' }])
  assert.doesNotMatch(JSON.stringify(inventory), /private-/)
  reportStatus = 404
  assert.deepEqual((await readAcceptanceInventory(environment, 2)).reports, [])
  menu.push({ code: 'balance', route: '/reports/example.balance' })
  await assert.rejects(readAcceptanceInventory(environment, 2), /Cannot read acceptance inventory/)
  reportStatus = 403
  await assert.rejects(readAcceptanceInventory(environment, 2), /Cannot read acceptance inventory/)
})

test('a saved Flow edge and an editable source still require proof of server membership', () => {
  const { plan, inventory } = fixture()
  inventory.documents[0].form = { sections: [{ rows: [{ fields: [{ key: 'parent_id', mirroredRelationship: {} }] }] }] }
  const target = plan.surfaces[0]
  const add = (scenario, outcomes) => {
    const entry = { ...plan.cases[0], id: scenario, title: scenario, target, scenario, outcomes }
    plan.cases.push(entry)
    plan.requirements[0].cases.push(entry.id)
    plan.requirements[0].scenarios.push(scenario)
  }
  add('document-flow', [...scenarioOutcomes['document-flow'], 'reference:parent_id'])
  plan.workflows.flows.push('business-1')
  assert.equal(plan.documents[0].sourceDriven, false)
  assert.throws(() => reconcileAcceptance(plan, inventory), /source-membership.*parent_id/)
  plan.businessChecks.push({ kind: 'source-membership', target, key: 'parent_id', test: 'Example.Tests.Source' })
  plan.cases.find(entry => entry.scenario === 'draft-edit').outcomes.push('commands:example.order/[]')
  plan.cases.find(entry => entry.scenario === 'lifecycle').outcomes.push('registers:example.order/[[],[]]')
  assert.throws(() => reconcileAcceptance(plan, inventory), /Missing form-labels/)
  add('form-labels', ['named-fields'])
  reconcileAcceptance(plan, inventory)
  plan.documents[0].withdrawalDependencies = ['capacity']
  assert.throws(() => reconcileAcceptance(plan, inventory), /withdrawal-cycle.*capacity/)
  plan.businessChecks.push({ kind: 'withdrawal-cycle', target, key: 'capacity', test: 'Example.Tests.Capacity' })
  reconcileAcceptance(plan, inventory)
  const entry = plan.cases.at(-1)
  assert.throws(() => verifyOutcomeProof(entry, { outcomes: entry.outcomes, evidence: { 'named-fields': ['text'] } }), /accessible-fields/)
  const observed = marks => ({ outcomes: entry.outcomes, evidence: { 'named-fields': marks } })
  assert.throws(() => verifyOutcomeProof(entry, observed(['accessible-fields'])), /accessible-fields:/)
  verifyOutcomeProof(entry, observed(['accessible-fields:' + entry.target]))
  const journal = { ...entry, target: 'document:general_journal_entry' }
  assert.throws(() => verifyOutcomeProof(journal, observed(['accessible-fields:' + entry.target])), /accessible-fields:document:general_journal_entry/)
  const form = 'accessible-fields:' + journal.target
  const lines = form + '/Lines'
  const workflow = form + '/Workflow'
  assert.throws(() => verifyOutcomeProof(journal, observed([form])), /Lines/)
  assert.throws(() => verifyOutcomeProof(journal, observed([form, workflow])), /Lines/)
  assert.throws(() => verifyOutcomeProof(journal, observed([form, lines])), /Workflow/)
  verifyOutcomeProof(journal, observed([form, workflow, lines]))
})

test('metadata can omit part columns and navigation when checking server dependencies', () => {
  const { plan, inventory } = fixture()
  assert.deepEqual(businessReferences({ parts: [{ partCode: 'items' }] }), [])
  delete inventory.menu
  reconcileWorkflows(plan, inventory)
})

test('business totals, custom commands and register fields require evidence from the matching document', () => {
  const { plan, inventory } = fixture()
  const doc = plan.documents[0]
  const target = plan.surfaces[0]
  const draft = plan.cases.find(entry => entry.scenario === 'draft-edit')
  const posted = plan.cases.find(entry => entry.scenario === 'lifecycle')
  const metadata = inventory.documents[0]
  metadata.form = { sections: [{ rows: [{ fields: [{ key: 'cost', dataType: 'Decimal' }] }] }] }
  const labels = { ...draft, id: 'labels', title: 'labels', scenario: 'form-labels', outcomes: ['named-fields'] }
  plan.cases.push(labels)
  plan.requirements[0].cases.push(labels.id)
  plan.requirements[0].scenarios.push(labels.scenario)
  assert.throws(() => reconcileAcceptance(plan, inventory), /flowAmountField/)
  doc.flowAmountField = null
  assert.throws(() => reconcileAcceptance(plan, inventory), /flowAmountReason/)
  doc.flowAmountReason = 'The numeric field is a per-unit rate.'
  assert.throws(() => reconcileAcceptance(plan, inventory), /commands:/)
  delete doc.flowAmountReason
  doc.flowAmountField = 'cost'
  assert.throws(() => reconcileAcceptance(plan, inventory), /Incorrect AmountField/)
  metadata.presentation = { amountField: 'cost' }
  assert.throws(() => reconcileAcceptance(plan, inventory), /flow-amount:example.order/)
  posted.outcomes.push('flow-amount:example.order')
  doc.commands = ['approve']
  assert.throws(() => reconcileAcceptance(plan, inventory), /commands:/)
  draft.outcomes.push('commands:example.order/["approve"]')
  assert.throws(() => reconcileAcceptance(plan, inventory), /command:example.order\/approve/)
  draft.outcomes.push('command:example.order/approve')
  assert.throws(() => reconcileAcceptance(plan, inventory), /register inventory/)
  posted.outcomes.push('registers:example.order/[[],["terms"]]')
  assert.throws(() => reconcileAcceptance(plan, inventory), /Reconcile actual posting registers/)
  doc.referenceRegisters = ['terms']
  assert.throws(() => reconcileAcceptance(plan, inventory), /register:example.order\/terms/)
  posted.outcomes.push('register:example.order/terms')
  doc.effectivePrerequisites = ['approval']
  assert.throws(() => reconcileAcceptance(plan, inventory), /effective-prerequisite/)
  plan.businessChecks.push({ kind: 'effective-prerequisite', target, key: 'approval', test: 'Example.Tests.Approval' })
  doc.uniqueHistoryKeys = ['rate']
  assert.throws(() => reconcileAcceptance(plan, inventory), /history-conflict/)
  plan.businessChecks.push({ kind: 'history-conflict', target, key: 'rate', test: 'Example.Tests.Rate' })
  reconcileAcceptance(plan, inventory)
  for (const name of [...posted.outcomes, ...draft.outcomes].filter(item => item.includes(':'))) {
    assert.throws(() => verifyOutcomeProof({ id: 'foreign', outcomes: [name] },
      { outcomes: [name], evidence: { [name]: ['text'] } }), /verify its expected business value/)
  }
})

test('retrying a calculated draft requires a save and readback for that document', () => {
  const { plan, inventory } = fixture()
  const document = plan.documents[0]
  document.priced = true
  document.asynchronous = true
  const target = 'document:' + document.type
  const proof = 'persisted-retry:' + document.type
  for (const scenario of ['pricing', 'async-race']) {
    const entry = { ...plan.cases[0], id: scenario, title: scenario, target, scenario, outcomes: [...scenarioOutcomes[scenario]] }
    plan.cases.push(entry)
    plan.requirements[0].cases.push(entry.id)
    plan.requirements[0].scenarios.push(scenario)
  }
  assert.throws(() => reconcileAcceptance(plan, inventory), /persisted-retry/)
  document.synchronousPriceReason = ' '
  assert.throws(() => reconcileAcceptance(plan, inventory), /independent asynchronous dependency/)
  document.synchronousPriceReason = 'The price is entered locally. Only the item description loads asynchronously.'
  reconcileAcceptance(plan, inventory)
  delete document.synchronousPriceReason
  plan.cases.at(-1).outcomes.push(proof)
  assert.ok(reconcileAcceptance(plan, inventory).includes(target + '/' + proof))
  const entry = { id: 'quote', outcomes: [proof] }
  assert.throws(() => verifyOutcomeProof(entry, { outcomes: [proof], evidence: { [proof]: ['current-retry'] } }), /persisted-retry/)
  assert.throws(() => verifyOutcomeProof(entry, { outcomes: [proof], evidence: { [proof]: [proof] } }), /failed calculation/)
  verifyOutcomeProof(entry, { outcomes: [proof], evidence: { [proof]: ['current-retry', proof] } })
})
