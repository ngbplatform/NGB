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
const dates = ['2026-03-31', '2026-04-01', '2026-04-02']
const withdrawn = { source: 'Withdrawn', consumer: 'Posted', balance: 10 }
const rows = Array.from({ length: 12 }, (_, index) => ({ id: index + 1, number: 'Ref-' + index }))
const lookup = { operation: 'available-sources', actor: 'operator', returnedRows: 1, queries: 4, status: 200,
  expectedRows: rows.slice(0, 1), actualRows: rows.slice(0, 1) }
const largePage = { ...lookup, returnedRows: rows.length, expectedRows: rows, actualRows: rows }
const growthBudget = { smallRows: 1, largeRows: 12, maxAdditionalQueries: 0 }

function check(kind, changes = {}, options = {}) {
  const samples = {
    'report-export': { target: report, key: 'totals', downloadStatus: 200,
      contentType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      expectedCells: [12], screenCells: [12], worksheetCells: [12] },
    'state-invariant': { target, key: 'terminal', operation: 'confirm', persistedControlId: 'open',
      controlStatus: 200, refusalStatus: 409, ...refusal },
    'dated-withdrawal': { target, key: 'authorization', sourceId: 'first', consumerId: 'second',
      sourceAt: '2026-04-01', consumerAt: '2026-04-02', replacementAt: '2026-04-03', persistedControlId: 'first',
      controlConsumerId: 'second', expectedControl: withdrawn, afterControl: withdrawn,
      controlStatus: 200, refusalStatus: 409, ...refusal,
      beforeRefusal: { source: 'Posted', consumer: 'Posted', balance: 10 }, afterRefusal: { source: 'Posted', consumer: 'Posted', balance: 10 } },
    'business-day-cutoff': { target: report, key: 'position', recordId: 'dated', businessDate: '2026-04-01',
      opening: 0, closing: 12, zones: ['America/New_York', 'Pacific/Kiritimati'].map(zone => ({ zone, configuredZone: zone, dates, values: [0, 12, 12] })) },
    'effective-prerequisite': { target, key: 'funding', persistedControlId: 'funded-control', attemptAt: '2026-04-01', prerequisiteAt: '2026-04-02',
      controlAt: '2026-04-02', controlStatus: 200, refusalStatus: 409, ...refusal },
    'interval-overlap': { target, key: 'reservation', boundary: 'exclusive-end', priorStart: '2026-04-01', priorEnd: '2026-04-05',
      attemptStart: '2026-04-02', attemptEnd: '2026-04-03', controlStart: '2026-04-05', controlEnd: '2026-04-06', resourceId: 'asset',
      closedRecordId: 'first', persistedControlId: 'next', controlStatus: 201, refusalStatus: 409, ...refusal },
    'snapshot-stability': { target, key: 'classification', capturedAt: '2026-04-01', changedAt: '2026-04-02',
      consumedAt: '2026-04-03', sourceId: 'original', consumerId: 'later', original: 'A', changed: 'B',
      expected: 5, observed: 5, freshExpected: 7, freshObserved: 7 },
    'restore-rehearsal': { target: 'navigation:example.operations', key: 'posted', sourceInstance: 'source',
      restoredInstance: 'restored', backupBytes: 100, backupSha256: 'a'.repeat(64), readStatus: 200,
      recordId: 'record', beforeBackup: { state: 'posted' }, afterRestore: { state: 'posted' } },
    'history-conflict': { target, key: 'schedule', pageLimit: 2, newerIds: ['b', 'c'], conflictId: 'a',
      controlStatus: 201, refusalStatus: 409, ...refusal },
    capacity: { target: report, key: 'position', populations: { items: 1100 },
      samples: [{ status: 200, milliseconds: 30, queries: 3 }, { status: 200, milliseconds: 45, queries: 4 }] },
    'query-growth': { target, key: lookup.operation, valueFields: ['number'], small: lookup, large: largePage },
    'source-membership': { target, key: 'source', sourceType: 'request', otherType: 'request', sourceState: 'Posted',
      otherState: 'Posted', correctSource: 'a', otherSource: 'b', otherStatus: 422, correctStatus: 200, ...refusal },
    'withdrawal-cycle': { target, key: 'capacity', prerequisite: 'one', laterUse: 'two', laterRelease: 'three',
      initialState: { available: 1 }, restoredState: { available: 1 }, refusalStatus: 409, recoveryStatus: 204, ...refusal },
    'historical-cutoff': { target: report, key: 'available', cutoff: '2026-04-02', repeatedCutoff: '2026-04-02',
      timeZone: 'UTC', cutoffExclusive: '2026-04-03T00:00:00Z', laterCutoff: '2026-04-03',
      laterEvent: 'three', laterEffectiveAt: '2026-04-03', laterStatus: 200, expected: 0, before: 0, after: 0,
      laterExpected: 5, laterActual: 5 },
    'worker-backlog': { target: job, key: 'notice', batchLimit: 2, runs: 2, eligible: ids, delivered: ids, afterReplay: ids },
  }
  const proof = { kind, ...samples[kind], ...changes }
  const entry = { kind, target: proof.target, key: proof.key, test: 'Example.Tests.ReadBack', ...options }
  if (kind === 'capacity') entry.budget = { populations: { items: 1000 }, samples: 2, p95Milliseconds: 100, maxQueries: 4 }
  if (kind === 'query-growth') entry.budget = growthBudget
  const plan = { businessChecks: [entry], surfaces: [target, report, 'navigation:example.operations'], cases: [] }
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
  assert.throws(() => reconcileHistoricalChecks(plan, [{ reportCode: 'example.position', capabilities: { allowsXlsxExport: true } }]), /report-export/)
})

