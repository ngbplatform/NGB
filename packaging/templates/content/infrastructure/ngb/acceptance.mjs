import assert from 'node:assert/strict'
import { digest, objectKeys, portablePath } from './kit-contracts.mjs'
import { browserTestCounts } from './evidence.mjs'

const scenarios = ['access', 'draft-edit', 'lifecycle', 'validation', 'lines',
  'source-change-clear', 'lookup-eligibility', 'pricing', 'async-race']

function unique(values, description) {
  assert.ok(Array.isArray(values) && values.length <= 10000, `Invalid ${description}.`)
  assert.ok(values.every(value => typeof value === 'string' && value.trim() === value && value.length > 0),
    `Invalid ${description} identity.`)
  assert.equal(new Set(values).size, values.length, `Duplicate ${description}.`)
  return [...values].sort()
}

export function acceptancePlan(value) {
  objectKeys(value, ['schemaVersion', 'documents', 'roles', 'cases'])
  assert.equal(value.schemaVersion, 1, 'Unsupported acceptance plan schema.')
  assert.ok(Array.isArray(value.documents) && Array.isArray(value.roles) && Array.isArray(value.cases),
    'Declare acceptance documents, roles and cases.')
  unique(value.documents.map(item => item.type), 'document')
  unique(value.roles, 'role')
  unique(value.cases.map(item => item.id), 'case')
  unique(value.cases.map(item => JSON.stringify([item.file, item.title, item.project])), 'browser test binding')
  for (const document of value.documents) {
    objectKeys(document, ['type', 'sourceDriven', 'priced', 'asynchronous'])
    for (const key of ['sourceDriven', 'priced', 'asynchronous']) {
      assert.equal(typeof document[key], 'boolean', `Classify ${document.type}/${key}.`)
    }
  }
  for (const entry of value.cases) {
    objectKeys(entry, ['id', 'document', 'role', 'scenario', 'file', 'title', 'project'])
    assert.ok(value.documents.some(document => document.type === entry.document), `Unknown document: ${entry.document}`)
    assert.ok(value.roles.includes(entry.role), `Unknown role: ${entry.role}`)
    assert.ok(scenarios.includes(entry.scenario), `Unknown scenario: ${entry.scenario}`)
    portablePath(entry.file)
    assert.ok(typeof entry.title === 'string' && entry.title.trim().length > 0, 'Missing test title.')
    assert.equal(typeof entry.project, 'string', 'Missing browser project.')
  }
  return value
}

export function reconcileAcceptance(plan, inventory) {
  acceptancePlan(plan)
  assert.deepEqual(unique(plan.documents.map(item => item.type), 'document'),
    unique(inventory.documents.map(item => item.documentType), 'registered document'),
    'Acceptance plan must include every runtime document, without stale entries.')
  assert.deepEqual(unique(plan.roles, 'role'), unique(inventory.roles, 'runtime role'),
    'Acceptance plan must include every active runtime role, including ngb-admin.')
  const required = []
  for (const metadata of inventory.documents) {
    const document = plan.documents.find(item => item.type === metadata.documentType)
    const kinds = ['draft-edit', 'validation']
    if (metadata.capabilities?.canPost !== false) kinds.push('lifecycle')
    if (metadata.parts?.length) kinds.push('lines')
    if (document.sourceDriven) kinds.push('source-change-clear', 'lookup-eligibility')
    if (document.priced) kinds.push('pricing')
    if (document.asynchronous) kinds.push('async-race')
    for (const kind of kinds) {
      assert.ok(plan.cases.some(entry => entry.document === document.type && entry.scenario === kind),
        `Missing acceptance scenario: ${document.type}/${kind}`)
      required.push(`${document.type}/${kind}`)
    }
    for (const role of plan.roles) {
      assert.ok(plan.cases.some(entry => entry.document === document.type && entry.role === role && entry.scenario === 'access'),
        `Missing role acceptance: ${document.type}/${role}`)
      required.push(`${document.type}/${role}/access`)
    }
  }
  return required
}

async function inventoryToken(environment) {
  // Credentials are confined to this disposable realm and are never persisted in evidence.
  const tokenResponse = await fetch(`${environment.issuer}/protocol/openid-connect/token`, {
    method: 'POST', redirect: 'error', signal: AbortSignal.timeout(15000),
    body: new URLSearchParams({ grant_type: 'password', client_id: 'ngb-load',
      username: 'administrator', password: environment.password }),
  })
  assert.equal(tokenResponse.status, 200, 'Cannot authenticate acceptance inventory administrator.')
  const token = (await tokenResponse.json()).access_token
  assert.ok(typeof token === 'string' && token.length > 0, 'Missing inventory access token.')
  return token
}

