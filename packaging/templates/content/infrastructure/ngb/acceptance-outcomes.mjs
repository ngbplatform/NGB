import assert from 'node:assert/strict'
import { objectKeys } from './kit-contracts.mjs'

// Named outcomes prevent an unrelated visible assertion from satisfying a scenario.
// The browser must still derive its expected values from the business requirement.
export const scenarioOutcomes = {
  access: ['access-result'],
  'draft-edit': ['saved', 'reopened'],
  'form-labels': ['named-fields'],
  validation: ['refusal', 'corrected'],
  lifecycle: ['posted', 'unposted', 'reposted', 'effects-restored'],
  reversal: ['posted', 'reversal-target', 'original-preserved', 'net-effects'],
  'document-flow': ['linked-records', 'linked-target'],
  'document-chain': ['linked-chain', 'linked-target'],
  'withdrawal-blocked': ['reason', 'unchanged-effects', 'correction-path'],
  lines: ['line-values', 'reference-target'],
  'source-change-clear': ['replacement-header', 'replacement-lines', 'cleared-header', 'cleared-lines', 'cleared-totals'],
  'lookup-eligibility': ['eligible', 'ineligible', 'reopened-selection'],
  pricing: ['preview', 'date-change', 'posted', 'snapshot'],
  'async-race': ['late-source', 'late-dependent', 'late-failure'],
  search: ['opened-target'],
  'search-denial': ['denied-result', 'permitted-result'],
  report: ['figures', 'drill-down', 'source-target', 'short-description'],
  'summary-report': ['figures', 'period', 'short-description'],
  'diagnostic-report': ['empty-state', 'checked-scope', 'short-description'],
  navigation: ['single-navigation', 'destination'],
  shell: ['root-dashboard', 'product-name'],
  'numbering-date': ['prior-year', 'next-year', 'reopened', 'seed'],
  'temporal-restoration': ['unused-withdrawal', 'previous-value', 'later-value', 'history', 'correction'],
  'prerequisite-withdrawal': ['blocked-consumer', 'unchanged-effects', 'allowed-after-release'],
  'dashboard-complete': ['beyond-page-limit', 'metric-scope', 'reconciled-total'],
  'dashboard-access': ['authorized-figures', 'authorized-navigation'],
  settlement: ['issued-amount', 'settlement-target', 'remaining-balance', 'reopened-balance'],
  'partial-adjustment': ['partial-amount', 'final-residual', 'net-effects'],
  'historical-classification': ['original-group', 'reclassified-catalog', 'original-group-total'],
  'concurrent-operation': ['competing-requests', 'committed-effects', 'reopened-state'],
  'request-replay': ['same-request', 'conflicting-request', 'committed-effects'],
  'historical-as-of': ['before-later-event', 'after-later-event'],
}

export function validateOutcomes(plan) {
  assert.ok(Array.isArray(plan.requirements), 'Declare business requirements and their executable cases.')
  const ids = new Set()
  for (const requirement of plan.requirements) {
    objectKeys(requirement, ['id', 'description', 'scenarios', 'cases'])
    assert.ok(typeof requirement.id === 'string' && requirement.id.trim() && !ids.has(requirement.id),
      'Missing or duplicate business requirement identity.')
    ids.add(requirement.id)
    assert.ok(typeof requirement.description === 'string' && requirement.description.trim(), 'Missing business outcome.')
    assert.ok(Array.isArray(requirement.cases) && requirement.cases.length > 0,
      `Required business outcome has no executable cases: ${requirement.id}`)
    assert.ok(Array.isArray(requirement.scenarios) && requirement.scenarios.length > 0,
      `Classify acceptance risks for requirement: ${requirement.id}`)
    for (const scenario of requirement.scenarios) {
      assert.ok(scenarioOutcomes[scenario], `Unknown requirement scenario: ${scenario}`)
      assert.ok(plan.cases.some(entry => requirement.cases.includes(entry.id) && entry.scenario === scenario),
        `Required scenario has no executable case: ${requirement.id}/${scenario}`)
    }
    for (const id of requirement.cases) {
      assert.ok(plan.cases.some(entry => entry.id === id), `Unknown requirement case: ${id}`)
    }
  }
  for (const entry of plan.cases) {
    const required = scenarioOutcomes[entry.scenario]
    assert.ok(required, `Unknown outcome scenario: ${entry.scenario}`)
    assert.ok(Array.isArray(entry.outcomes) && new Set(entry.outcomes).size === entry.outcomes.length,
      `Declare unique outcomes: ${entry.id}`)
    for (const name of required) {
      assert.ok(entry.outcomes.includes(name), `Missing expected outcome: ${entry.id}/${name}`)
    }
    assert.ok(plan.requirements.some(item => item.cases.includes(entry.id)),
      `Case has no business requirement: ${entry.id}`)
  }
}

