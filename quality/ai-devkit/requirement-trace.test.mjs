import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { mkdir, mkdtemp, readFile, realpath, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import { fileURLToPath } from 'node:url'
import { captureObligations, verifyApplicationUpgrade, verifyPresentationInventory, verifyRequirementTrace } from '../../packaging/templates/content/infrastructure/ngb/requirement-trace.mjs'

const rule = { kind: 'interval-overlap', target: 'document:example.booking', key: 'resource' }
const capacity = { kind: 'capacity', target: 'report:example.activity', key: 'monthly' }

test('only an empty starter can use the legacy plan, even before obligations are captured', async t => {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'ngb-trace-')))
  t.after(() => rm(root, { recursive: true, force: true }))
  await mkdir(join(root, 'ai'))
  const plan = { schemaVersion: 1, documents: [] }
  await verifyRequirementTrace(root, plan)
  await assert.rejects(verifyRequirementTrace(root, { ...plan, documents: [{ type: 'example.booking' }] }), /Use schema 2/)
  for (const file of ['business-obligations.json', 'business-obligations.lock.json', 'brief-sources.json', 'brief-context.json', 'requirement-trace.json', 'operation-review.json']) {
    const path = join(root, 'ai', file)
    await writeFile(path, '{}')
    await assert.rejects(verifyRequirementTrace(root, plan), /Use schema 2/)
    await rm(path)
  }
})

test('source bindings survive deletion of a declared check and preserve dated and memory requirements', async t => {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'ngb-trace-')))
  t.after(() => rm(root, { recursive: true, force: true }))
  await mkdir(join(root, 'ai'))
  await verifyRequirementTrace(root, { schemaVersion: 1 })
  await writeFile(join(root, 'ai/operation-review.json'), JSON.stringify([{ target: rule.target,
    operation: 'post', requirement: 'exclusive', facts: [{ kind: 'exclusive', key: rule.key, correctable: false }] }]))
  await writeFile(join(root, 'ai/brief-sources.json'), JSON.stringify(['brief.md']))
  const quote = 'A resource can only be booked once in a given interval.'
  await writeFile(join(root, 'brief.md'), quote + '\nLimit the monthly report working set.')
  const plan = { schemaVersion: 2, documents: [{ type: 'example.booking' }],
    requirements: [{ id: 'exclusive', cases: [] }, { id: 'memory', cases: [] }],
    businessChecks: [rule, { ...capacity, budget: { maxWorkingSetBytes: 250000000 } }] }
  const trace = {
    requirements: [
      { id: 'exclusive', source: 'brief.md', quote, checks: [rule] },
      { id: 'memory', source: 'brief.md', quote: 'Limit the monthly report working set.',
        checks: [{ ...capacity, workingSet: true }] },
    ],
    documentRules: [{ type: 'example.booking', 'interval-overlap': ['resource'],
      'effective-prerequisite': { reason: 'Booking establishes the first dated fact.' },
      'snapshot-stability': { reason: 'No mutable facts are copied at booking.' } }],
  }
  const save = source => writeFile(join(root, 'ai/requirement-trace.json'), JSON.stringify(source))
  await assert.rejects(verifyRequirementTrace(root, plan))
  await save(trace)
  const obligations = trace.requirements.map(({ id, source, quote, checks }) => ({ id, source, quote,
    effect: id === 'exclusive' ? 'write' : 'operations',
    proofs: checks.map(({ kind, target, key }) => ({ kind, target, key })) }))
  const obligationsPath = join(root, 'ai/business-obligations.json')
  await writeFile(obligationsPath, JSON.stringify(obligations))
  await captureObligations(root, false)
  await assert.rejects(verifyRequirementTrace(root, plan), /capture --apply/)
  await captureObligations(root, true)
  await verifyRequirementTrace(root, plan)
  const weakened = structuredClone(trace)
  weakened.requirements[0].quote = 'Limit the monthly report working set.'
  weakened.requirements[0].checks = []
  weakened.requirements[0].browserOnlyReason = 'Covered by search.'
  weakened.documentRules[0]['interval-overlap'] = { reason: 'Current state is sufficient.' }
  const searchOnly = { ...plan, businessChecks: plan.businessChecks.slice(1) }
  await save(weakened)
  await assert.rejects(verifyRequirementTrace(root, searchOnly), /original obligation/)
  weakened.requirements[0].quote = quote
  await save(weakened)
  await assert.rejects(verifyRequirementTrace(root, searchOnly), /Missing required server proof/)
  await writeFile(obligationsPath, JSON.stringify(obligations.slice(1)))
  await assert.rejects(verifyRequirementTrace(root, searchOnly), /Unreviewed brief text/)
  await writeFile(obligationsPath, JSON.stringify(obligations))
  await save(trace)
  await assert.rejects(verifyRequirementTrace(root, { ...plan, businessChecks: plan.businessChecks.slice(1) }), /Missing required/)
  await assert.rejects(verifyRequirementTrace(root, { ...plan, businessChecks: [rule, { ...capacity, budget: {} }] }), /working-set budget/)
  const withNamedTest = structuredClone(trace)
  withNamedTest.requirements[0].integrationTests = ['Example.BookingTests.Retry']
  await save(withNamedTest)
  await assert.rejects(verifyRequirementTrace(root, plan, new Set()), /Missing passed integration test/)
  await verifyRequirementTrace(root, plan, new Set(['Example.BookingTests.Retry']))
  await save({ ...trace, documentRules: [] })
  await assert.rejects(verifyRequirementTrace(root, plan), /every application document/)
  const classification = trace.documentRules[0]
  await save({ ...trace, documentRules: [{ ...classification, 'interval-overlap': { reason: 'Not needed.' } }] })
  await assert.rejects(verifyRequirementTrace(root, plan), /interval-overlap/)
  await save(trace)
  await writeFile(join(root, 'brief.md'), 'The original text changed.')
  await assert.rejects(verifyRequirementTrace(root, plan), /cited requirement/)
  await assert.rejects(verifyRequirementTrace(root, { schemaVersion: 1 }), /Use schema 2/)
})

