import assert from 'node:assert/strict'
import { digest, objectKeys, portablePath } from './kit-contracts.mjs'
import { reconcileHistoricalChecks, requireBusinessCheck, validateBusinessChecks } from './business-checks.mjs'
import { browserTestCounts } from './evidence.mjs'
import { businessReferences, reconcileSurfaces, reconcileWorkflows, scenarioOutcomes, validateOutcomes, verifyOutcomeProof } from './acceptance-outcomes.mjs'

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
  const current = value.schemaVersion === 2
  objectKeys(value, current ? ['schemaVersion', 'documents', 'roles', 'cases', 'surfaces', 'requirements', 'businessChecks']
    : ['schemaVersion', 'documents', 'roles', 'cases'], current ? ['workflows', 'nonBatchJobs'] : [])
  assert.ok(current || value.schemaVersion === 1, 'Unsupported acceptance plan schema.')
  if (current) unique(value.surfaces, 'surface')
  assert.ok(Array.isArray(value.documents) && Array.isArray(value.roles) && Array.isArray(value.cases),
    'Declare acceptance documents, roles and cases.')
  unique(value.documents.map(item => item.type), 'document')
  unique(value.roles, 'role')
  unique(value.cases.map(item => item.id), 'case')
  unique(value.cases.map(item => JSON.stringify([item.file, item.title, item.project])), 'browser test binding')
  for (const document of value.documents) {
    objectKeys(document, ['type', 'sourceDriven', 'priced', 'asynchronous'], current
      ? ['informationalReferences', 'withdrawalDependencies', 'flowAmountField', 'flowAmountReason', 'commands', 'referenceRegisters', 'operationalRegisters',
        'effectivePrerequisites', 'uniqueHistoryKeys', 'synchronousPriceReason'] : [])
    for (const key of ['sourceDriven', 'priced', 'asynchronous']) {
      assert.equal(typeof document[key], 'boolean', `Classify ${document.type}/${key}.`)
    }
  }
  for (const entry of value.cases) {
    objectKeys(entry, current ? ['id', 'target', 'role', 'scenario', 'file', 'title', 'project', 'outcomes']
      : ['id', 'document', 'role', 'scenario', 'file', 'title', 'project'])
    if (current) assert.ok(value.surfaces.includes(entry.target), `Unknown surface: ${entry.target}`)
    else assert.ok(value.documents.some(document => document.type === entry.document), `Unknown document: ${entry.document}`)
    assert.ok(value.roles.includes(entry.role), `Unknown role: ${entry.role}`)
    assert.ok((current ? Object.keys(scenarioOutcomes) : scenarios).includes(entry.scenario), `Unknown scenario: ${entry.scenario}`)
    portablePath(entry.file)
    assert.ok(typeof entry.title === 'string' && entry.title.trim().length > 0, 'Missing test title.')
    assert.equal(typeof entry.project, 'string', 'Missing browser project.')
  }
  if (current) {
    validateOutcomes(value)
    validateBusinessChecks(value)
  }
  return value
}

