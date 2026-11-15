import assert from 'node:assert/strict'
import test from 'node:test'
import { acceptancePlan, browserAcceptance, reconcileAcceptance, readAcceptanceInventory } from '../../packaging/templates/content/infrastructure/ngb/acceptance.mjs'

const context = { runId: 'current-run', origin: 'http://127.0.0.1:4321' }
function fixture() {
  const inventory = { documents: [{ documentType: 'example.order', parts: [{}] }], roles: ['ngb-admin', 'example.reader'] }
  const kinds = ['access', 'draft-edit', 'validation', 'lifecycle', 'lines', 'source-change-clear', 'lookup-eligibility', 'pricing', 'async-race']
  const cases = [...kinds.map(scenario => ({ id: scenario, role: 'ngb-admin', scenario })),
    { id: 'reader', role: 'example.reader', scenario: 'access' }].map(entry => ({
    ...entry, document: 'example.order', file: 'order.spec.ts', title: entry.id, project: 'chromium',
  }))
  const plan = { schemaVersion: 1, documents: [{ type: 'example.order', sourceDriven: true, priced: true, asynchronous: true }],
    roles: inventory.roles, cases }
  const specs = cases.map(entry => ({ ok: true, file: entry.file, title: entry.title, tests: [{
    projectName: entry.project, expectedStatus: 'passed', status: 'expected', results: [{ status: 'passed', attachments: [{
      name: 'ngb-acceptance', contentType: 'application/json', body: Buffer.from(JSON.stringify({
        schemaVersion: 1, ...context, caseId: entry.id, role: entry.role, subject: entry.role,
        url: `${context.origin}/documents/example.order/new`, interactions: 3, assertions: 2,
      })).toString('base64'),
    }] }],
  }] }))
  const report = { errors: [], stats: { expected: cases.length, unexpected: 0, flaky: 0, skipped: 0 }, suites: [{ specs }] }
  return { plan, inventory, report }
}

function verify({ plan, inventory, report }) {
  return browserAcceptance(plan, inventory, report, context)
}

test('acceptance binds every document, role, scenario and current-run UI result', () => {
  const value = fixture()
  assert.equal(verify(value).cases.length, 10)
  assert.equal(verify(value).required.length, 10)
  value.report.suites = [{ specs: [], suites: value.report.suites }]
  assert.equal(verify(value).cases.length, 10)
  assert.deepEqual(acceptancePlan(value.plan), value.plan)
})

test('runtime inventory and behavioral scenarios cannot be omitted from the matrix', () => {
  for (const change of [
    value => value.inventory.documents.push({ documentType: 'example.omitted' }),
    value => value.inventory.roles.push('example.omitted'),
    value => value.plan.roles.push('example.stale'),
    value => value.plan.documents.push({ type: 'example.stale', sourceDriven: false, priced: false, asynchronous: false }),
    ...fixture().plan.cases.map(entry => value => { value.plan.cases = value.plan.cases.filter(item => item.id !== entry.id) }),
  ]) {
    const value = fixture()
    change(value)
    assert.throws(() => reconcileAcceptance(value.plan, value.inventory))
  }
  const value = fixture()
  value.inventory.documents[0] = { documentType: 'example.order', capabilities: { canPost: false } }
  Object.assign(value.plan.documents[0], { sourceDriven: false, priced: false, asynchronous: false })
  value.plan.cases = value.plan.cases.filter(entry => ['access', 'draft-edit', 'validation'].includes(entry.scenario))
  assert.equal(reconcileAcceptance(value.plan, value.inventory).length, 4)
})

test('green test counts cannot substitute missing, stale, duplicate or API-only UI evidence', () => {
  for (const change of [
    spec => { spec.file = 'other.spec.ts' }, spec => { spec.title = 'other' },
    spec => { spec.tests[0].projectName = 'other' },
    spec => { spec.tests[0].results[0].attachments = [] },
    spec => { spec.tests[0].results[0].attachments[0].name = 'unrelated' },
    spec => { spec.tests[0].results[0].attachments[0].contentType = 'text/plain' },
    spec => { delete spec.tests[0].results[0].attachments[0].body },
    spec => { spec.tests[0].results[0].attachments.push(spec.tests[0].results[0].attachments[0]) },
  ]) {
    const value = fixture()
    change(value.report.suites[0].specs[0])
    assert.throws(() => verify(value))
  }
  for (const [key, replacement] of Object.entries({ schemaVersion: 2, caseId: 'unknown', runId: 'old-run',
    origin: 'http://127.0.0.1:9999', url: 'https://example.test/', role: 'other', subject: '', interactions: 0, assertions: 0 })) {
    const value = fixture()
    const attachment = value.report.suites[0].specs[0].tests[0].results[0].attachments[0]
    const proof = JSON.parse(Buffer.from(attachment.body, 'base64'))
    proof[key] = replacement
    attachment.body = Buffer.from(JSON.stringify(proof)).toString('base64')
    assert.throws(() => verify(value), key)
  }
})

test('malformed matrices are rejected before execution', () => {
  for (const change of [
    plan => { plan.schemaVersion = 2 }, plan => { plan.documents = null },
    plan => { plan.documents[0].sourceDriven = 'yes' }, plan => { plan.roles.push(plan.roles[0]) },
    plan => { plan.roles.push(' ') }, plan => { plan.cases.push(plan.cases[0]) },
    plan => { plan.cases.push({ ...plan.cases[0], id: 'same-test' }) },
    plan => { plan.cases[0].document = 'unknown' }, plan => { plan.cases[0].role = 'unknown' },
    plan => { plan.cases[0].scenario = 'unknown' }, plan => { plan.cases[0].file = '../outside.ts' },
    plan => { plan.cases[0].title = '' }, plan => { plan.cases[0].project = null },
  ]) {
    const { plan } = fixture()
    change(plan)
    assert.throws(() => acceptancePlan(plan))
  }
})