test('runtime menu icons, dashboard proofs and selectable record identifiers are checked independently', () => {
  const menu = { label: 'Dashboard', icon: 'home',
    items: [{ code: 'example.home', route: '/home', label: 'Dashboard', icon: 'home' }] }
  const id = { code: 'source_id', dataType: 'uuid', lookup: { kind: 'document' } }
  const display = { code: 'source_display', isSelectable: true }
  const report = { reportCode: 'example.activity', dataset: { fields: [id, display] } }
  const inventory = { menuGroups: [menu], reports: [report] }
  const icons = ['home', 'file-text']
  const plan = { cases: [
    { target: 'navigation:example.home', outcomes: ['dashboard-home', 'dashboard-layout:desktop', 'dashboard-layout:mobile', 'business-calendar', 'dashboard-context'] },
    { target: 'report:example.activity', outcomes: ['report-reference:source_display'] },
  ] }
  verifyPresentationInventory(plan, inventory, icons)
  const localized = structuredClone(inventory)
  const localizedGroup = localized.menuGroups[0]
  localizedGroup.label = 'Übersicht'
  localizedGroup.items[0].label = localizedGroup.label
  localizedGroup.items[0].route = '/uebersicht'
  const dashboardPlan = { ...plan, workflows: { dashboards: ['daily-review'] } }
  verifyPresentationInventory(dashboardPlan, localized, icons)
  delete localizedGroup.items[0].icon
  assert.throws(() => verifyPresentationInventory(dashboardPlan, localized, icons), /Dashboard item icon/)
  for (const name of plan.cases[0].outcomes) {
    const changed = structuredClone(plan)
    changed.cases[0].outcomes = changed.cases[0].outcomes.filter(item => item !== name)
    assert.throws(() => verifyPresentationInventory(changed, inventory, icons))
  }
  for (const level of ['group', 'item']) {
    for (const icon of ['file-text', 'unsupported', null, undefined]) {
      const changed = structuredClone(inventory)
      const group = changed.menuGroups[0]
      const registration = level === 'group' ? group : group.items[0]
      if (icon === undefined) delete registration.icon
      else registration.icon = icon
      const message = icon === 'unsupported' ? `Unknown menu ${level} icon` : `Dashboard ${level} icon`
      assert.throws(() => verifyPresentationInventory(plan, changed, icons), new RegExp(message))
    }
  }
  for (const fields of [[{ ...id, isSelectable: true }], [{ ...id, isGroupable: true }, display]]) {
    assert.throws(() => verifyPresentationInventory(plan, { ...inventory,
      reports: [{ ...report, dataset: { fields } }] }, icons), /supporting ID/)
  }
  assert.throws(() => verifyPresentationInventory({ cases: plan.cases.slice(0, 1) }, inventory, icons), /visible reference/)
})