function documentOutcomes(plan, document, metadata) {
  const target = 'document:' + document.type
  const required = []
  const needsOutcome = (name, scenario, role) => {
    assert.ok(plan.cases.some(entry => entry.target === target && entry.outcomes.includes(name)
      && (!scenario || entry.scenario === scenario) && (!role || entry.role === role)),
    `Missing browser outcome: ${target}/${name}`)
    required.push(target + '/' + name)
  }
  if (document.synchronousPriceReason !== undefined) {
    assert.ok(document.priced && document.asynchronous && typeof document.synchronousPriceReason === 'string'
      && document.synchronousPriceReason.trim(), 'Explain the local price calculation and its independent asynchronous dependency.')
  } else if (document.priced && document.asynchronous) {
    needsOutcome('persisted-retry:' + document.type, 'async-race')
  }
  const fields = [
    ...(metadata.form?.sections?.flatMap(section => section.rows.flatMap(row => row.fields)) ?? []),
    ...(metadata.list?.columns ?? []),
    ...(metadata.parts?.flatMap(part => part.list?.columns ?? []) ?? []),
  ]
  const numericFields = fields.some(field => ['Money', 'Decimal'].includes(field.dataType))
  if (metadata.presentation?.amountField || numericFields
    || Object.hasOwn(document, 'flowAmountField')) {
    assert.ok(document.flowAmountField === null || (typeof document.flowAmountField === 'string' && document.flowAmountField.trim()),
      `Classify the business total for ${target} with flowAmountField (null for no document total).`)
    assert.equal(metadata.presentation?.amountField ?? null, document.flowAmountField, `Incorrect AmountField for ${target}.`)
    if (numericFields && document.flowAmountField === null) {
      assert.ok(typeof document.flowAmountReason === 'string' && document.flowAmountReason.trim(),
        `Explain which numeric values are present and why ${target} has no business total (flowAmountReason).`)
    }
    if (document.flowAmountField) needsOutcome('flow-amount:' + document.type)
  }
  if (document.type === 'general_journal_entry') needsOutcome('journal-draft', 'draft-edit')
  else if (metadata.form || document.commands) {
    const codes = unique(document.commands ?? [], 'command')
    needsOutcome('commands:' + document.type + '/' + JSON.stringify(codes), 'draft-edit', 'ngb-admin')
    for (const code of codes) needsOutcome('command:' + document.type + '/' + code)
  }
  const references = unique(document.referenceRegisters ?? [], 'reference register')
  const operational = unique(document.operationalRegisters ?? [], 'operational register')
  if (metadata.form && document.type !== 'general_journal_entry' && metadata.capabilities?.canPost !== false) {
    const prefix = 'registers:' + document.type + '/'
    const observed = plan.cases.filter(entry => entry.target === target)
      .flatMap(entry => entry.outcomes.filter(name => name.startsWith(prefix)).map(name => JSON.parse(name.slice(prefix.length))))
    assert.ok(observed.length, `Missing posted register inventory for ${target}.`)
    for (const [index, codes] of [operational, references].entries()) {
      assert.ok(observed.every(pair => Array.isArray(pair) && pair.length === 2 && Array.isArray(pair[index])),
        'Declare operational and reference register codes for each posting fixture.')
      assert.deepEqual([...new Set(observed.flatMap(pair => pair[index]))].sort(), codes,
        `Reconcile actual posting registers with the business requirements for ${target}.`)
    }
  }
  for (const code of references) {
    needsOutcome('register:' + document.type + '/' + code)
  }
  for (const key of unique(document.effectivePrerequisites ?? [], 'effective prerequisite')) {
    requireBusinessCheck(plan, 'effective-prerequisite', target, key)
  }
  for (const key of unique(document.uniqueHistoryKeys ?? [], 'historical uniqueness key')) {
    requireBusinessCheck(plan, 'history-conflict', target, key)
  }
  return required
}