test('runtime inventory uses authenticated APIs and never records the test credential', async t => {
  const requests = []
  t.mock.method(globalThis, 'fetch', async (url, options) => {
    requests.push([url, options])
    if (url.endsWith('/token')) return Response.json({ access_token: 'private-token' })
    if (url.endsWith('/metadata')) return Response.json([{ documentType: 'example.order' }])
    if (url.endsWith('/roles')) return Response.json([{ code: 'reader', isActive: true }, { code: 'inactive', isActive: false }])
    if (url.endsWith('/main-menu')) return Response.json({ groups: [] })
    return Response.json({ isAuthenticated: true, isActive: true, isBootstrapAdmin: true })
  })
  const inventory = await readAcceptanceInventory({ issuer: 'http://localhost/realm', apiOrigin: context.origin, password: 'private-password' })
  assert.deepEqual(inventory.roles, ['ngb-admin', 'reader'])
  assert.equal(requests.length, 5)
  assert.equal(requests[1][1].headers.Authorization, 'Bearer private-token')
  assert.doesNotMatch(JSON.stringify(inventory), /private-/)
})

test('inventory rejects inaccessible endpoints, missing tokens and filtered administrator metadata', async t => {
  for (const failure of ['token', 'missing-token', 'metadata', 'roles', 'inactive', 'filtered']) {
    t.mock.method(globalThis, 'fetch', async url => {
      if (url.endsWith('/token')) return failure === 'token' ? new Response('', { status: 401 })
        : Response.json(failure === 'missing-token' ? {} : { access_token: 'token' })
      if (url.endsWith('/metadata')) return failure === 'metadata' ? new Response('', { status: 403 }) : Response.json([])
      if (url.endsWith('/roles')) return Response.json(failure === 'roles' ? {} : [])
      if (url.endsWith('/main-menu')) return Response.json({ groups: [] })
      return Response.json({ isAuthenticated: true, isActive: failure !== 'inactive', isBootstrapAdmin: failure !== 'filtered' })
    })
    await assert.rejects(readAcceptanceInventory({ issuer: 'http://localhost/realm', apiOrigin: context.origin, password: 'password' }))
    t.mock.restoreAll()
  }
})

test('disposable actors are provisioned through the authorized API with exactly one application role', async t => {
  const { prepareAcceptanceActors } = await import('../../packaging/templates/content/infrastructure/ngb/acceptance.mjs')
  const inventory = { roleRecords: [{ roleId: 'reader-id', code: 'reader', name: 'Reader' }] }
  let payload
  t.mock.method(globalThis, 'fetch', async (url, options) => {
    if (url.endsWith('/token')) return Response.json({ access_token: 'token' })
    payload = JSON.parse(options.body)
    return Response.json({ isActive: true, authSubject: 'subject', roles: [{ code: 'reader' }] })
  })
  const actors = await prepareAcceptanceActors({ issuer: 'http://localhost/realm', apiOrigin: context.origin, password: 'password' }, inventory)
  assert.equal(actors['ngb-admin'], 'administrator')
  assert.match(actors.reader, /^acceptance-[a-f0-9]{16}@example.test$/)
  assert.deepEqual(payload.roleIds, ['reader-id'])
  assert.equal(payload.requirePasswordUpdate, false)
  for (const body of [null, { isActive: false }, { isActive: true, authSubject: 'subject', roles: [{ code: 'administrator' }] }]) {
    t.mock.method(globalThis, 'fetch', async url => url.endsWith('/token') ? Response.json({ access_token: 'token' })
      : body === null ? new Response('', { status: 403 }) : Response.json(body))
    await assert.rejects(prepareAcceptanceActors({ issuer: 'http://localhost/realm', apiOrigin: context.origin, password: 'password' }, inventory))
  }
})

test('the always-registered SDK journal joins acceptance when Accounting is composed; hidden application documents remain mandatory', async t => {
  for (const menu of [[], [{ items: [{ code: 'general_journal_entry' }] }],
    [{ items: [{ code: 'accounting.trial_balance' }] }], [{ items: [{ route: '/accounting/general-journal-entries' }] }]]) {
    t.mock.method(globalThis, 'fetch', async url => {
      if (url.endsWith('/token')) return Response.json({ access_token: 'token' })
      if (url.endsWith('/metadata')) return Response.json([{ documentType: 'example.hidden' }, { documentType: 'general_journal_entry' }])
      if (url.endsWith('/roles')) return Response.json([])
      if (url.endsWith('/main-menu')) return Response.json({ groups: menu })
      return Response.json({ isAuthenticated: true, isActive: true, isBootstrapAdmin: true })
    })
    const inventory = await readAcceptanceInventory({ issuer: 'http://localhost/realm', apiOrigin: context.origin, password: 'password' })
    assert.deepEqual(inventory.documents.map(item => item.documentType), menu.length
      ? ['example.hidden', 'general_journal_entry'] : ['example.hidden'])
    assert.equal(inventory.platformOnly.length, menu.length ? 0 : 1)
    t.mock.restoreAll()
  }
})