test('a reviewed baseline binds actual values, named tests, visual evidence and the complete recovery scope', async t => {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'ngb-reviewed-')))
  t.after(() => rm(root, { recursive: true, force: true }))
  await mkdir(join(root, 'ai'))
  await writeFile(join(root, 'ai/operation-review.json'), '[]')
  const source = 'brief.md'
  const quote = 'Recover the closing balance, identity and attachments and display its period.'
  const requirement = { id: 'recovery', source, quote }
  const check = { kind: 'restore-rehearsal', target: 'navigation:example.overview', key: 'balance' }
  const stores = ['database', 'identity', 'attachments']
  const testName = 'Example.Recovery.RestoredAccount'
  const visual = { target: check.target, scenario: 'navigation', outcome: 'dashboard-context' }
  const obligations = [{ ...requirement, effect: 'recovery', proofs: [{ ...check, stores, expect: { closingBalance: 25 } },
    { integrationTest: testName }, visual] }]
  const write = (name, value) => writeFile(join(root, name), JSON.stringify(value))
  await writeFile(join(root, source), quote)
  await write('ai/brief-sources.json', [source])
  await write('ai/business-obligations.json', [{ ...requirement, effect: 'write', proofs: [visual] }])
  await assert.rejects(captureObligations(root, true), /persisted readback/)
  await write('ai/business-obligations.json', [{ ...requirement, effect: 'recovery', proofs: [{ integrationTest: testName }] }])
  await assert.rejects(captureObligations(root, true), /storage scope/)
  await write('ai/business-obligations.json', obligations)
  const trace = { documentRules: [], requirements: [{ ...requirement, checks: [check], integrationTests: [testName] }] }
  await write('ai/requirement-trace.json', trace)
  const cli = fileURLToPath(new URL('../../packaging/templates/content/infrastructure/ngb.mjs', import.meta.url))
  const capture = (...flags) => JSON.parse(execFileSync(process.execPath,
    [cli, 'ai', 'capture', '--app', root, '--format', 'json', ...flags], { encoding: 'utf8' }))
  const preview = capture()
  const recorded = capture('--apply')
  assert.deepEqual(preview.baseline, recorded.baseline)
  const baseline = await captureObligations(root, false)
  assert.deepEqual(recorded.baseline, baseline)
  const plan = { schemaVersion: 2, cases: [{ id: 'view', target: visual.target, scenario: visual.scenario, outcomes: [visual.outcome] }],
    requirements: [{ id: requirement.id, cases: ['view'] }], documents: [], businessChecks: [{ ...check, stores }] }
  const passed = new Set([testName])
  const observed = [{ test: testName, proof: { ...check, closingBalance: 25 } }]
  await verifyRequirementTrace(root, plan, passed, observed)
  await assert.rejects(verifyRequirementTrace(root, plan, passed, []), /Unexpected observed/)
  await assert.rejects(verifyRequirementTrace(root, plan, passed, [{ ...observed[0], proof: { ...check, closingBalance: 24 } }]), /closingBalance/)
  await assert.rejects(verifyRequirementTrace(root, { ...plan, cases: [] }, passed, observed), /browser outcome/)
  await assert.rejects(verifyRequirementTrace(root, { ...plan, businessChecks: [{ ...check, stores: ['database'] }] }, passed, observed), /recovery scope/)
  await write('ai/requirement-trace.json', { ...trace, requirements: [{ ...requirement, checks: [check] }] })
  await assert.rejects(verifyRequirementTrace(root, plan, passed, observed), /required integration test/)
  const previous = process.env.NGB_OBLIGATIONS_SHA256
  try {
    process.env.NGB_OBLIGATIONS_SHA256 = baseline.baselineSha256
    await captureObligations(root, false)
    await writeFile(join(root, source), quote + '\nNew business condition.')
    await assert.rejects(captureObligations(root, true), /Unreviewed brief text/)
  } finally {
    if (previous === undefined) delete process.env.NGB_OBLIGATIONS_SHA256
    else process.env.NGB_OBLIGATIONS_SHA256 = previous
  }
})

