import assert from 'node:assert/strict'
import test from 'node:test'
import { acceptancePlan, browserAcceptance, readAcceptanceInventory, reconcileAcceptance } from '../../packaging/templates/content/infrastructure/ngb/acceptance.mjs'
import { reconcileSurfaces, scenarioOutcomes, validateOutcomes, verifyOutcomeProof } from '../../packaging/templates/content/infrastructure/ngb/acceptance-outcomes.mjs'

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
    ...['access', 'draft-edit', 'validation', 'lifecycle', 'search'].map(kind => [surfaces[0], kind]),
    [surfaces[1], 'search'], [surfaces[2], 'report'], [surfaces[3], 'navigation'],
  ]
  const cases = bindings.map(([target, scenario], index) => ({
    id: `case-${index}`, target, scenario, role: 'ngb-admin', file: 'business.spec.ts',
    title: `business outcome ${index}`, project: '',
    outcomes: scenario === 'search' ? ['opened-target', target === surfaces[0] ? 'number-or-code' : 'name']
      : [...scenarioOutcomes[scenario]],
  }))
  const plan = {
    schemaVersion: 2, documents: [{ type: 'example.order', sourceDriven: false, priced: false, asynchronous: false }],
    roles: inventory.roles, surfaces, cases,
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

test('a denial cannot stand in for allowed withdrawal and one cleared line cannot stand in for the whole dependency chain', () => {
  for (const scenario of ['lifecycle', 'source-change-clear', 'async-race', 'pricing', 'temporal-restoration',
    'prerequisite-withdrawal', 'dashboard-complete', 'historical-as-of', 'numbering-date']) {
    const entry = { id: scenario, outcomes: scenarioOutcomes[scenario] }
    verifyOutcomeProof(entry, { outcomes: entry.outcomes })
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
          outcomes: entry.outcomes })).toString('base64') }] }] }],
  })) }], stats: { expected: plan.cases.length, unexpected: 0, flaky: 0, skipped: 0 } }
  assert.equal(browserAcceptance(plan, inventory, report, context).cases.length, plan.cases.length)
  const attachment = report.suites[0].specs[3].tests[0].results[0].attachments[0]
  const proof = JSON.parse(Buffer.from(attachment.body, 'base64'))
  proof.outcomes = ['reason']
  attachment.body = Buffer.from(JSON.stringify(proof)).toString('base64')
  assert.throws(() => browserAcceptance(plan, inventory, report, context), /every expected outcome/)
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
