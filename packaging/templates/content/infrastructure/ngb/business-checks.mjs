import assert from 'node:assert/strict'
import { objectKeys } from './kit-contracts.mjs'
import { readTestXml } from './project-data.mjs'

const kinds = {
  'source-membership': 'document',
  'withdrawal-cycle': 'document',
  'historical-cutoff': 'report',
  'worker-backlog': 'job',
}

const identity = entry => JSON.stringify([entry.kind, entry.target, entry.key])

function nonempty(value) {
  return typeof value === 'string' && value.trim().length > 0
}

export function validateBusinessChecks(plan) {
  assert.ok(Array.isArray(plan.businessChecks), 'Declare businessChecks in schema 2. See server-acceptance.md.')
  const exemptions = plan.nonBatchJobs ?? []
  assert.ok(Array.isArray(exemptions) && new Set(exemptions.map(item => item.id)).size === exemptions.length,
    'Classify each non-batch application job once.')
  for (const item of exemptions) {
    objectKeys(item, ['id', 'reason'])
    assert.ok(nonempty(item.id) && nonempty(item.reason), 'Explain which single operation the non-batch job performs.')
  }
  const bindings = new Set()
  for (const entry of plan.businessChecks) {
    objectKeys(entry, ['kind', 'target', 'key', 'test'], ['unrestrictedReason'])
    assert.ok(Object.hasOwn(kinds, entry.kind), 'Unknown server acceptance check.')
    const prefix = kinds[entry.kind] + ':'
    assert.ok(nonempty(entry.target) && entry.target.startsWith(prefix) && nonempty(entry.target.slice(prefix.length)),
      'Invalid server acceptance target.')
    if (entry.kind !== 'worker-backlog') assert.ok(plan.surfaces.includes(entry.target), 'Unknown server acceptance surface.')
    assert.ok(nonempty(entry.key) && nonempty(entry.test), 'Bind each server check to a business key and integration test.')
    if (entry.unrestrictedReason !== undefined) {
      assert.ok(entry.kind === 'source-membership' && nonempty(entry.unrestrictedReason),
        'Explain why distinct in-scope sources are interchangeable.')
    }
    assert.ok(!bindings.has(identity(entry)), 'Duplicate server acceptance binding.')
    bindings.add(identity(entry))
  }
}

export function requireBusinessCheck(plan, kind, target, key) {
  assert.ok(plan.businessChecks.some(entry => entry.kind === kind && entry.target === target
    && (key === undefined || key === entry.key)), `Missing ${kind} integration check for ${target}/${key ?? '*'}. See server-acceptance.md.`)
}

export function reconcileHistoricalChecks(plan, reports) {
  for (const report of reports) {
    const target = `report:${report.reportCode}`
    const cutoff = report.parameters?.some(parameter => /^asof(?:utc|date|time|at)?$/i.test(parameter.code.replaceAll('_', '')))
    const declared = plan.businessChecks.some(entry => entry.target === target && entry.kind === 'historical-cutoff')
    if (!cutoff && !declared && !plan.cases.some(entry => entry.target === target && entry.scenario === 'historical-as-of')) continue
    requireBusinessCheck(plan, 'historical-cutoff', target)
    for (const measure of report.dataset?.measures ?? []) requireBusinessCheck(plan, 'historical-cutoff', target, measure.code)
  }
}

// xUnit writes the integration test's own output inside its UnitTestResult. Global
// run output, unrelated passing tests and stale files cannot satisfy a binding.
export function integrationObservations(xml) {
  const root = readTestXml(xml)
  const observations = []
  for (const result of root.children.find(node => node.name === 'Results')?.children ?? []) {
    if (result.name !== 'UnitTestResult' || result.attributes.outcome !== 'Passed') continue
    const output = result.children.find(node => node.name === 'Output')
    const stdout = output?.children.find(node => node.name === 'StdOut')?.text ?? ''
    for (const line of stdout.split(/\r?\n/)) {
      const prefix = 'NGB-CHECK '
      if (!line.trimStart().startsWith(prefix)) continue
      observations.push({ test: result.attributes.testName, proof: JSON.parse(line.trimStart().slice(prefix.length)) })
    }
  }
  return observations
}

function successful(status) {
  return Number.isInteger(status) && status >= 200 && status < 300
}

function unchanged(before, after) {
  assert.ok(before && typeof before === 'object' && Object.keys(before).length > 0, 'Capture the persisted business state.')
  assert.deepEqual(after, before, 'A refused operation changed the persisted state.')
}