test('initial capture identifies omitted text and fingerprints explicit context alongside requirements', async t => {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'ngb-brief-')))
  t.after(() => rm(root, { recursive: true, force: true }))
  await mkdir(join(root, 'ai'))
  await writeFile(join(root, 'ai/operation-review.json'), '[]')
  const source = 'brief.md'
  const opening = 'Only active resources can start work.'
  const additional = '每项资源单独结算。'
  const heading = '# Operations'
  await writeFile(join(root, source), heading + '\n\n' + opening + '\n' + additional)
  const write = (file, data) => writeFile(join(root, 'ai', file), JSON.stringify(data))
  const obligation = { id: 'active', source, quote: opening, effect: 'write',
    proofs: [{ kind: 'state-invariant', target: 'catalog:example.resource', key: 'retire' }] }
  await write('business-obligations.json', [obligation])
  await write('brief-sources.json', [source])
  await write('brief-context.json', [{ source, quote: heading, reason: 'Section heading.' }])
  for (const apply of [false, true]) {
    await assert.rejects(captureObligations(root, apply), /brief.md:4: 每项资源/)
  }
  const lock = join(root, 'ai/business-obligations.lock.json')
  await assert.rejects(readFile(lock), { code: 'ENOENT' })
  const complete = [obligation, { id: 'scope', source, quote: additional, effect: 'read',
    proofs: [{ integrationTest: 'Example.Matchers.NeverMixResources' }] }]
  await write('business-obligations.json', complete)
  const preview = await captureObligations(root, false)
  assert.deepEqual(await captureObligations(root, true), preview)
  await write('brief-context.json', [{ source, quote: heading, reason: 'Updated explanation.' }])
  assert.notEqual((await captureObligations(root, false)).baselineSha256, preview.baselineSha256)
  await write('brief-context.json', [{ source, quote: heading, reason: ' ' }])
  await assert.rejects(captureObligations(root, true), /exact context excerpt/)
  assert.deepEqual(JSON.parse(await readFile(lock, 'utf8')), preview)
})