export function reconcileSurfaces(plan, inventory) {
  const actual = [
    ...inventory.documents.map(item => `document:${item.documentType}`),
    ...(inventory.catalogs ?? []).map(item => `catalog:${item.catalogType}`),
    ...(inventory.reports ?? []).map(item => `report:${item.reportCode}`),
    ...(inventory.menu ?? []).map(item => `navigation:${item.code}`),
  ].sort()
  assert.deepEqual([...plan.surfaces].sort(), actual,
    'Acceptance must include every runtime catalog, report and menu destination as well as documents.')
  for (const target of actual) {
    const kind = target.split(':')[0]
    const scenarios = kind === 'navigation' ? ['navigation'] : kind === 'report' ? ['report'] : ['search']
    // These built-in SDK views also support a summary-only or a healthy, empty diagnostic result.
    if (target === 'report:accounting.cash_flow_statement_indirect') scenarios.push('summary-report')
    if (target === 'report:accounting.consistency') scenarios.push('diagnostic-report')
    assert.ok(plan.cases.some(entry => entry.target === target && scenarios.includes(entry.scenario)),
      `Missing surface acceptance: ${target}/${scenarios.join(' or ')}`)
  }
  for (const document of inventory.documents) {
    const target = `document:${document.documentType}`
    const identity = document.presentation?.hasNumber === false ? 'name' : 'number-or-code'
    assert.ok(plan.cases.some(entry => entry.target === target && entry.scenario === 'search' && entry.outcomes.includes(identity)),
      `Missing search by ${identity} for ${target}.`)
  }
  for (const catalog of inventory.catalogs ?? []) {
    const target = `catalog:${catalog.catalogType}`
    assert.ok(plan.cases.some(entry => entry.target === target && entry.scenario === 'search' && entry.outcomes.includes('name')),
      `Missing search by name for ${target}.`)
  }
  for (const report of inventory.reports ?? []) {
    const description = report.description?.trim()
    assert.ok(description && [...description].length <= 160 && !/[\r\n]/u.test(description),
      `Provide a short, single-line description for ${report.reportCode}.`)
  }
}

// Metadata identifies document references even when the application forgot to mirror them.
export function businessReferences(metadata, informational = []) {
  const references = new Map()
  function inspect(key, field) {
    if (field.lookup?.kind === 'document' || field.mirroredRelationship) references.set(key, field)
  }
  for (const section of metadata.form?.sections ?? []) {
    for (const row of section.rows) {
      for (const field of row.fields) inspect(field.key, field)
    }
  }
  for (const part of metadata.parts ?? []) {
    for (const field of part.list?.columns ?? []) inspect(`${part.partCode}.${field.key}`, field)
  }
  assert.ok(Array.isArray(informational), 'List informational document references and their business purpose.')
  for (const item of informational) {
    objectKeys(item, ['field', 'reason'])
    assert.ok(references.has(item.field) && !references.get(item.field).mirroredRelationship,
      `Unknown, duplicate or mirrored informational reference: ${item.field}`)
    assert.ok(typeof item.reason === 'string' && item.reason.trim(), `Explain informational reference: ${item.field}`)
    references.delete(item.field)
  }
  return [...references.keys()]
}

