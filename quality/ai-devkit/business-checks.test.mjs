import assert from 'node:assert/strict'
import test from 'node:test'
import { integrationObservations, reconcileHistoricalChecks, requireBusinessCheck, validateBusinessChecks,
  verifyBusinessObservations } from '../../packaging/templates/content/infrastructure/ngb/business-checks.mjs'

const target = 'document:example.release'
const report = 'report:example.position'
const job = 'job:example.dispatch'
const beforeRefusal = { posted: ['first', 'second'], movements: 2 }
const refusal = { reason: 'dependency', expectedReason: 'dependency', beforeRefusal, afterRefusal: beforeRefusal }
const ids = ['a', 'b', 'c']

function check(kind, changes = {}, options = {}) {
  const samples = {
    'effective-prerequisite': { target, key: 'funding', attemptAt: '2026-04-01', prerequisiteAt: '2026-04-02',
      controlAt: '2026-04-02', controlStatus: 200, refusalStatus: 409, ...refusal },
    'history-conflict': { target, key: 'schedule', pageLimit: 2, newerIds: ['b', 'c'], conflictId: 'a',
      controlStatus: 201, refusalStatus: 409, ...refusal },
    capacity: { target: report, key: 'position', populations: { items: 1100 },
      samples: [{ status: 200, milliseconds: 30, queries: 3 }, { status: 200, milliseconds: 45, queries: 4 }] },
    'source-membership': { target, key: 'source', sourceType: 'request', otherType: 'request', sourceState: 'Posted',
      otherState: 'Posted', correctSource: 'a', otherSource: 'b', otherStatus: 422, correctStatus: 200, ...refusal },
    'withdrawal-cycle': { target, key: 'capacity', prerequisite: 'one', laterUse: 'two', laterRelease: 'three',
      initialState: { available: 1 }, restoredState: { available: 1 }, refusalStatus: 409, recoveryStatus: 204, ...refusal },
    'historical-cutoff': { target: report, key: 'available', cutoff: '2026-04-02', repeatedCutoff: '2026-04-02',
      laterEvent: 'three', laterEffectiveAt: '2026-04-03', laterStatus: 200, expected: 0, before: 0, after: 0,
      laterExpected: 5, laterActual: 5 },
    'worker-backlog': { target: job, key: 'notice', batchLimit: 2, runs: 2, eligible: ids, delivered: ids, afterReplay: ids },
  }
  const proof = { kind, ...samples[kind], ...changes }
  const entry = { kind, target: proof.target, key: proof.key, test: 'Example.Tests.ReadBack', ...options }
  if (kind === 'capacity') entry.budget = { populations: { items: 1000 }, samples: 2, p95Milliseconds: 100, maxQueries: 4 }
  const plan = { businessChecks: [entry], surfaces: [target, report], cases: [] }
  const observations = [{ test: entry.test, proof }]
  if (kind === 'worker-backlog') observations.push({ test: 'Worker.Test', proof: { kind: 'worker-inventory', jobs: ['example.dispatch'] } })
  return { plan, observations }
}

test('business bindings require unique applicable operations and explicit job classifications', () => {
  const { plan } = check('source-membership')
  validateBusinessChecks(plan)
  for (const changes of [{ kind: 'other' }, { target: null }, { target: 'job:a' }, { target: 'document:' },
    { target: 'document:gone' }, { key: '' }, { test: '' }, { unrestrictedReason: '' }]) {
    assert.throws(() => validateBusinessChecks({ ...plan, businessChecks: [{ ...plan.businessChecks[0], ...changes }] }))
  }
  assert.throws(() => validateBusinessChecks({ ...plan, businessChecks: null }))
  assert.throws(() => validateBusinessChecks({ ...plan, businessChecks: [...plan.businessChecks, ...plan.businessChecks] }))
  for (const invalid of [true, [{ id: '', reason: 'single' }], [{ id: 'job', reason: '' }],
    [{ id: 'job', reason: 'one' }, { id: 'job', reason: 'two' }]]) {
    assert.throws(() => validateBusinessChecks({ ...plan, nonBatchJobs: invalid }))
  }
  const historical = check('historical-cutoff', {}, { unrestrictedReason: 'interchangeable' })
  assert.throws(() => validateBusinessChecks(historical.plan))
})

test('cutoff checks are bound to each report and registered measure including custom historical parameters', () => {
  const { plan } = check('historical-cutoff')
  const definition = { reportCode: 'example.position', parameters: [{ code: 'as_of_utc' }], dataset: { measures: [{ code: 'available' }] } }
  reconcileHistoricalChecks(plan, [definition, { reportCode: 'example.current' }])
  assert.throws(() => reconcileHistoricalChecks(plan, [{ ...definition, reportCode: 'second' }]), /report:second/)
  assert.throws(() => reconcileHistoricalChecks(plan, [{ ...definition, dataset: { measures: [{ code: 'remaining' }] } }]), /remaining/)
  assert.throws(() => reconcileHistoricalChecks({ ...plan, businessChecks: [], cases: [{ target: report, scenario: 'historical-as-of' }] },
    [{ reportCode: 'example.position' }]), /historical-cutoff/)
  reconcileHistoricalChecks(plan, [{ reportCode: 'example.position' }])
  reconcileHistoricalChecks({ ...plan, cases: [{ target: 'report:other', scenario: 'report' }] }, [{ reportCode: 'example.current' }])
  assert.throws(() => requireBusinessCheck(plan, 'historical-cutoff', report, 'unbound'))
})