test('capture checks each independently declared source before writing a lock', async t => {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'ngb-appendix-')))
  t.after(() => rm(root, { recursive: true, force: true }))
  await mkdir(join(root, 'ai'))
  await writeFile(join(root, 'ai/operation-review.json'), '[]')
  const source = 'requirements.md'
  const appendix = 'limits.txt'
  const quote = 'Show the account position. The limits are specified in limits.txt.'
  const limit = 'Reject a withdrawal above the remaining balance.'
  const write = (name, value) => writeFile(join(root, 'ai', name), JSON.stringify(value))
  const reviewed = [{ id: 'position', source, quote, effect: 'read', proofs: [{ integrationTest: 'Example.Position' }] }]
  await writeFile(join(root, source), quote)
  await write('business-obligations.json', reviewed)
  await assert.rejects(captureObligations(root, true), /brief-sources.json/)
  for (const paths of [[], {}, [source, source.toUpperCase()], ['../limits.txt'], ['limits.json']]) {
    await write('brief-sources.json', paths)
    await assert.rejects(captureObligations(root, true))
  }

  await write('brief-sources.json', [source, appendix])
  await assert.rejects(captureObligations(root, true), /Missing declared brief source: limits.txt/)
  await writeFile(join(root, appendix), limit)
  await assert.rejects(captureObligations(root, true), /Unreviewed brief text at limits.txt/)
  await assert.rejects(readFile(join(root, 'ai/business-obligations.lock.json')), { code: 'ENOENT' })
  reviewed.push({ id: 'limits', source: appendix, quote: limit, effect: 'write',
    proofs: [{ kind: 'state-invariant', target: 'document:example.withdrawal', key: 'funds' }] })
  await write('business-obligations.json', reviewed)
  const cli = fileURLToPath(new URL('../../packaging/templates/content/infrastructure/ngb.mjs', import.meta.url))
  const args = [cli, 'ai', 'capture', '--app', root, '--format', 'json']
  const baseline = JSON.parse(execFileSync(process.execPath, [...args, '--apply'], { encoding: 'utf8' })).baseline
  assert.deepEqual(Object.keys(baseline.sources), [source, appendix])
  const pinned = { ...process.env, NGB_OBLIGATIONS_SHA256: baseline.baselineSha256 }
  execFileSync(process.execPath, args, { env: pinned })
  await writeFile(join(root, appendix), limit + '\n')
  assert.throws(() => execFileSync(process.execPath, args, { env: pinned }), /Command failed/)
  await write('brief-sources.json', [source])
  await assert.rejects(captureObligations(root, true), /outside ai\/brief-sources.json/)
})

test('capture requires fixture values and a row-growth budget independent of the observed results', async t => {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'ngb-fixture-')))
  t.after(() => rm(root, { recursive: true, force: true }))
  await mkdir(join(root, 'ai'))
  await writeFile(join(root, 'ai/operation-review.json'), '[]')
  const requirement = { id: 'position', source: 'brief.md', quote: 'Show the position for each resource.' }
  await writeFile(join(root, requirement.source), requirement.quote)
  const write = (name, data) => writeFile(join(root, 'ai', name), JSON.stringify(data))
  await write('brief-sources.json', [requirement.source])
  const target = 'report:example.position'
  const history = { kind: 'historical-cutoff', target, key: 'balance' }
  const growth = { kind: 'query-growth', target, key: 'resource-position' }
  const budget = { smallRows: 1, largeRows: 12, maxAdditionalQueries: 0 }
  const record = proofs => write('business-obligations.json', [{ ...requirement, effect: 'read', proofs }])
  for (const [kind, field] of [['report-export', 'expectedCells'], ['dated-withdrawal', 'expectedControl'],
    ['historical-cutoff', 'expected']]) {
    await record([{ ...history, kind, target: kind === 'dated-withdrawal' ? 'document:example.request' : target }])
    await assert.rejects(captureObligations(root, false), new RegExp('fixture expectation ' + field))
  }
  const fixture = { expected: 0, laterExpected: 7, timeZone: 'UTC' }
  const operation = 'GET /api/resource-position'
  const values = { valueFields: ['number'], operation }
  await record([{ ...history, expect: { expected: 0 } }])
  await assert.rejects(captureObligations(root, false), /laterExpected/)
  for (const [proof, error] of [
    [{ ...history, kind: 'xlsx-export', expect: { expectedCells: [7] } }, /Unknown server acceptance check: xlsx-export/],
    [{ ...history, kind: 'report-export', expect: { expectedCells: { total: 7 } } }, /populated business cell/],
    [{ ...history, expect: { expected: 0, laterExpected: 0, timeZone: 'UTC' } }, /observable effect/],
    [{ kind: 'dated-withdrawal', target: 'document:example.request', key: 'release',
      expect: { expectedControl: { source: null, consumer: null, balance: null } } }, /persisted lifecycle/],
  ]) {
    await record([proof])
    await assert.rejects(captureObligations(root, true), error)
  }
  await record([{ ...growth, budget }])
  await assert.rejects(captureObligations(root, false), /valueFields/)
  await record([{ ...history, expect: { ...fixture, timeZone: null } }])
  await captureObligations(root, false)
  await write('operation-review.json', [{ target, operation, requirement: requirement.id,
    facts: [{ kind: 'rows', key: growth.key }] }])
  await record([{ ...history, expect: fixture }, { ...growth, budget, expect: values }])
  await captureObligations(root, true)
  await write('requirement-trace.json', { documentRules: [], requirements: [{ ...requirement, checks: [history, growth] }] })
  const plan = { schemaVersion: 2, requirements: [{ id: requirement.id, cases: [] }], surfaces: [target], cases: [], documents: [],
    businessChecks: [history, { ...growth, budget }] }
  await verifyRequirementTrace(root, plan, new Set(), [{ proof: { ...history, ...fixture } }, { proof: { ...growth, ...values } }])
  await assert.rejects(verifyRequirementTrace(root, plan, new Set(), [{ proof: { ...history, expected: 0, laterExpected: 1 } }]), /laterExpected/)
  plan.businessChecks[1].budget = { ...budget, maxAdditionalQueries: 5 }
  await assert.rejects(verifyRequirementTrace(root, plan), /reviewed query-growth/)
})

