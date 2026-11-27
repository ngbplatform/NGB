import assert from 'node:assert/strict'
import test from 'node:test'
import { verifyBusinessObservations } from '../../packaging/templates/content/infrastructure/ngb/business-checks.mjs'
import { verifyOperationReview, verifyRuntimeOperationReview } from '../../packaging/templates/content/infrastructure/ngb/operation-review.mjs'

const receipt = 'document:example.receipt'
const usage = 'document:example.usage'
const asset = 'catalog:example.asset'
const report = 'report:example.availability'
const operation = 'post'
const dates = ['2026-05-02', '2026-05-03', '2026-05-05']
const sourceState = { source: 'Posted', consumer: 'Posted', balance: 0 }
const corrected = { source: 'Draft', consumer: 'Draft', balance: 0 }

function observe(kind, target, values) {
  const entry = { key: 'fixture', kind, target, test: 'Example.OperationTests.Check' }
  const plan = { surfaces: [target], businessChecks: [entry] }
  verifyBusinessObservations(plan, [{ test: entry.test, proof: { ...entry, ...values } }])
}

test('backdating leaves every affected closing intact and succeeds against the earlier funding', () => {
  const proof = { source: receipt, ownerId: 'owner', controlOwnerId: 'control', attemptId: 'attempt', reopenedAttemptId: 'attempt',
    attemptDate: dates[0], controlDate: dates[0], dates, change: -20, controlChange: -20,
    opening: [20, 0, 20], funded: [40, 20, 20], before: [20, 0, 20], after: [20, 0, 20],
    status: 409, reason: 'negative', expectedReason: 'negative', controlStatus: 200, controlId: 'ok', reopenedId: 'ok',
    controlBefore: [40, 20, 20], controlAfter: [20, 0, 0] }
  const check = changes => observe('intermediate-balance', usage, { ...proof, ...changes })
  check({})
  for (const invalid of [{ source: '' }, { dates: [dates[1], dates[0], dates[2]] }, { opening: [20, 20, 20] },
    { funded: [40, 20, 40] }, { after: [0, -20, 0] }, { status: 200 }, { controlAfter: [20, 0, 20] }]) {
    assert.throws(() => check(invalid))
  }
})

test('a correction examines the same dependency order at the boundary and beyond it', () => {
  const sample = dependentIds => ({ operation: 'unpost', sourceId: 'source', reopenedId: 'source', dependentIds,
    decisiveId: dependentIds.at(-1), reopenedDependentId: dependentIds.at(-1), status: 409, reason: 'dependent',
    before: sourceState, after: sourceState, resolvedId: dependentIds.at(-1), unchangedPrefixIds: dependentIds.slice(0, -1),
    controlStatus: 200, controlSourceId: 'source', afterControl: corrected })
  const samples = [sample(['first', 'second']), sample(['first', 'second', 'third'])]
  const proof = { consumer: usage, operation: 'unpost', pageSize: 2, expectedReason: 'dependent', samples,
    expectedBefore: [sourceState, sourceState], expectedAfterControl: [corrected, corrected] }
  const check = changes => observe('dependency-window', receipt, { ...proof, samples: [samples[0], { ...samples[1], ...changes }] })
  check({})
  for (const invalid of [{ decisiveId: 'second' }, { dependentIds: ['first', 'first', 'third'] },
    { status: 200 }, { reopenedId: 'other' }, { operation: 'edit' }, { after: corrected }, { afterControl: sourceState },
    { unchangedPrefixIds: ['first'] }, { resolvedId: 'first' }]) {
    assert.throws(() => check(invalid))
  }
})