test('passing test output alone contributes its own observations', () => {
  const xml = `<TestRun><Results><UnitTestResult outcome="Passed" testName="Example.Read"><Output><StdOut>
other output
  NGB-CHECK {"kind":"source-membership"}
</StdOut></Output></UnitTestResult><UnitTestResult outcome="Passed"/><UnitTestResult outcome="Failed">
<Output><StdOut>NGB-CHECK {"ignored":true}</StdOut></Output></UnitTestResult><Other/></Results>
<Output><StdOut>NGB-CHECK {"outside":true}</StdOut></Output></TestRun>`
  assert.deepEqual(integrationObservations(xml), [{ test: 'Example.Read', proof: { kind: 'source-membership' } }])
  assert.deepEqual(integrationObservations('<TestRun/>'), [])
  assert.throws(() => integrationObservations(xml.replace('{"kind":"source-membership"}', 'not json')))
})

test('each check rejects the observed defect and incomplete or unrelated evidence', () => {
  const invalid = {
    'effective-prerequisite': [{ attemptAt: '2026-04-02' }, { controlAt: '2026-04-01' }, { prerequisiteAt: 'invalid' },
      { controlStatus: 403 }, { refusalStatus: 200 }, { afterRefusal: {} }],
    'history-conflict': [{ newerIds: ['b'] }, { newerIds: ['a', 'b'] }, { newerIds: ['b', 'b'] },
      { pageLimit: 0 }, { reason: 'unrelated' }, { refusalStatus: 200 }],
    capacity: [{ populations: { items: 999 } }, { samples: [] },
      { samples: Array(2).fill({ status: 200, queries: 0, milliseconds: 1 }) },
      { samples: Array(2).fill({ status: 403, queries: 1, milliseconds: 1 }) },
      { samples: Array(2).fill({ status: 200, queries: 5, milliseconds: 1 }) },
      { samples: Array(2).fill({ status: 200, queries: 3, milliseconds: 101 }) }],
    'source-membership': [{ otherStatus: 200 }, { otherSource: 'a' }, { correctSource: '' }, { otherType: 'other' },
      { sourceType: '' }, { sourceState: null }, { otherState: 'Draft' }, { correctStatus: 403 }, { reason: '' },
      { reason: 'permission' }, { beforeRefusal: {} }, { afterRefusal: { movements: 3 } }],
    'withdrawal-cycle': [{ refusalStatus: 200 }, { laterUse: '' }, { laterRelease: 'one' }, { initialState: undefined },
      { restoredState: { available: 0 } }, { reason: '' }, { reason: 'version' }, { recoveryStatus: 409 }, { afterRefusal: [] }],
    'historical-cutoff': [{ after: 50 }, { before: 3 }, { cutoff: '' }, { repeatedCutoff: '2026-04-03' },
      { laterEffectiveAt: '2026-04-01' }, { laterEvent: '' }, { laterStatus: 400 }, { laterActual: 0 }],
    'worker-backlog': [{ delivered: ['a', 'b'] }, { delivered: ['a', 'a', 'c'] }, { afterReplay: ['a', 'b', 'c', 'a'] },
      { eligible: ['a', 'a', 'c'] }, { eligible: null }, { eligible: ['a', '', 'c'] }, { batchLimit: 3 },
      { batchLimit: 0 }, { batchLimit: '2' }, { runs: 1 }, { runs: null }, { delivered: null }],
  }
  for (const [kind, changes] of Object.entries(invalid)) {
    const good = check(kind)
    verifyBusinessObservations(good.plan, good.observations)
    for (const mutation of changes) {
      const { plan, observations } = check(kind, mutation)
      assert.throws(() => verifyBusinessObservations(plan, observations), JSON.stringify(mutation))
    }
  }
  for (const key of ['before', 'after', 'expected', 'laterActual', 'laterExpected']) {
    const { plan, observations } = check('historical-cutoff')
    delete observations[0].proof[key]
    assert.throws(() => verifyBusinessObservations(plan, observations))
  }
  const any = check('source-membership', { otherStatus: 201 }, { unrestrictedReason: 'Any posted source can supply this field.' })
  verifyBusinessObservations(any.plan, any.observations)
  any.observations[0].proof.otherStatus = 409
  assert.throws(() => verifyBusinessObservations(any.plan, any.observations))
  const { plan, observations } = check('withdrawal-cycle')
  assert.throws(() => verifyBusinessObservations(plan, []), /Missing integration evidence/)
  assert.throws(() => verifyBusinessObservations(plan, [...observations, ...observations]), /duplicate/)
  assert.throws(() => verifyBusinessObservations(plan, [{ ...observations[0], test: 'Other' }]), /incorrectly bound/)
})

test('worker inventory rejects missing jobs and allows explicitly single-operation jobs', () => {
  const { plan, observations } = check('worker-backlog')
  assert.throws(() => verifyBusinessObservations(plan, observations.slice(0, 1), true), /worker host/)
  const inventory = observations[1].proof
  inventory.jobs.push('example.refresh')
  assert.throws(() => verifyBusinessObservations(plan, observations, true), /every registered/)
  plan.nonBatchJobs = [{ id: 'example.refresh', reason: 'Refreshes one published summary.' }]
  verifyBusinessObservations(plan, observations, true)
  plan.nonBatchJobs[0].id = 'example.dispatch'
  assert.throws(() => verifyBusinessObservations(plan, observations), /Conflicting/)
  inventory.jobs = 'invalid'
  assert.throws(() => verifyBusinessObservations(plan, observations), /Invalid worker/)
})
