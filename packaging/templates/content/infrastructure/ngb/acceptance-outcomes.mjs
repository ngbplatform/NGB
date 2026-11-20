import assert from 'node:assert/strict'
import { objectKeys } from './kit-contracts.mjs'

// Named outcomes prevent an unrelated visible assertion from satisfying a scenario.
// The browser must still derive its expected values from the business requirement.
export const scenarioOutcomes = {
  access: ['access-result'],
  'draft-edit': ['saved', 'reopened'],
  validation: ['refusal', 'corrected'],
  lifecycle: ['posted', 'unposted', 'reposted', 'effects-restored'],
  'withdrawal-blocked': ['reason', 'unchanged-effects', 'correction-path'],
  lines: ['line-values', 'reference-target'],
  'source-change-clear': ['replacement-header', 'replacement-lines', 'cleared-header', 'cleared-lines', 'cleared-totals'],
  'lookup-eligibility': ['eligible', 'ineligible', 'reopened-selection'],
  pricing: ['preview', 'date-change', 'posted', 'snapshot'],
  'async-race': ['late-source', 'late-dependent', 'late-failure'],
  search: ['opened-target'],
  'search-denial': ['denied-result', 'permitted-result'],
  report: ['figures', 'drill-down', 'source-target', 'short-description'],
  navigation: ['single-navigation', 'destination'],
  shell: ['root-dashboard', 'product-name'],
  'numbering-date': ['prior-year', 'next-year', 'reopened', 'seed'],
  'temporal-restoration': ['unused-withdrawal', 'previous-value', 'later-value', 'history', 'correction'],
  'prerequisite-withdrawal': ['blocked-consumer', 'unchanged-effects', 'allowed-after-release'],
  'dashboard-complete': ['beyond-page-limit', 'reconciled-total'],
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
    const scenario = kind === 'navigation' ? 'navigation' : kind === 'report' ? 'report' : 'search'
    assert.ok(plan.cases.some(entry => entry.target === target && entry.scenario === scenario),
      `Missing surface acceptance: ${target}/${scenario}`)
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

export function verifyOutcomeProof(entry, proof) {
  assert.ok(Array.isArray(proof.outcomes), 'Missing named browser outcomes.')
  assert.deepEqual([...proof.outcomes].sort(), [...entry.outcomes].sort(),
    `Browser did not verify every expected outcome: ${entry.id}`)
}