test('terminal status survives ordinary edits and subsequent use while the original reporting cutoff remains stable', () => {
  const proof = { state: 'Retired', consumer: usage, id: 'old', reopenedId: 'old', reverseId: 'old',
    terminalStatus: 200, persistedState: 'Retired', reverseStatus: 409, reverseReason: 'terminal', reverseError: 'terminal',
    useStatus: 422, useError: 'unavailable', useReason: 'unavailable', afterReverse: 'Retired', afterUse: 'Retired',
    attemptedUseId: 'old', activeId: 'sibling', controlUseId: 'sibling', controlStatus: 200,
    cutoff: dates[0], terminalAt: dates[1], laterCutoff: dates[2], historicalState: 'Busy', laterState: 'Retired', report }
  const check = (changes, times = dates) => {
    const [cutoff, terminalAt, laterCutoff] = times
    const read = (cutoff, state) => ({ status: 200, id: 'old', report, cutoff, state })
    observe('terminal-state', asset, { ...proof, cutoff, terminalAt, laterCutoff,
      reads: [read(cutoff, 'Busy'), read(cutoff, 'Busy'), read(laterCutoff, 'Retired')], ...changes })
  }
  check({})
  check({}, dates.map(day => day + 'T00:00:00Z'))
  check({ report: null })
  for (const invalid of [{ reverseStatus: 200 }, { useStatus: 200 }, { activeId: 'old' }, { terminalAt: dates[0] },
    { cutoff: dates[1] }, { reads: [] }, { historicalState: 'Retired' }, { laterState: 'Busy' }]) {
    assert.throws(() => check(invalid))
  }
})

test('each operation supplies its own reason and writes the same value to the record and its audit entry', () => {
  const attempts = [null, '', '  '].map(input => ({ input, operation, id: 'draft', reopenedId: 'draft', status: 422,
    reason: 'missing-reason', before: { state: 'Draft', events: 0 }, after: { state: 'Draft', events: 0 } }))
  const proof = { operation, suppliedReason: 'Verified discrepancy', expectedReason: 'missing-reason', attempts,
    recordId: 'a', reopenedId: 'a', persistedReason: 'Verified discrepancy', actorId: 'actor', controlStatus: 201,
    audit: { recordId: 'a', operation, actorId: 'actor', reason: 'Verified discrepancy', at: '2026-05-05T10:12:30Z' } }
  const check = changes => observe('required-reason', usage, { ...proof, ...changes })
  check({})
  const tab = { ...attempts[2], input: '\t' }
  check({ attempts: [attempts[1], tab, attempts[0]] })
  check({ attempts: [tab, ...attempts, { ...tab, input: ' \n\t' }] })
  for (const input of [null, '', '  ']) {
    assert.throws(() => check({ attempts: attempts.filter(attempt => attempt.input !== input) }), /whitespace-only/)
  }
  for (const input of ['not blank', false, undefined]) {
    assert.throws(() => check({ attempts: [...attempts, { ...tab, input }] }))
  }
  assert.throws(() => check({ attempts: [...attempts, { ...tab, after: { state: 'Posted', events: 1 } }] }))
  observe('required-reason', 'navigation:periods', proof)
  const creations = attempts.map(attempt => ({ ...attempt, id: null, requestKey: 'unique', reopenedKey: 'unique',
    before: { matchingRecords: 0 }, after: { matchingRecords: 0 } }))
  check({ attempts: creations })
  assert.throws(() => check({ attempts: [{ ...creations[0], before: { matchingRecords: 1 } }, ...creations.slice(1)] }))
  for (const invalid of [{ attempts: attempts.slice(1) }, { attempts: [{ ...attempts[0], status: 200 }, ...attempts.slice(1)] },
    { operation: 'different' }, { persistedReason: '' }, { audit: { ...proof.audit, recordId: 'other' } },
    { audit: { ...proof.audit, reason: 'default' } }]) {
    assert.throws(() => check(invalid))
  }
})