test('passing test output alone contributes its own observations', () => {
  const xml = `<TestRun><Results><UnitTestResult outcome="Passed" testName="Example.Read"><Output><StdOut>
other output
  NGB-CHECK {"kind":"source-membership"}
</StdOut></Output></UnitTestResult><UnitTestResult outcome="Passed"/><UnitTestResult outcome="Failed">
<Output><StdOut>NGB-CHECK {"ignored":true}</StdOut></Output></UnitTestResult><Other/></Results>
<Output><StdOut>NGB-CHECK {"outside":true}</StdOut></Output></TestRun>`
  assert.deepEqual(integrationObservations(xml), [{ test: 'Example.Read', proof: { kind: 'source-membership' } }])
  const passed = new Set()
  integrationObservations(xml, passed)
  assert.deepEqual([...passed], ['Example.Read'])
  assert.deepEqual(integrationObservations('<TestRun/>'), [])
  assert.throws(() => integrationObservations(xml.replace('{"kind":"source-membership"}', 'not json')))
})

test('each check rejects the observed defect and incomplete or unrelated evidence', () => {
  const invalid = {
    'report-export': [{ downloadStatus: 403 }, { contentType: 'text/plain' }, { expectedCells: [] },
      { screenCells: [13] }, { worksheetCells: [0] },
      { expectedCells: [null, ' ', {}], screenCells: [null, ' ', {}], worksheetCells: [null, ' ', {}] }],
    'state-invariant': [{ operation: '' }, { persistedControlId: '' }, { refusalStatus: 200 }],
    'dated-withdrawal': [{ sourceId: 'second' }, { consumerId: '' }, { replacementAt: '2026-04-01' }, { sourceAt: '2026-04-03' },
      { beforeRefusal, afterRefusal: beforeRefusal }, { persistedControlId: '' }, { controlConsumerId: 'another' },
      { afterControl: undefined }, { afterControl: { ...withdrawn, source: 'Posted' } }, { afterControl: { ...withdrawn, balance: 0 } },
      { afterControl: { ...withdrawn, balance: 'absent' } }, { afterControl: { ...withdrawn, balance: null } }],
    'business-day-cutoff': [{ businessDate: '1 Apr' }, { businessDate: '2026-02-31' }, { opening: null }, { closing: null }, { recordId: '' }, { zones: [] }, { closing: 0 },
      { zones: ['UTC', 'Europe/Paris'].map(zone => ({ zone, configuredZone: zone, dates, values: [0, 12, 12] })) },
      { zones: Array(2).fill({ zone: 'UTC', configuredZone: 'Europe/Paris', values: [0, 12, 12] }) },
      { zones: Array(2).fill({ zone: 'Pacific/Kiritimati', configuredZone: 'Pacific/Kiritimati', dates, values: [0, 0, 12] }) }],
    'effective-prerequisite': [{ attemptAt: '2026-04-02' }, { controlAt: '2026-04-01' }, { prerequisiteAt: 'invalid' },
      { controlStatus: 403 }, { refusalStatus: 200 }, { afterRefusal: {} }],
    'interval-overlap': [{ priorEnd: null }, { attemptStart: '2026-04-08' }, { closedRecordId: '' },
      { controlStart: '2026-04-03' }, { persistedControlId: 'first' }, { afterRefusal: {} }],
    'snapshot-stability': [{ observed: 7 }, { consumedAt: '2026-04-02' }, { changed: 'A' },
      { freshExpected: 5 }, { freshObserved: 5 }, { consumerId: '' }],
    'restore-rehearsal': [{ restoredInstance: 'source' }, { backupBytes: 0 }, { backupSha256: 'x' }, { backupSha256: null },
      { readStatus: 404 }, { afterRestore: { state: 'draft' } }],
    'history-conflict': [{ newerIds: ['b'] }, { newerIds: ['a', 'b'] }, { newerIds: ['b', 'b'] },
      { pageLimit: 0 }, { reason: 'unrelated' }, { refusalStatus: 200 }],
    capacity: [{ populations: { items: 999 } }, { samples: [] },
      { samples: Array(2).fill({ status: 200, queries: 0, milliseconds: 1 }) },
      { samples: Array(2).fill({ status: 403, queries: 1, milliseconds: 1 }) },
      { samples: Array(2).fill({ status: 200, queries: 5, milliseconds: 1 }) },
      { samples: Array(2).fill({ status: 200, queries: 3, milliseconds: 101 }) }],
    'query-growth': [{ small: null }, { small: { ...lookup, status: 403 } }, { small: { ...lookup, queries: 0 } },
      { small: { ...lookup, returnedRows: 0 } }, { small: { ...lookup, queries: 1.5 } },
      { small: { ...lookup, actor: ' ' } }, { small: { ...lookup, operation: 'another' } },
      { large: { ...largePage, actor: 'administrator' } }, { large: { ...largePage, queries: 15 } }],
    'source-membership': [{ otherStatus: 200 }, { otherSource: 'a' }, { correctSource: '' }, { otherType: 'other' },
      { sourceType: '' }, { sourceState: null }, { otherState: 'Draft' }, { correctStatus: 403 }, { reason: '' },
      { reason: 'permission' }, { beforeRefusal: {} }, { afterRefusal: { movements: 3 } }],
    'withdrawal-cycle': [{ refusalStatus: 200 }, { laterUse: '' }, { laterRelease: 'one' }, { initialState: undefined },
      { restoredState: { available: 0 } }, { reason: '' }, { reason: 'version' }, { recoveryStatus: 409 }, { afterRefusal: [] }],
    'historical-cutoff': [{ after: 50 }, { before: 3 }, { cutoff: '' }, { repeatedCutoff: '2026-04-03' },
      { laterEffectiveAt: '2026-04-01' }, { laterEvent: '' }, { laterStatus: 400 }, { laterActual: 0 },
      { laterActual: 0, laterExpected: 0 }],
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

test('zero balances, false cells and invariant totals with changing details remain meaningful', () => {
  const cells = [0, false, ' ', null]
  const before = { total: 0, count: 0 }
  const after = { total: 0, count: 2 }
  const control = { source: false, consumer: true, balance: '0.00' }
  for (const { plan, observations } of [
    check('report-export', { expectedCells: cells, screenCells: cells, worksheetCells: cells }),
    check('report-export', { expectedCells: [false], screenCells: [false], worksheetCells: [false] }),
    check('historical-cutoff', { expected: before, before, after: before, laterExpected: after, laterActual: after }),
    check('dated-withdrawal', { afterControl: control, expectedControl: control }),
  ]) verifyBusinessObservations(plan, observations)
})

test('calendar-day evidence respects the configured midnight and the actual later report period', () => {
  for (const [zone, cutoff, nextDay, boundary, lastIncluded] of [
    ['UTC', '2026-05-01', '2026-05-02', '2026-05-02T00:00:00Z', '2026-05-01T23:59:59.999999Z'],
    ['Pacific/Kiritimati', '2026-05-01', '2026-05-02', '2026-05-01T10:00:00Z', '2026-05-01T09:59:59.999999Z'],
    ['America/New_York', '2026-03-08', '2026-03-09', '2026-03-09T04:00:00Z', '2026-03-09T03:59:59.999999Z'],
    ['America/New_York', '2026-11-01', '2026-11-02', '2026-11-02T05:00:00Z', '2026-11-02T04:59:59.999999Z'],
  ]) {
    const sample = { timeZone: zone, cutoff, repeatedCutoff: cutoff, laterCutoff: nextDay, cutoffExclusive: boundary,
      laterEffectiveAt: boundary }
    const verify = changes => {
      const result = check('historical-cutoff', { ...sample, ...changes })
      return verifyBusinessObservations(result.plan, result.observations)
    }
    verify({})
    verify({ laterEffectiveAt: nextDay })
    for (const changes of [{ laterEffectiveAt: cutoff }, { laterEffectiveAt: lastIncluded },
      { laterEffectiveAt: '2027-01-01T00:00:00Z' }, { laterCutoff: cutoff }, { laterCutoff: undefined },
      { cutoffExclusive: undefined }, { cutoffExclusive: lastIncluded }, { timeZone: undefined },
      { cutoffExclusive: nextDay + 'T22:00:00Z' }, { cutoffExclusive: boundary.replace('Z', '.000001Z') },
      { timeZone: 'Invalid/Zone' }, { laterEffectiveAt: '2026-02-30' }, { laterEffectiveAt: '2026-00-00' }]) {
      assert.throws(() => verify(changes))
    }
  }
})

test('instant cutoffs use explicit offsets and preserve database timestamp precision', () => {
  const cutoff = '2026-05-01T12:00:00.123456Z'
  const sample = { timeZone: null, cutoff, repeatedCutoff: cutoff, laterEffectiveAt: '2026-05-01T08:00:00.123457-04:00',
    laterCutoff: '2026-05-01T12:00:00.123457Z' }
  const verify = changes => {
    const result = check('historical-cutoff', { ...sample, ...changes })
    return verifyBusinessObservations(result.plan, result.observations)
  }
  verify({})
  for (const changes of [{ laterEffectiveAt: cutoff }, { laterCutoff: cutoff }, { laterEffectiveAt: '2026-05-02' },
    { laterCutoff: '2026-05-01T15:00:00' }]) assert.throws(() => verify(changes))
})

test('growth evidence reconciles projected fields and distinct seeded identities for each measured page', () => {
  const verify = changes => {
    const result = check('query-growth', changes)
    return verifyBusinessObservations(result.plan, result.observations)
  }
  const row = { id: 'first', number: 'A1' }
  verify({ small: { ...lookup, expectedRows: [row], actualRows: [row] } })
  for (const valueFields of [undefined, [], ['id'], [''], ['number', 'number']]) assert.throws(() => verify({ valueFields }))
  for (const small of [{ ...lookup, expectedRows: undefined }, { ...lookup, expectedRows: [] },
    { ...lookup, expectedRows: [{ id: 'first' }] }, { ...lookup, expectedRows: [{ id: null, number: 'A' }] },
    { ...lookup, actualRows: [{ ...rows[0], number: 'wrong' }] }]) {
    assert.throws(() => verify({ small }))
  }
  assert.throws(() => verify({ large: { ...largePage, expectedRows: Array(12).fill(row) } }), /distinct expected/)
})

test('query growth budgets use two distinct row counts and detect a command per additional row', () => {
  const { plan } = check('query-growth')
  validateBusinessChecks(plan)
  for (const changes of [{ smallRows: 0 }, { smallRows: 1.5 }, { largeRows: 1 }, { largeRows: 2.5 },
    { maxAdditionalQueries: -1 }, { maxAdditionalQueries: 0.5 }, { maxAdditionalQueries: 11 }]) {
    plan.businessChecks[0].budget = { ...growthBudget, ...changes }
    assert.throws(() => validateBusinessChecks(plan))
  }
})

test('a recovery claim includes fresh login and downloaded bytes for every declared store', () => {
  const { plan, observations } = check('restore-rehearsal', {}, { stores: ['database', 'identity', 'attachments'] })
  const proof = observations[0].proof
  assert.throws(() => verifyBusinessObservations(plan, observations), /restored identity/)
  proof.restoredLogin = { status: 200, subject: 'restored-user', originalSubject: 'restored-user' }
  assert.throws(() => verifyBusinessObservations(plan, observations), /restored attachment/)
  proof.restoredAttachment = { status: 200, id: 'file', bytes: 12, sha256: 'a'.repeat(64), originalSha256: 'b'.repeat(64) }
  assert.throws(() => verifyBusinessObservations(plan, observations), /downloaded contents/)
  proof.restoredAttachment.sha256 = proof.restoredAttachment.originalSha256
  verifyBusinessObservations(plan, observations)
  for (const stores of [[], ['database', 'unknown'], ['database', 'database']]) {
    plan.businessChecks[0].stores = stores
    assert.throws(() => validateBusinessChecks(plan))
  }
  const dated = check('dated-withdrawal')
  delete dated.observations[0].proof.replacementAt
  verifyBusinessObservations(dated.plan, dated.observations)
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

test('a declared process-memory budget requires measured bytes for every workload sample', () => {
  const { plan, observations } = check('capacity')
  const budget = plan.businessChecks[0].budget
  budget.maxWorkingSetBytes = 1000
  assert.throws(() => verifyBusinessObservations(plan, observations), /working set/)
  const { samples } = observations[0].proof
  for (const sample of samples) sample.workingSetBytes = 512
  verifyBusinessObservations(plan, observations)
  samples[0].workingSetBytes = 1001
  assert.throws(() => verifyBusinessObservations(plan, observations), /working set/)
})

test('inclusive day bookings and half-open timestamps honor their different endpoint rules', () => {
  const day = { boundary: 'inclusive', priorEnd: '2026-04-01', attemptStart: '2026-04-01',
    attemptEnd: '2026-04-01', controlStart: '2026-04-02', controlEnd: '2026-04-02' }
  const { plan, observations } = check('interval-overlap', day)
  verifyBusinessObservations(plan, observations)
  observations[0].proof.controlStart = '2026-04-01'
  assert.throws(() => verifyBusinessObservations(plan, observations), /nonoverlapping/)
  const touching = check('interval-overlap', { attemptStart: '2026-04-05', attemptEnd: '2026-04-08' })
  assert.throws(() => verifyBusinessObservations(touching.plan, touching.observations), /overlap a completed interval/)
})
