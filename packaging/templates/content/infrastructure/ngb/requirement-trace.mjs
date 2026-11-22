import assert from 'node:assert/strict'
import { readAsset } from './file-data.mjs'
import { objectKeys } from './kit-contracts.mjs'

const binding = check => JSON.stringify([check.kind, check.target, check.key])

// Capture the source-to-proof inventory before implementation. An independent
// requirement binding survives removing a rule and its test from the plan.
export async function verifyRequirementTrace(root, plan, passedTests) {
  if (plan.schemaVersion === 1) return
  const raw = await readAsset(root, 'ai/requirement-trace.json')
  assert.ok(raw, 'Map the original business brief to executable evidence in ai/requirement-trace.json.')
  const trace = JSON.parse(raw)
  objectKeys(trace, ['requirements', 'documentRules'])
  assert.ok(Array.isArray(trace.requirements), 'List the original requirements.')
  assert.deepEqual(trace.requirements.map(row => row.id).sort(), plan.requirements.map(row => row.id).sort(),
    'Reconcile the source requirements with ai/acceptance.json.')
  const sources = new Map()
  const checked = new Set()
  const declared = new Map(plan.businessChecks.map(check => [binding(check), check]))
  for (const row of trace.requirements) {
    objectKeys(row, ['id', 'source', 'quote', 'checks'], ['integrationTests', 'browserOnlyReason'])
    assert.match(row.source, /\.(?:md|txt)$/i, 'Reference the original text of the business brief.')
    if (!sources.has(row.source)) sources.set(row.source, await readAsset(root, row.source))
    assert.ok(typeof row.quote === 'string' && row.quote.trim() && sources.get(row.source)?.includes(row.quote),
      `Cannot find the cited requirement in ${row.source}: ${row.id}`)
    assert.ok(Array.isArray(row.checks), 'List the required server observations.')
    const requirement = plan.requirements.find(item => item.id === row.id)
    const tests = row.integrationTests ?? []
    assert.ok(Array.isArray(tests) && tests.every(name => typeof name === 'string' && name.trim())
      && tests.length === new Set(tests).size, 'Bind distinct integration test names.')
    if (passedTests) {
      for (const name of tests) assert.ok(passedTests.has(name), `Missing passed integration test for ${row.id}: ${name}`)
    }
    if (!row.checks.length && !tests.length) {
      assert.ok(requirement.cases.length && typeof row.browserOnlyReason === 'string' && row.browserOnlyReason.trim(),
        `Explain browser-only coverage or bind real server checks: ${row.id}`)
    }
    for (const check of row.checks) {
      objectKeys(check, ['kind', 'target', 'key'], ['workingSet'])
      const id = binding(check)
      assert.ok(declared.has(id), `Missing required server check for ${row.id}: ${id}`)
      if (check.workingSet !== undefined) {
        assert.ok(check.workingSet === true && check.kind === 'capacity' && declared.get(id).budget.maxWorkingSetBytes,
          'A memory requirement must bind a working-set budget.')
      }
      checked.add(id)
    }
  }
  assert.deepEqual([...checked].sort(), [...declared.keys()].sort(), 'Trace every server observation to a source requirement.')

  const documents = plan.documents.map(row => row.type).filter(type => type !== 'general_journal_entry')
  assert.ok(Array.isArray(trace.documentRules), 'Classify the dated business rules for each application document.')
  assert.deepEqual(trace.documentRules.map(row => row.type).sort(), documents.sort(), 'Review every application document.')
  for (const row of trace.documentRules) {
    objectKeys(row, ['type', 'effective-prerequisite', 'interval-overlap', 'snapshot-stability'])
    for (const kind of ['effective-prerequisite', 'interval-overlap', 'snapshot-stability']) {
      const rules = row[kind]
      const keys = plan.businessChecks.filter(check => check.target === 'document:' + row.type && check.kind === kind)
        .map(check => check.key).sort()
      if (Array.isArray(rules)) {
        assert.ok(rules.length, `Bind business keys or explain why ${kind} does not apply to ${row.type}.`)
        assert.deepEqual([...rules].sort(), keys, `Unverified ${kind} rule for ${row.type}.`)
      } else {
        objectKeys(rules, ['reason'])
        assert.ok(!keys.length && typeof rules.reason === 'string' && rules.reason.trim(),
          `Explain why ${kind} does not apply to ${row.type}.`)
      }
    }
  }
}

export function verifyPresentationInventory(plan, inventory, icons) {
  assert.ok(Array.isArray(icons) && icons.includes('home'), 'Missing SDK menu icon catalog.')
  const isDashboard = item => ['/home', '/dashboard'].includes(item.route) || item.label?.toLowerCase() === 'dashboard'
  if (plan.workflows?.dashboards?.length) {
    assert.ok(inventory.menuGroups.some(group => group.items.some(isDashboard)), 'Register the required dashboard in the main menu.')
  }
  for (const group of inventory.menuGroups) {
    assert.ok(group.icon == null || icons.includes(group.icon), `Unknown menu group icon: ${group.label}/${group.icon}`)
    for (const item of group.items) {
      assert.ok(item.icon == null || icons.includes(item.icon), `Unknown menu item icon: ${item.code}/${item.icon}`)
      if (!isDashboard(item)) continue
      assert.ok(inventory.menuGroups[0] === group && group.items.length === 1 && group.label === item.label
        && group.icon === 'home' && item.icon === 'home', 'Use a standalone Dashboard root with the home icon.')
      assert.ok(plan.cases.some(entry => entry.target === 'navigation:' + item.code
        && entry.outcomes.includes('dashboard-home')), 'Verify the rendered home icon and dashboard destination.')
      for (const size of ['desktop', 'mobile']) {
        assert.ok(plan.cases.some(entry => entry.target === 'navigation:' + item.code
          && entry.outcomes.includes('dashboard-layout:' + size)), `Missing ${size} dashboard layout proof.`)
      }
      assert.ok(plan.cases.some(entry => entry.target === 'navigation:' + item.code
        && entry.outcomes.includes('business-calendar')), 'Verify the dashboard business date across client time zones.')
    }
  }
  for (const report of inventory.reports) {
    const fields = report.dataset?.fields ?? []
    for (const field of fields) {
      const display = field.code.endsWith('_id')
        ? fields.find(other => other.code === field.code.slice(0, -3) + '_display')
        : undefined
      if (!display && !(field.lookup && ['uuid', 'guid'].includes(field.dataType?.toLowerCase()))) continue
      assert.ok(!field.isSelectable && !field.isGroupable, `Hide the supporting ID in ${report.reportCode}/${field.code}.`)
      if (!display?.isSelectable) continue
      assert.ok(plan.cases.some(entry => entry.target === 'report:' + report.reportCode
        && entry.outcomes.includes('report-reference:' + display.code)),
      `Verify the visible reference and its destination: ${report.reportCode}/${display.code}`)
    }
  }
}