function verifyObservation(entry, proof) {
  if (entry.kind === 'source-membership') {
    assert.ok(nonempty(proof.correctSource) && nonempty(proof.otherSource) && proof.correctSource !== proof.otherSource,
      'Use two distinct persisted sources of the same eligible type and status.')
    assert.ok(nonempty(proof.sourceType) && proof.otherType === proof.sourceType
      && proof.sourceState != null && proof.otherState === proof.sourceState, 'Use sources of identical type and lifecycle state.')
    assert.ok(successful(proof.correctStatus), 'The matching source must succeed for this actor.')
    if (entry.unrestrictedReason) {
      assert.ok(successful(proof.otherStatus), 'The documented interchangeable source must also succeed.')
    } else {
      assert.ok([400, 409, 422].includes(proof.otherStatus) && nonempty(proof.reason) && proof.reason === proof.expectedReason, 'Expected a business refusal for the unrelated source.')
      unchanged(proof.beforeRefusal, proof.afterRefusal)
    }
  } else if (entry.kind === 'withdrawal-cycle') {
    const events = [proof.prerequisite, proof.laterUse, proof.laterRelease]
    assert.ok(events.every(nonempty) && new Set(events).size === events.length, 'Exercise a distinct, completed later cycle.')
    assert.notEqual(proof.initialState, undefined, 'Capture the original business state.')
    assert.deepEqual(proof.restoredState, proof.initialState, 'The later cycle must restore the same apparent state.')
    assert.ok([400, 409, 422].includes(proof.refusalStatus) && nonempty(proof.reason) && proof.reason === proof.expectedReason, 'Expected a chronology or dependency refusal.')
    unchanged(proof.beforeRefusal, proof.afterRefusal)
    assert.ok(successful(proof.recoveryStatus), 'Exercise the permitted recovery sequence.')
  } else if (entry.kind === 'historical-cutoff') {
    assert.ok(nonempty(proof.cutoff) && proof.repeatedCutoff === proof.cutoff, 'Execute the same historical cutoff twice.')
    assert.ok(Date.parse(proof.laterEffectiveAt) > Date.parse(proof.cutoff) && nonempty(proof.laterEvent) && successful(proof.laterStatus),
      'Commit a distinct later event affecting this measure.')
    assert.ok(Object.hasOwn(proof, 'expected') && Object.hasOwn(proof, 'before') && Object.hasOwn(proof, 'after'),
      'Capture the expected and both observed values for this measure.')
    assert.deepEqual(proof.before, proof.expected, 'Incorrect original historical value.')
    assert.deepEqual(proof.after, proof.expected, 'A later event changed the earlier result.')
    assert.ok(Object.hasOwn(proof, 'laterExpected') && Object.hasOwn(proof, 'laterActual'), 'Read a control result at the later cutoff.')
    assert.deepEqual(proof.laterActual, proof.laterExpected, 'Incorrect later-period control result.')
  } else {
    assert.ok(Number.isSafeInteger(proof.batchLimit) && proof.batchLimit > 0, 'Use the effective worker batch limit.')
    assert.ok(Array.isArray(proof.eligible) && proof.eligible.length > proof.batchLimit
      && proof.eligible.every(nonempty) && new Set(proof.eligible).size === proof.eligible.length,
    'Seed distinct eligible business keys beyond the actual batch limit.')
    assert.ok(Number.isSafeInteger(proof.runs) && proof.runs >= 2, 'Exercise successive worker runs with a stable eligible prefix.')
    const expected = [...proof.eligible].sort()
    for (const observed of [proof.delivered, proof.afterReplay]) {
      assert.ok(Array.isArray(observed), 'Read all persisted delivery keys after processing and replay.')
      assert.deepEqual([...observed].sort(), expected, 'Missing or duplicate worker delivery.')
    }
  }
}

export function verifyBusinessObservations(plan, observations, workerHost = false) {
  validateBusinessChecks(plan)
  const pending = new Map(plan.businessChecks.map(entry => [identity(entry), entry]))
  const inventories = observations.filter(item => item.proof.kind === 'worker-inventory')
  if (workerHost) assert.ok(inventories.length, 'Collect the application job inventory from the composed worker host.')
  const jobs = new Set()
  for (const { proof } of inventories) {
    assert.ok(Array.isArray(proof.jobs) && proof.jobs.every(nonempty), 'Invalid worker job inventory.')
    for (const id of proof.jobs) jobs.add(id)
  }
  const classified = new Set(plan.businessChecks.filter(entry => entry.kind === 'worker-backlog')
    .map(entry => entry.target.slice('job:'.length)))
  for (const { id } of plan.nonBatchJobs ?? []) {
    assert.ok(!classified.has(id), `Conflicting worker classifications: ${id}`)
    classified.add(id)
  }
  assert.deepEqual([...classified].sort(), [...jobs].sort(), 'Classify every registered application job and remove stale job bindings.')
  for (const { test, proof } of observations) {
    if (proof.kind === 'worker-inventory') continue
    const id = identity(proof)
    const entry = pending.get(id)
    assert.ok(entry && entry.test === test, `Unexpected, duplicate or incorrectly bound integration evidence: ${id}/${test}`)
    verifyObservation(entry, proof)
    pending.delete(id)
  }
  assert.equal(pending.size, 0, `Missing integration evidence: ${[...pending.keys()].join(', ')}`)
  return { verified: plan.businessChecks.length, applicationJobs: jobs.size }
}