export async function readAcceptanceInventory(environment) {
  const token = await inventoryToken(environment)
  const read = async path => {
    const response = await fetch(`${environment.apiOrigin}${path}`, {
      headers: { Authorization: `Bearer ${token}` }, redirect: 'error', signal: AbortSignal.timeout(15000),
    })
    assert.equal(response.status, 200, `Cannot read acceptance inventory: ${path}`)
    return response.json()
  }
  const [documents, roles, access, menu] = await Promise.all([
    read('/api/documents/metadata'), read('/api/security/roles'), read('/api/security/me/access'), read('/api/main-menu'),
  ])
  assert.ok(access.isAuthenticated && access.isActive && access.isBootstrapAdmin,
    'Inventory must be collected by the active trusted administrator, without permission filtering.')
  assert.ok(Array.isArray(documents) && Array.isArray(roles) && Array.isArray(menu.groups), 'Invalid runtime acceptance inventory.')
  // AddNgbRuntime always registers the SDK journal definition, even in the
  // security-only starter. Its specialized UI belongs to the Accounting module.
  // Application documents are never filtered by menu visibility.
  const accounting = menu.groups.flatMap(group => group.items ?? []).some(item =>
    item.code === 'general_journal_entry' || item.code?.startsWith('accounting.')
    || item.route?.startsWith('/accounting/'))
  const platformOnly = accounting ? [] : documents.filter(item => item.documentType === 'general_journal_entry')
  const activeRoles = roles.filter(role => role.isActive)
  return { documents: documents.filter(item => !platformOnly.includes(item)),
    platformOnly: platformOnly.map(item => ({ documentType: item.documentType,
      reason: 'SDK journal definition is registered, but the application has not composed Accounting navigation.' })),
    roles: unique(['ngb-admin', ...activeRoles.map(role => role.code)], 'runtime role'),
    roleRecords: activeRoles.map(role => ({ code: role.code, roleId: role.roleId, name: role.name })) }
}

export async function prepareAcceptanceActors(environment, inventory) {
  const token = await inventoryToken(environment)
  const actors = { 'ngb-admin': 'administrator' }
  for (const role of inventory.roleRecords) {
    const email = `acceptance-${digest(role.code).slice(0, 16)}@example.test`
    const response = await fetch(`${environment.apiOrigin}/api/security/users`, {
      method: 'POST', redirect: 'error', signal: AbortSignal.timeout(15000),
      headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, firstName: 'Acceptance', lastName: role.name, displayName: `Acceptance ${role.name}`,
        enabled: true, temporaryPassword: environment.password, requirePasswordUpdate: false, roleIds: [role.roleId] }),
    })
    assert.equal(response.status, 200, `Cannot provision isolated acceptance actor: ${role.code}`)
    const user = await response.json()
    assert.ok(user.isActive && user.authSubject, `Inactive or unlinked acceptance actor: ${role.code}`)
    assert.deepEqual(user.roles.map(item => item.code), [role.code], 'Acceptance users must have exactly one application role.')
    actors[role.code] = email
  }
  return actors
}

export function browserAcceptance(plan, inventory, report, { runId, origin }) {
  const required = reconcileAcceptance(plan, inventory)
  browserTestCounts(report)
  const executed = new Map()
  const bindings = new Set()
  function visit(suites) {
    for (const suite of suites) {
      for (const spec of suite.specs) {
        for (const test of spec.tests) {
          const binding = JSON.stringify([spec.file, spec.title, test.projectName])
          assert.ok(!bindings.has(binding), 'Ambiguous browser test identity; use distinct titles within each file/project.')
          bindings.add(binding)
          for (const attachment of test.results[0].attachments ?? []) {
            if (attachment.name !== 'ngb-acceptance') continue
            assert.equal(attachment.contentType, 'application/json', 'Invalid acceptance attachment type.')
            assert.ok(typeof attachment.body === 'string' && attachment.body.length <= 65536,
              'Acceptance evidence must be a bounded inline attachment.')
            const proof = JSON.parse(Buffer.from(attachment.body, 'base64').toString('utf8'))
            const entry = plan.cases.find(item => item.id === proof.caseId)
            assert.ok(entry && !executed.has(entry.id), 'Unknown or duplicate acceptance evidence.')
            assert.equal(spec.file.replaceAll('\\', '/'), entry.file, 'Acceptance test file differs.')
            assert.equal(spec.title, entry.title, 'Acceptance test title differs.')
            assert.equal(test.projectName, entry.project, 'Acceptance browser project differs.')
            assert.equal(proof.schemaVersion, 1, 'Unsupported browser proof.')
            assert.equal(proof.runId, runId, 'Acceptance evidence belongs to another run.')
            assert.equal(proof.origin, origin, 'Acceptance must use the disposable application origin.')
            assert.equal(new URL(proof.url).origin, origin, 'Acceptance ended outside the application.')
            assert.equal(proof.role, entry.role, 'Acceptance actor has a different role.')
            assert.ok(typeof proof.subject === 'string' && proof.subject.length > 0, 'Missing authenticated actor.')
            assert.ok(Number.isSafeInteger(proof.interactions) && proof.interactions > 0,
              'Acceptance requires real browser input, not API-only setup.')
            assert.ok(Number.isSafeInteger(proof.assertions) && proof.assertions > 0,
              'Acceptance requires a visible UI assertion.')
            executed.set(entry.id, { caseId: entry.id, subject: proof.subject })
          }
        }
      }
      visit(suite.suites ?? [])
    }
  }
  visit(report.suites)
  assert.deepEqual([...executed.keys()].sort(), plan.cases.map(entry => entry.id).sort(),
    'Required browser scenarios were not executed with UI evidence.')
  return { required, cases: [...executed.values()] }
}