test('the reviewed write policies bind each source, correction, terminal catalog and reason-bearing operation', () => {
  const scope = { source: receipt, owner: 'party', effectiveAt: 'date' }
  const terminal = { state: 'Retired', consumer: usage, report }
  const fact = { kind: 'consumes', key: 'credit', ...scope, position: 'bounded', editableOwner: false, withdrawable: true,
    replaceable: true, corrections: [{ operation: 'unpost', cardinality: 'multiple' }, { operation: 'edit', cardinality: 'single' }] }
  const rows = [{ target: usage, operation, requirement: 'control', facts: [fact] },
    { target: asset, operation: 'retire', requirement: 'control', facts: [], reason: 'Defines the terminal transition.',
      terminalStates: [{ key: 'retire', ...terminal }] }]
  const proof = (kind, target, key, expect) => ({ kind, target, key, expect })
  const proofs = [
    proof('effective-prerequisite', usage, 'credit', scope), proof('intermediate-balance', usage, 'credit', { source: receipt }),
    proof('dated-withdrawal', receipt, 'credit', { consumer: usage, replaceable: true }),
    proof('dependency-window', receipt, 'credit/unpost', { consumer: usage, operation: 'unpost' }),
    proof('terminal-state', asset, 'retire', terminal), proof('required-reason', receipt, 'receipt-reason', { operation }),
    proof('required-reason', usage, 'usage-reason', { operation }),
  ]
  const rule = { id: 'control', effect: 'write', quote: 'Record a reason for adjustments.',
    reasonOperations: [{ term: 'adjustments', operation, targets: [receipt, usage] }],
    reasonExclusions: [{ reason: 'The catalog describes the resource.', targets: [asset] }], proofs }
  const inventory = { catalogs: [{ catalogType: 'example.asset' }], documents: [{ documentType: 'example.receipt' }, { documentType: 'example.usage' }] }
  const plan = { surfaces: [asset, report, receipt, usage], cases: [] }
  const planned = (entries = rows, obligation = rule) => verifyOperationReview(entries, [obligation])
  const live = (entries = rows, obligation = rule) => verifyRuntimeOperationReview(entries, [obligation], plan, inventory)
  planned()
  live()
  const legacyReason = { kind: 'state-invariant', key: 'reason-required', target: usage }
  live(rows, { ...rule, proofs: [...proofs, legacyReason] })
  assert.throws(() => live(rows, { ...rule, proofs: [legacyReason], reasonOperations: [] }), /declared reason requirement/)
  assert.throws(() => planned(rows, { ...rule, reasonOperations: [], reasonExclusions: undefined }), /independently/)
  const historical = { reportCode: 'example.availability', parameters: [{ code: 'as_of_utc' }],
    dataset: { fields: [{ lookup: { kind: 'catalog', catalogType: 'example.asset' } }] } }
  function withReport(state, records = proofs) {
    const review = [rows[0], { ...rows[1], terminalStates: [state] }]
    verifyRuntimeOperationReview(review, [{ ...rule, proofs: records }], plan, { ...inventory, reports: [historical] })
  }
  withReport(terminal)
  const related = { ...historical, dataset: { fields: [{ lookup: { kind: 'document', documentTypes: ['example.usage'] } }] } }
  verifyRuntimeOperationReview(rows, [rule], plan, { ...inventory, reports: [related, { reportCode: 'overview' }] })
  const noReport = proofs.filter(entry => entry.kind !== 'terminal-state')
  assert.throws(() => withReport({ ...terminal, report: null }, noReport), /historical cutoff/)
  const contextual = { ...terminal, report: null, unaffectedReports: [{ report, reason: 'Only groups by the resource.' }] }
  withReport(contextual, noReport)
  planned([rows[0], { ...rows[1], terminalStates: [{ key: 'retire', ...contextual }] }],
    { ...rule, proofs: [...noReport, proof('terminal-state', asset, 'retire', { ...terminal, report: null })] })
  for (const kind of ['intermediate-balance', 'dependency-window', 'required-reason', 'terminal-state']) {
    assert.throws(() => planned(rows, { ...rule, proofs: proofs.filter(entry => entry.kind !== kind) }))
  }
  assert.throws(() => planned(rows, { ...rule, proofs: proofs.slice(0, -1) }), /reason for document:example.usage/)
  assert.throws(() => planned(rows, { ...rule, reasonOperations: undefined }), /scope exclusions/)
  assert.throws(() => planned(rows, { ...rule, reasonExclusions: [{ reason: 'Other', targets: [usage] }] }), /Conflicting/)
  assert.throws(() => live(rows, { ...rule, reasonOperations: undefined }), /business-reason scope/)
  assert.throws(() => live(rows, { ...rule, reasonExclusions: undefined }), /Classify catalog/)
  assert.throws(() => live(rows, { ...rule, reasonExclusions: [{ reason: 'Other', targets: ['catalog:nonexistent'] }] }), /Unknown business-reason/)
  assert.throws(() => live([rows[0]]), /terminal states/)
  assert.throws(() => live([{ ...rows[0], facts: [{ ...fact, position: undefined, corrections: undefined }] }, rows[1]]),
    error => error.message.includes('intermediate dated position') && error.message.includes('dependency window'))
  assert.throws(() => live([rows[0], { ...rows[1], terminalStates: [{ ...terminal, report: 'report:unknown' }] }]), /registered consumer/)
})
