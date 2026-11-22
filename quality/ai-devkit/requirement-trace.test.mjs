import assert from 'node:assert/strict'
import { mkdir, mkdtemp, realpath, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import { verifyPresentationInventory, verifyRequirementTrace } from '../../packaging/templates/content/infrastructure/ngb/requirement-trace.mjs'

const rule = { kind: 'interval-overlap', target: 'document:example.booking', key: 'resource' }
const capacity = { kind: 'capacity', target: 'report:example.activity', key: 'monthly' }

test('source bindings survive deletion of a declared check and preserve dated and memory requirements', async t => {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'ngb-trace-')))
  t.after(() => rm(root, { recursive: true, force: true }))
  await mkdir(join(root, 'ai'))
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
  await verifyRequirementTrace(root, plan)
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
  await verifyRequirementTrace(root, { schemaVersion: 1 })
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
    { target: 'navigation:example.home', outcomes: ['dashboard-home', 'dashboard-layout:desktop', 'dashboard-layout:mobile', 'business-calendar'] },
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