export function reconcileAcceptance(plan, inventory) {
  acceptancePlan(plan)
  assert.deepEqual(unique(plan.documents.map(item => item.type), 'document'),
    unique(inventory.documents.map(item => item.documentType), 'registered document'),
    'Acceptance plan must include every runtime document, without stale entries.')
  assert.deepEqual(unique(plan.roles, 'role'), unique(inventory.roles, 'runtime role'),
    'Acceptance plan must include every active runtime role, including ngb-admin.')
  if (plan.schemaVersion === 2) {
    reconcileSurfaces(plan, inventory)
    reconcileWorkflows(plan, inventory)
    reconcileHistoricalChecks(plan, inventory.reports ?? [])
  }
  const matches = (entry, type) => plan.schemaVersion === 2 ? entry.target === `document:${type}` : entry.document === type
  const required = []
  for (const metadata of inventory.documents) {
    const document = plan.documents.find(item => item.type === metadata.documentType)
    const kinds = ['draft-edit', 'validation']
    if (metadata.capabilities?.canPost !== false) {
      const withdrawalDisabled = plan.schemaVersion === 2 && metadata.capabilities?.canUnpost === false
      // Older SDK metadata also advertised Unpost for the typed journal. Its correction is a new journal.
      if (plan.schemaVersion === 2 && document.type === 'general_journal_entry') kinds.push('reversal')
      else kinds.push(withdrawalDisabled ? 'withdrawal-blocked' : 'lifecycle')
    }
    if (metadata.parts?.length) kinds.push('lines')
    if (plan.schemaVersion === 2) {
      const references = businessReferences(metadata, document.informationalReferences)
      for (const field of references) {
        const covered = plan.cases.some(entry => matches(entry, document.type)
          && ['document-flow', 'document-chain'].includes(entry.scenario) && entry.outcomes.includes(`reference:${field}`))
        assert.ok(covered, `Missing document-flow proof for ${document.type}/${field}.`)
        required.push(`${document.type}/reference:${field}`)
      }
      if (metadata.capabilities?.canPost !== false && metadata.capabilities?.canUnpost !== false && document.type !== 'general_journal_entry') {
        for (const key of unique(document.withdrawalDependencies, `withdrawalDependencies for ${document.type}`)) {
          requireBusinessCheck(plan, 'withdrawal-cycle', `document:${document.type}`, key)
        }
      }
      for (const field of references) requireBusinessCheck(plan, 'source-membership', `document:${document.type}`, field)
    }
    if (document.sourceDriven) kinds.push('source-change-clear', 'lookup-eligibility')
    if (document.priced) kinds.push('pricing')
    if (document.asynchronous) kinds.push('async-race')
    for (const kind of kinds) {
      assert.ok(plan.cases.some(entry => matches(entry, document.type) && entry.scenario === kind),
        `Missing acceptance scenario: ${document.type}/${kind}`)
      required.push(`${document.type}/${kind}`)
    }
    if (plan.schemaVersion === 2) required.push(...documentOutcomes(plan, document, metadata))
    for (const role of plan.roles) {
      assert.ok(plan.cases.some(entry => matches(entry, document.type) && entry.role === role && entry.scenario === 'access'),
        `Missing role acceptance: ${document.type}/${role}`)
      if (plan.schemaVersion === 2 && document.type === 'general_journal_entry') {
        assert.ok(plan.cases.some(entry => matches(entry, document.type) && entry.role === role
          && entry.scenario === 'access' && entry.outcomes.includes('journal-create-access')),
        `Verify journal Create access for ${role}.`)
      }
      required.push(`${document.type}/${role}/access`)
    }
  }
  if (plan.schemaVersion === 2) {
    const forms = [...inventory.documents.map(item => [`document:${item.documentType}`, item.form]),
      ...(inventory.catalogs ?? []).map(item => [`catalog:${item.catalogType}`, item.form])]
    for (const [target, form] of forms) {
      if (!form?.sections?.some(section => section.rows.some(row => row.fields?.length))) continue
      assert.ok(plan.cases.some(entry => entry.target === target && entry.scenario === 'form-labels'),
        `Missing form-labels browser test for ${target}.`)
      required.push(`${target}/form-labels`)
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

export async function readAcceptanceInventory(environment, schemaVersion = 1) {
  const token = await inventoryToken(environment)
  const read = async (path, optional = false) => {
    const response = await fetch(`${environment.apiOrigin}${path}`, {
      headers: { Authorization: `Bearer ${token}` }, redirect: 'error', signal: AbortSignal.timeout(15000),
    })
    if (optional && response.status === 404) return []
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
  const menuItems = menu.groups.flatMap(group => group.items ?? [])
  const accounting = menuItems.some(item =>
    item.code === 'general_journal_entry' || item.code?.startsWith('accounting.')
    || item.route?.startsWith('/accounting/'))
  const platformOnly = accounting ? [] : documents.filter(item => item.documentType === 'general_journal_entry')
  const activeRoles = roles.filter(role => role.isActive)
  const surfaces = schemaVersion === 2 ? {
    catalogs: await read('/api/catalogs/metadata'),
    reports: await read('/api/report-definitions', !menuItems.some(item =>
      item.kind === 'report' || item.route?.startsWith('/reports/'))),
    menuGroups: menu.groups,
    menu: menuItems.map(item => ({ code: item.code, route: item.route })),
  } : {}
  return { ...surfaces, documents: documents.filter(item => !platformOnly.includes(item)),
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
          const attachments = test.results[0].attachments ?? []
          if (plan.schemaVersion === 2) {
            const diagnostics = attachments.filter(item => item.name === 'ngb-page-errors')
            assert.equal(diagnostics.length, 1, 'Use the shared browser fixture to capture unhandled page errors.')
            const diagnostic = diagnostics[0]
            assert.ok(diagnostic.contentType === 'application/json' && typeof diagnostic.body === 'string'
              && diagnostic.body.length <= 65536, 'Invalid browser error evidence.')
            assert.deepEqual(JSON.parse(Buffer.from(diagnostic.body, 'base64').toString()), [], 'Unhandled browser page error.')
          }
          for (const attachment of attachments) {
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
            assert.equal(proof.schemaVersion, plan.schemaVersion, 'Unsupported browser proof.')
            if (plan.schemaVersion === 2) {
              verifyOutcomeProof(entry, proof)
              for (const size of ['desktop', 'mobile']) {
                if (!entry.outcomes.includes('dashboard-layout:' + size)) continue
                const images = attachments.filter(item => item.name === 'ngb-dashboard-' + size)
                assert.equal(images.length, 1, 'Attach the dashboard screenshot from this test.')
                assert.equal(images[0].contentType, 'image/png')
                const png = Buffer.from(images[0].body ?? '', 'base64')
                assert.ok(png.length > 32 && png.subarray(0, 8).toString('hex') === '89504e470d0a1a0a'
                  && png.toString('ascii', 12, 16) === 'IHDR', 'Invalid dashboard screenshot.')
                const width = png.readUInt32BE(16)
                assert.ok(width > 0 && (size === 'desktop' ? width >= 1024 : width <= 480), 'Incorrect screenshot viewport.')
              }
            }
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