test('release upgrade evidence distinguishes an unavailable predecessor from a verified release pair', async t => {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'ngb-release-baseline-')))
  t.after(() => rm(root, { recursive: true, force: true }))
  await mkdir(join(root, 'ai'))
  const path = join(root, 'ai/application-upgrade.json')
  const unavailable = { currentRelease: '2.0', previousRelease: null, reason: 'First release supported by this application.' }
  await assert.rejects(verifyApplicationUpgrade(root, []), /Declare the application release baseline/)
  await writeFile(path, JSON.stringify(unavailable))
  assert.deepEqual(await verifyApplicationUpgrade(root, []), { status: 'unavailable', ...unavailable })
  await assert.rejects(verifyApplicationUpgrade(root, [{}]), /conflict/)
  await writeFile(path, JSON.stringify({ ...unavailable, reason: ' ' }))
  await assert.rejects(verifyApplicationUpgrade(root, []), /Explain/)

  const manifest = '{"release":"1.0"}'
  const digest = createHash('sha256').update(manifest).digest('hex')
  const entry = { currentRelease: '2.0', previousRelease: '1.0', sourceManifest: 'release.json',
    sourceManifestSha256: digest, test: 'ReleaseTests.ReopensOriginal' }
  const proof = { currentRelease: '2.0', previousRelease: '1.0', sourceManifestSha256: digest,
    recordId: 'record-7', reopenedId: 'record-7', seedStatus: 201, readStatus: 200, before: { quantity: 7 }, after: { quantity: 7 } }
  await writeFile(join(root, entry.sourceManifest), manifest)
  await writeFile(path, JSON.stringify(entry))
  const observation = { test: entry.test, proof }
  assert.deepEqual(await verifyApplicationUpgrade(root, [observation]), { status: 'verified', ...entry, recordId: proof.recordId })
  await assert.rejects(verifyApplicationUpgrade(root, []), /one release upgrade observation/)
  await assert.rejects(verifyApplicationUpgrade(root, [observation, observation]), /one release upgrade observation/)
  await assert.rejects(verifyApplicationUpgrade(root, [{ test: 'Other', proof }]), /different test/)
  for (const change of [{ previousRelease: '0.9' }, { readStatus: 404 }, { reopenedId: 'record-2' },
    { before: {} }, { after: { quantity: 8 } }]) {
    await assert.rejects(verifyApplicationUpgrade(root, [{ ...observation, proof: { ...proof, ...change } }]))
  }
  await writeFile(join(root, entry.sourceManifest), manifest + '\n')
  await assert.rejects(verifyApplicationUpgrade(root, [observation]), /pinned digest/)
})