const workflowScenarios = {
  flows: 'document-flow',
  chains: 'document-chain',
  settlements: 'settlement',
  adjustments: 'partial-adjustment',
  historicalGroups: 'historical-classification',
  concurrentWrites: 'concurrent-operation',
  replays: 'request-replay',
  dashboards: 'dashboard-access',
  metrics: 'dashboard-complete',
}

export function reconcileWorkflows(plan, inventory) {
  const workflows = plan.workflows
  assert.ok(workflows, 'Classify the business workflows from the requirements in ai/acceptance.json. See browser-acceptance.md.')
  objectKeys(workflows, Object.keys(workflowScenarios))
  for (const [kind, scenario] of Object.entries(workflowScenarios)) {
    const bindings = workflows[kind]
    assert.ok(Array.isArray(bindings) && bindings.every(id => typeof id === 'string') && new Set(bindings).size === bindings.length,
      `List distinct business requirement IDs for workflows.${kind}.`)
    for (const requirement of plan.requirements) {
      if (requirement.scenarios.includes(scenario)) {
        assert.ok(bindings.includes(requirement.id), `Classify ${requirement.id} under workflows.${kind}.`)
      }
    }
    for (const id of bindings) {
      const requirement = plan.requirements.find(item => item.id === id)
      assert.ok(requirement?.scenarios.includes(scenario), `Missing ${scenario} requirement: ${id}`)
      const cases = plan.cases.filter(entry => requirement.cases.includes(entry.id) && entry.scenario === scenario)
      if (kind === 'dashboards') {
        for (const target of new Set(cases.map(entry => entry.target))) {
          for (const role of inventory.roles) {
            assert.ok(cases.some(entry => entry.target === target && entry.role === role), `Missing dashboard acceptance: ${target}/${role}`)
          }
        }
      }
      if (kind === 'concurrentWrites' || kind === 'replays' || kind === 'metrics') {
        const otherCases = new Set(plan.requirements
          .filter(item => item.id !== id && bindings.includes(item.id))
          .flatMap(item => item.cases))
        assert.ok(cases.some(entry => !otherCases.has(entry.id)), `Provide a dedicated ${scenario} test for operation: ${id}`)
      }
    }
  }
  const dashboardCases = new Set(plan.requirements.filter(item => workflows.dashboards.includes(item.id)).flatMap(item => item.cases))
  for (const item of inventory.menu ?? []) {
    if (item.route !== '/dashboard') continue
    const target = `navigation:${item.code}`
    assert.ok(plan.cases.some(entry => entry.target === target && entry.scenario === 'dashboard-access' && dashboardCases.has(entry.id)),
      `Missing role coverage for dashboard: ${item.code}`)
  }
}

export function verifyOutcomeProof(entry, proof) {
  assert.ok(Array.isArray(proof.outcomes), 'Missing named browser outcomes.')
  assert.deepEqual([...proof.outcomes].sort(), [...entry.outcomes].sort(),
    `Browser did not verify every expected outcome: ${entry.id}`)
  for (const name of entry.outcomes) {
    const check = name.startsWith('reference:') ? name : requiredChecks[name]
    if (check) assert.ok(Array.isArray(proof.evidence?.[name]) && proof.evidence[name].includes(check),
      `Use the ${check} check for ${entry.id}/${name} and verify its expected business value.`)
  }
}

const requiredChecks = {
  'named-fields': 'accessible-fields',
  'linked-records': 'flow',
  'linked-chain': 'chain',
  'linked-target': 'record',
  'opened-target': 'record',
  'settlement-target': 'record',
  'reversal-target': 'record',
  'source-target': 'record',
  figures: 'figure',
  'issued-amount': 'figure',
  'remaining-balance': 'figure',
  'reopened-balance': 'figure',
  'partial-amount': 'figure',
  'final-residual': 'figure',
  'net-effects': 'figure',
  'original-group-total': 'figure',
  'reconciled-total': 'figure',
  'metric-scope': 'metric-drilldown',
  period: 'report-scope',
  'checked-scope': 'report-scope',
  'authorized-figures': 'permissions',
  'authorized-navigation': 'navigation',
  'competing-requests': 'concurrent',
  'same-request': 'replay',
  'conflicting-request': 'conflicting-replay',
}
