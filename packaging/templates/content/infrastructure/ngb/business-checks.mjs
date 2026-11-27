import assert from 'node:assert/strict'
import { objectKeys } from './kit-contracts.mjs'
import { readTestXml } from './project-data.mjs'

const kinds = {
  'intermediate-balance': 'document',
  'dependency-window': 'document',
  'terminal-state': null,
  'required-reason': null,
  'source-membership': 'document',
  'prerequisite-scope': 'document',
  'classification-context': 'report',
  'owner-change': 'document',
  'boundary-withdrawal': 'document',
  'competing-consumers': 'document',
  'paged-total': null,
  'withdrawal-cycle': 'document',
  'historical-cutoff': 'report',
  'effective-prerequisite': 'document',
  'dated-withdrawal': 'document',
  'state-invariant': null,
  'business-day-cutoff': 'report',
  'report-export': 'report',
  'history-conflict': 'document',
  'interval-overlap': 'document',
  'snapshot-stability': 'document',
  'restore-rehearsal': 'navigation',
  capacity: null,
  'query-growth': null,
  'worker-backlog': 'job',
}

const identity = entry => JSON.stringify([entry.kind, entry.target, entry.key])

function nonempty(value) {
  return typeof value === 'string' && value.trim().length > 0
}

export function validateBusinessTarget(kind, target) {
  assert.ok(Object.hasOwn(kinds, kind), 'Unknown server acceptance check: ' + kind)
  const [type, ...code] = String(target).split(':')
  const allowedTypes = ['capacity', 'query-growth', 'paged-total'].includes(kind) ? ['document', 'catalog', 'report', 'navigation']
    : kind === 'required-reason' ? ['document', 'catalog', 'navigation']
    : ['state-invariant', 'terminal-state'].includes(kind) ? ['document', 'catalog'] : [kinds[kind]]
  assert.ok(allowedTypes.includes(type) && nonempty(code.join(':')), 'Invalid server acceptance target.')
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
    objectKeys(entry, ['kind', 'target', 'key', 'test'], ['unrestrictedReason', 'budget', 'stores'])
    validateBusinessTarget(entry.kind, entry.target)
    if (entry.kind !== 'worker-backlog') assert.ok(plan.surfaces.includes(entry.target), 'Unknown server acceptance surface.')
    assert.ok(nonempty(entry.key) && nonempty(entry.test), 'Bind each server check to a business key and integration test.')
    if (entry.kind === 'capacity') {
      objectKeys(entry.budget, ['populations', 'samples', 'p95Milliseconds', 'maxQueries'], ['maxWorkingSetBytes'])
      assert.ok(entry.budget.populations && Object.keys(entry.budget.populations).length > 0
        && Object.entries(entry.budget.populations).every(([key, size]) => nonempty(key) && Number.isSafeInteger(size) && size > 0),
      'Declare minimum persisted populations for the business workload.')
      for (const key of Object.keys(entry.budget).filter(key => key !== 'populations')) {
        assert.ok(Number.isSafeInteger(entry.budget[key]) && entry.budget[key] > 0, 'Declare positive workload budgets.')
      }
    } else if (entry.kind === 'query-growth') {
      validateGrowthBudget(entry.budget)
    } else assert.ok(entry.budget === undefined, 'Workload budgets apply only to capacity and query-growth checks.')
    if (entry.stores !== undefined) {
      assert.ok(entry.kind === 'restore-rehearsal' && Array.isArray(entry.stores) && entry.stores.includes('database')
        && entry.stores.every(store => ['database', 'identity', 'attachments'].includes(store))
        && new Set(entry.stores).size === entry.stores.length, 'Declare the distinct stores in the restore rehearsal.')
    }
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

export function isHistoricalReport(plan, report) {
  const target = 'report:' + report.reportCode
  return report.parameters?.some(parameter => /^asof(?:utc|date|time|at)?$/i.test(parameter.code.replaceAll('_', '')))
    || plan.businessChecks?.some(entry => entry.target === target && entry.kind === 'historical-cutoff')
    || plan.cases.some(entry => entry.target === target && entry.scenario === 'historical-as-of')
}

export function reconcileHistoricalChecks(plan, reports) {
  for (const report of reports) {
    const target = `report:${report.reportCode}`
    if (report.capabilities?.allowsXlsxExport) requireBusinessCheck(plan, 'report-export', target)
    if (!isHistoricalReport(plan, report)) continue
    requireBusinessCheck(plan, 'historical-cutoff', target)
    for (const measure of report.dataset?.measures ?? []) requireBusinessCheck(plan, 'historical-cutoff', target, measure.code)
  }
}

// xUnit writes the integration test's own output inside its UnitTestResult. Global
// run output, unrelated passing tests and stale files cannot satisfy a binding.
export function integrationObservations(xml, passedTests, prefix = 'NGB-CHECK ') {
  const root = readTestXml(xml)
  const observations = []
  for (const result of root.children.find(node => node.name === 'Results')?.children ?? []) {
    if (result.name !== 'UnitTestResult' || result.attributes.outcome !== 'Passed') continue
    if (result.attributes.testName) passedTests?.add(result.attributes.testName)
    const output = result.children.find(node => node.name === 'Output')
    const stdout = output?.children.find(node => node.name === 'StdOut')?.text ?? ''
    for (const line of stdout.split(/\r?\n/)) {
      if (!line.trimStart().startsWith(prefix)) continue
      observations.push({ test: result.attributes.testName, proof: JSON.parse(line.trimStart().slice(prefix.length)) })
    }
  }
  return observations
}

function successful(status) {
  return Number.isInteger(status) && status >= 200 && status < 300
}

export function validateGrowthBudget(budget) {
  objectKeys(budget, ['smallRows', 'largeRows', 'maxAdditionalQueries'])
  const { smallRows, largeRows, maxAdditionalQueries } = budget
  assert.ok(Number.isSafeInteger(smallRows) && smallRows > 0 && Number.isSafeInteger(largeRows) && largeRows > smallRows,
    'Declare two increasing, positive numbers of returned rows.')
  assert.ok(Number.isSafeInteger(maxAdditionalQueries) && maxAdditionalQueries >= 0 && maxAdditionalQueries < largeRows - smallRows,
    'The growth budget must detect one additional database command per added row.')
}

export function validatePageTotals(expected) {
  assert.ok(Number.isSafeInteger(expected.pageLimit) && expected.pageLimit > 0
    && Number.isSafeInteger(expected.pageLimit + 1), 'Use the effective history page limit.')
  assert.ok(nonempty(expected.operation) && Array.isArray(expected.expectedTotals)
    && expected.expectedTotals.length === 3 && expected.expectedTotals.every(Number.isFinite)
    && expected.expectedTotals[1] !== expected.expectedTotals[2],
  'Freeze numeric totals before, at and beyond the page boundary; the last entry must change the total.')
}

function verifyPagedTotal(proof) {
  validatePageTotals(proof)
  assert.ok(nonempty(proof.recordId) && Array.isArray(proof.samples) && proof.samples.length === 3,
    'Read the same persisted record before, at and beyond the page limit.')
  for (const [index, sample] of proof.samples.entries()) {
    const count = proof.pageLimit + index - 1
    assert.ok(sample.count === count && sample.recordId === proof.recordId && successful(sample.status)
      && successful(sample.comparisonStatus), 'Read both the business card and its independent reconciliation at each boundary.')
    assert.equal(sample.total, proof.expectedTotals[index], 'Incorrect full-history total.')
    assert.equal(sample.comparisonTotal, proof.expectedTotals[index], 'The independent total differs.')
  }
  const { pageIds, continuationIds, lastId } = proof
  assert.ok(Array.isArray(pageIds) && pageIds.length === proof.pageLimit && pageIds.every(nonempty)
    && Array.isArray(continuationIds) && continuationIds.length === 1 && continuationIds.every(nonempty) && nonempty(lastId)
    && [...pageIds, ...continuationIds].includes(lastId) && new Set([...pageIds, ...continuationIds]).size === proof.pageLimit + 1,
  'Follow the next page and find the last confirmed entry exactly once.')
}

export function validateValueFields(fields) {
  assert.ok(Array.isArray(fields) && fields.length > 0 && fields.every(field => nonempty(field) && field !== 'id')
    && new Set(fields).size === fields.length, 'Declare the distinct business fields to compare for every returned identity.')
}

function checkPageValues(sample, fields) {
  assert.ok(Array.isArray(sample.expectedRows) && sample.expectedRows.length === sample.returnedRows,
    'Provide independently seeded identities and field values for the entire requested page.')
  for (const row of sample.expectedRows) {
    objectKeys(row, ['id', ...fields])
    assert.ok(nonempty(row.id) || Number.isSafeInteger(row.id), 'Identify each expected row.')
  }
  assert.equal(new Set(sample.expectedRows.map(row => row.id)).size, sample.returnedRows, 'Use distinct expected row identities.')
  assert.deepEqual(sample.actualRows, sample.expectedRows, 'The ordered row identities and business values differ from the fixture.')
}

function populated(value) {
  return Number.isFinite(value) || typeof value === 'boolean' || nonempty(value)
}

export function validateWithdrawalState(snapshot) {
  for (const field of ['source', 'consumer']) {
    assert.ok(populated(snapshot?.[field]), 'Read the persisted lifecycle state of both source and consumer.')
  }
  assert.ok(Number.isFinite(snapshot.balance) || (nonempty(snapshot.balance) && /^-?\d+(?:\.\d+)?$/.test(snapshot.balance)),
    'Read the numeric balance at the dependent operation date.')
}

export function validateExportCells(cells) {
  assert.ok(Array.isArray(cells) && cells.some(populated), 'Reconcile at least one populated business cell in the exported worksheet.')
}

const calendarDate = /^\d{4}-\d{2}-\d{2}$/
const offsetTimestamp = /^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.(\d{1,9}))?(?:Z|[+-](?:[01]\d|2[0-3]):[0-5]\d)$/

function checkedDate(value) {
  assert.ok(calendarDate.test(value) && Number.isFinite(Date.parse(value))
    && new Date(value).toISOString().slice(0, 10) === value, 'Use a valid ISO calendar date.')
  return value
}

function instant(value) {
  const match = offsetTimestamp.exec(value)
  assert.ok(match, 'Use an ISO timestamp with an explicit UTC offset.')
  checkedDate(value.slice(0, 10))
  // Date.parse retains milliseconds. Keep the sub-millisecond part of database timestamps.
  const fraction = (match[1] ?? '').padEnd(9, '0')
  return BigInt(Date.parse(value)) * 1000000n + BigInt(fraction.slice(3))
}

export function businessDateFormat(timeZone) {
  assert.ok(nonempty(timeZone), 'Identify the configured business time zone for a date-based report.')
  return new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' })
}

function verifyCutoffPeriod(proof) {
  if (proof.timeZone === null) {
    const event = instant(proof.laterEffectiveAt)
    assert.ok(instant(proof.cutoff) < event && event <= instant(proof.laterCutoff),
      'The later event must fall between the two requested instants.')
    return
  }

  const format = businessDateFormat(proof.timeZone)
  const cutoff = checkedDate(proof.cutoff)
  const laterCutoff = checkedDate(proof.laterCutoff)
  const boundary = instant(proof.cutoffExclusive)
  const milliseconds = Date.parse(proof.cutoffExclusive)
  assert.ok(boundary % 1000000n === 0n && format.format(milliseconds - 1) === cutoff
    && format.format(milliseconds) > cutoff, 'Use the first instant after the selected business day.')
  let eventDay
  if (calendarDate.test(proof.laterEffectiveAt)) {
    eventDay = checkedDate(proof.laterEffectiveAt)
  } else {
    assert.ok(instant(proof.laterEffectiveAt) >= boundary, 'The later event is inside the original period.')
    eventDay = format.format(Date.parse(proof.laterEffectiveAt))
  }
  assert.ok(cutoff < eventDay && eventDay <= laterCutoff, 'The later event must belong to the later requested business period.')
}

export function validateHistoricalControl(expected, laterExpected) {
  assert.notDeepEqual(laterExpected, expected,
    'Use a later event with an observable effect; include a changing detail or state alongside an invariant total.')
}

function unchanged(before, after) {
  assert.ok(before && typeof before === 'object' && Object.keys(before).length > 0, 'Capture the persisted business state.')
  assert.deepEqual(after, before, 'A refused operation changed the persisted state.')
}

export function validatePrerequisiteScope(expected) {
  assert.ok(/^document:[^\s:]+$/.test(expected.source)
    && [expected.prerequisite, expected.consumerField, expected.sourceField].every(nonempty),
  'Identify the prerequisite document and the two matching reference fields.')
  assert.ok(['same-owner', 'independent'].includes(expected.policy), 'Classify the shared reference against the business requirement.')
  for (const pair of [expected.expectedBefore, expected.expectedAfter]) {
    assert.ok(Array.isArray(pair) && pair.length === 2 && pair.every(populated),
      'Read the positions for the original and revised owners, in that order.')
  }
  if (expected.policy === 'same-owner') {
    assert.ok(nonempty(expected.expectedReason), 'Identify the expected business refusal.')
    assert.notDeepEqual(expected.expectedBefore[0], expected.expectedBefore[1], 'Establish distinguishable entitlements for the two owners.')
    assert.deepEqual(expected.expectedAfter, expected.expectedBefore, 'The attempted change must preserve both entitlements.')
  }
}

function verifyPrerequisiteScope(proof) {
  validatePrerequisiteScope(proof)
  assert.ok([proof.sourceId, proof.consumerId, proof.originalOwnerId, proof.revisedOwnerId].every(nonempty)
    && proof.originalOwnerId !== proof.revisedOwnerId && proof.sourceId !== proof.consumerId
    && proof.reopenedSourceId === proof.sourceId && proof.reopenedConsumerId === proof.consumerId
    && proof.initialConsumerSourceId === proof.sourceId && proof.persistedConsumerSourceId === proof.sourceId
    && proof.initialConsumerOwnerId === proof.originalOwnerId
    && proof.sourceOwnerId === proof.originalOwnerId, 'Change one reference while keeping the same source and consumer.')
  assert.ok(successful(proof.controlStatus) && nonempty(proof.controlId) && proof.reopenedControlId === proof.controlId
    && proof.controlSourceId === proof.sourceId && proof.controlOwnerId === proof.originalOwnerId,
    'Confirm and reopen the matching-owner control.')
  assert.deepEqual(proof.before, proof.expectedBefore)
  assert.deepEqual(proof.after, proof.expectedAfter)
  if (proof.policy === 'independent') {
    assert.ok(successful(proof.editStatus) && successful(proof.postStatus)
      && proof.persistedOwnerId === proof.revisedOwnerId && proof.state === 'Posted',
    'Demonstrate that changing this independent reference permits confirmation.')
    return
  }
  const edited = successful(proof.editStatus)
  const refusal = edited ? proof.postStatus : proof.editStatus
  assert.ok([400, 409, 422].includes(refusal) && (edited || proof.postStatus == null)
    && proof.persistedOwnerId === (edited ? proof.revisedOwnerId : proof.originalOwnerId)
    && proof.state === 'Draft' && nonempty(proof.expectedReason) && proof.reason === proof.expectedReason,
  'Refuse the owner edit or the subsequent confirmation and reopen the same unconfirmed draft.')
}

export function validateClassificationContext(expected) {
  assert.ok(/^document:[^\s:]+$/.test(expected.document) && /^(?:document|catalog):[^\s:]+$/.test(expected.contributor)
    && expected.contributor !== expected.document, 'Identify the contextual document and a distinct contributor.')
  assert.ok([expected.expectedGroups, expected.expectedWithContributor].every(pair =>
    Array.isArray(pair) && pair.length === 2 && pair.every(Number.isFinite)), 'Pin both groups before and after the control.')
  assert.notDeepEqual(expected.expectedGroups, expected.expectedWithContributor, 'Exercise a control with a measurable effect.')
}

function businessRefusal(status, reason, expected) {
  assert.ok([400, 409, 422].includes(status) && nonempty(expected) && reason === expected, 'Expected the specific business refusal.')
}

function verifyIntermediateBalance(proof) {
  assert.ok(nonempty(proof.source) && nonempty(proof.ownerId) && nonempty(proof.controlOwnerId), 'Identify both seeded histories.')
  assert.ok(Array.isArray(proof.dates) && proof.dates.length >= 3
    && proof.dates.every((date, index) => checkedDate(date) === date && (!index || date > proof.dates[index - 1])),
  'Read every affected date from the backdated attempt through the later replenishment.')
  for (const series of [proof.opening, proof.funded]) {
    assert.ok(Array.isArray(series) && series.length === proof.dates.length && series.every(value => Number.isSafeInteger(value) && value >= 0),
      'Pin the balance at every affected date for both histories.')
  }
  const delta = proof.change
  assert.ok(Number.isSafeInteger(delta) && delta < 0 && proof.opening[0] + delta >= 0 && proof.opening.at(-1) + delta >= 0
    && proof.opening.slice(1, -1).some(value => value + delta < 0) && proof.funded.every(value => value + delta >= 0),
  'Only an intermediate position may prevent the backdated posting. Fund the permitted control early.')
  assert.equal(proof.attemptDate, proof.dates[0])
  assert.equal(proof.controlDate, proof.attemptDate)
  assert.equal(proof.controlChange, delta)
  assert.equal(proof.funded.at(-1), proof.opening.at(-1), 'Keep the same final funds in the two histories.')
  assert.ok(nonempty(proof.attemptId) && proof.reopenedAttemptId === proof.attemptId)
  assert.deepEqual(proof.before, proof.opening)
  businessRefusal(proof.status, proof.reason, proof.expectedReason)
  assert.deepEqual(proof.after, proof.opening)
  assert.deepEqual(proof.controlBefore, proof.funded)
  assert.ok(successful(proof.controlStatus) && nonempty(proof.controlId) && proof.reopenedId === proof.controlId)
  assert.deepEqual(proof.controlAfter, proof.funded.map(value => value + delta))
}

function verifyDependencyWindow(proof) {
  assert.ok(nonempty(proof.operation) && nonempty(proof.consumer) && Number.isSafeInteger(proof.pageSize) && proof.pageSize > 0)
  assert.ok(Array.isArray(proof.samples) && proof.samples.length === 2, 'Exercise both sides of the dependency page boundary.')
  for (const [index, sample] of proof.samples.entries()) {
    assert.ok(Array.isArray(sample.dependentIds) && sample.dependentIds.length === proof.pageSize + index
      && sample.dependentIds.every(nonempty) && new Set(sample.dependentIds).size === sample.dependentIds.length
      && sample.dependentIds.at(-1) === sample.decisiveId, 'Place the decisive dependency last in the actual lookup order.')
    assert.ok(nonempty(sample.sourceId) && sample.reopenedId === sample.sourceId && sample.reopenedDependentId === sample.decisiveId)
    assert.equal(sample.operation, proof.operation)
    businessRefusal(sample.status, sample.reason, proof.expectedReason)
    validateWithdrawalState(sample.before)
    assert.deepEqual(sample.before, proof.expectedBefore[index])
    unchanged(sample.before, sample.after)
    assert.ok(successful(sample.controlStatus) && sample.controlSourceId === sample.sourceId && sample.resolvedId === sample.decisiveId)
    assert.deepEqual(sample.unchangedPrefixIds, sample.dependentIds.slice(0, -1),
      'Resolve only the decisive dependency and retain every earlier entry for the permitted control.')
    validateWithdrawalState(sample.afterControl)
    assert.notDeepEqual(sample.afterControl, sample.before, 'The permitted correction must change the persisted state.')
    assert.deepEqual(sample.afterControl, proof.expectedAfterControl[index])
  }
}

function verifyTerminalState(proof) {
  assert.ok(nonempty(proof.state) && nonempty(proof.consumer) && nonempty(proof.id) && proof.reopenedId === proof.id)
  assert.ok(successful(proof.terminalStatus) && proof.persistedState === proof.state && proof.reverseId === proof.id)
  businessRefusal(proof.reverseStatus, proof.reverseError, proof.reverseReason)
  businessRefusal(proof.useStatus, proof.useError, proof.useReason)
  assert.equal(proof.afterReverse, proof.state)
  assert.equal(proof.attemptedUseId, proof.id)
  assert.equal(proof.afterUse, proof.state)
  assert.ok(nonempty(proof.activeId) && proof.activeId !== proof.id && proof.controlUseId === proof.activeId
    && successful(proof.controlStatus), 'Use a distinct active sibling as the permitted control.')
  if (proof.report !== null) {
    assert.ok(/^report:[^\s:]+$/.test(proof.report) && nonempty(proof.historicalState) && proof.historicalState !== proof.state)
    assert.ok(nonempty(proof.laterState) && proof.laterState !== proof.historicalState,
      'Choose cutoffs on which the affected report shows the transition.')
    const date = calendarDate.test(proof.cutoff) ? checkedDate : instant
    assert.ok(date(proof.cutoff) < date(proof.terminalAt) && date(proof.terminalAt) <= date(proof.laterCutoff))
    for (const [index, read] of proof.reads.entries()) {
      assert.ok(successful(read.status) && read.id === proof.id && read.report === proof.report)
      assert.equal(read.cutoff, index === 2 ? proof.laterCutoff : proof.cutoff)
      assert.deepEqual(read.state, index === 2 ? proof.laterState : proof.historicalState)
    }
    assert.equal(proof.reads.length, 3, 'Read the identical historical scope before and after the transition and the later state.')
  }
}

function verifyRequiredReason(proof) {
  assert.ok(nonempty(proof.operation) && nonempty(proof.suppliedReason) && nonempty(proof.recordId))
  const inputs = new Set()
  for (const attempt of proof.attempts) {
    const { input } = attempt
    if (input === null) {
      inputs.add('omitted')
    } else {
      assert.ok(typeof input === 'string' && input.trim() === '', 'Refused reason evidence must describe a blank input.')
      inputs.add(input === '' ? 'empty' : 'whitespace')
    }
    if (attempt.id == null) {
      assert.ok(nonempty(attempt.requestKey) && attempt.reopenedKey === attempt.requestKey
        && attempt.before?.matchingRecords === 0, 'Look up the refused creation by its unique business key.')
    } else {
      assert.ok(nonempty(attempt.id) && attempt.reopenedId === attempt.id)
    }
    assert.equal(attempt.operation, proof.operation)
    businessRefusal(attempt.status, attempt.reason, proof.expectedReason)
    unchanged(attempt.before, attempt.after)
  }
  assert.deepEqual(inputs, new Set(['omitted', 'empty', 'whitespace']), 'Exercise omitted, empty and whitespace-only reasons.')
  assert.ok(successful(proof.controlStatus) && proof.reopenedId === proof.recordId && proof.persistedReason === proof.suppliedReason)
  assert.ok(proof.audit?.recordId === proof.recordId && proof.audit.operation === proof.operation
    && proof.audit.reason === proof.suppliedReason && nonempty(proof.actorId) && proof.audit.actorId === proof.actorId
    && Number.isFinite(Date.parse(proof.audit.at)), 'Read the entered reason and actor from the successful operation in audit history.')
}

function verifyObservation(entry, proof) {
  if (entry.kind === 'intermediate-balance') {
    verifyIntermediateBalance(proof)
  } else if (entry.kind === 'dependency-window') {
    verifyDependencyWindow(proof)
  } else if (entry.kind === 'terminal-state') {
    verifyTerminalState(proof)
  } else if (entry.kind === 'required-reason') {
    verifyRequiredReason(proof)
  } else if (entry.kind === 'prerequisite-scope') {
    verifyPrerequisiteScope(proof)
  } else if (entry.kind === 'classification-context') {
    validateClassificationContext(proof)
    assert.ok(nonempty(proof.documentId) && proof.reopenedId === proof.documentId && successful(proof.postStatus)
      && successful(proof.reportStatus) && successful(proof.controlStatus) && nonempty(proof.contributorId)
      && successful(proof.controlReportStatus) && proof.contributorId !== proof.documentId,
    'Post the contextual document and read its linked contribution separately from the source of the totals.')
    assert.deepEqual(proof.before, proof.expectedGroups)
    assert.deepEqual(proof.after, proof.before, 'The contextual document contributed to the group totals.')
    assert.deepEqual(proof.withContributor, proof.expectedWithContributor, 'Verify the control in the same report scope.')
  } else if (entry.kind === 'source-membership') {
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
  } else if (entry.kind === 'competing-consumers') {
    assert.ok(nonempty(proof.sourceId) && Array.isArray(proof.attempts) && proof.attempts.length === 2,
      'Exercise two documents that consume the same source.')
    const [winner, refused] = [...proof.attempts].sort((left, right) => left.status - right.status)
    assert.ok(successful(winner.status) && [400, 409, 422].includes(refused.status)
      && nonempty(winner.id) && nonempty(refused.id) && winner.id !== refused.id
      && proof.attempts.every(item => item.sourceId === proof.sourceId && item.reopenedId === item.id)
      && winner.state === 'Posted' && refused.state === 'Draft' && nonempty(proof.expectedReason)
      && refused.reason === proof.expectedReason, 'Read back the winning document and the distinct refused draft for the same source.')
  } else if (entry.kind === 'paged-total') {
    verifyPagedTotal(proof)
  } else if (['owner-change', 'boundary-withdrawal', 'state-invariant', 'dated-withdrawal', 'effective-prerequisite', 'history-conflict', 'interval-overlap'].includes(entry.kind)) {
    assert.ok(successful(proof.controlStatus), 'The authorized positive control must succeed.')
    assert.ok([400, 409, 422].includes(proof.refusalStatus) && nonempty(proof.expectedReason)
      && proof.reason === proof.expectedReason, 'Expected the specific business refusal.')
    unchanged(proof.beforeRefusal, proof.afterRefusal)
    if (entry.kind === 'owner-change') {
      assert.ok(nonempty(proof.owner) && nonempty(proof.originalOwnerId) && nonempty(proof.revisedOwnerId)
        && proof.originalOwnerId !== proof.revisedOwnerId && proof.sourceOwnerId === proof.originalOwnerId
        && proof.persistedDraftOwnerId === proof.revisedOwnerId && proof.controlOwnerId === proof.sourceOwnerId
        && nonempty(proof.sourceId) && nonempty(proof.persistedControlId),
      'Change the persisted draft owner after establishing the original entitlement and verify the matching-owner control.')
    } else if (entry.kind === 'boundary-withdrawal') {
      assert.ok(nonempty(proof.resourceId) && nonempty(proof.originalId) && nonempty(proof.successorId)
        && proof.originalId !== proof.successorId
        && successful(proof.successorStatus) && proof.beforeRefusal.activeId === proof.successorId
        && proof.beforeRefusal.activeCount === 1 && nonempty(proof.persistedControlId),
      'Withdraw a release after a distinct successor takes the resource at the same instant; verify the remaining holder.')
      const release = instant(proof.releasedAt)
      const successor = instant(proof.successorAt)
      if (proof.boundary === 'exclusive-end') assert.equal(successor, release, 'Exercise two adjacent events at the same instant.')
      else assert.ok(proof.boundary === 'inclusive' && release < successor
        && [400, 409, 422].includes(proof.sameInstantStatus), 'Verify the refused shared endpoint and the permitted successor.')
    } else if (entry.kind === 'state-invariant') {
      assert.ok(nonempty(proof.persistedControlId) && nonempty(proof.operation),
        'Exercise the prohibited write and read back the authorized positive control.')
    } else if (entry.kind === 'dated-withdrawal') {
      assert.ok(nonempty(proof.sourceId) && nonempty(proof.consumerId) && proof.sourceId !== proof.consumerId
        && Date.parse(proof.sourceAt) <= Date.parse(proof.consumerAt), 'Read back the dependent operation and its original prerequisite.')
      if (proof.replaceable === true) {
        assert.ok(nonempty(proof.replacementId) && proof.replacementId !== proof.sourceId && nonempty(proof.replacementAt),
          'Exercise the later replacement of the reviewed prerequisite.')
      }
      if (proof.replacementAt !== undefined) {
        assert.ok(Date.parse(proof.consumerAt) < Date.parse(proof.replacementAt), 'Exercise a replacement effective after the dependent operation.')
      }
      validateWithdrawalState(proof.beforeRefusal)
      validateWithdrawalState(proof.afterControl)
      assert.equal(proof.persistedControlId, proof.sourceId, 'Read back the original source after its permitted withdrawal.')
      assert.equal(proof.controlConsumerId, proof.consumerId, 'Read back the same dependent operation after correction.')
      assert.notDeepEqual(proof.afterControl.source, proof.beforeRefusal.source, 'The permitted correction must withdraw the source.')
      assert.deepEqual(proof.afterControl, proof.expectedControl, 'Incorrect source, consumer or dated balance after correction.')
    } else if (entry.kind === 'effective-prerequisite') {
      if (proof.source !== undefined) {
        assert.ok(nonempty(proof.ownerId) && proof.sourceOwnerId === proof.ownerId && nonempty(proof.sourceId)
          && nonempty(proof.effectiveAt), 'Read the dated entitlement for the consuming owner.')
      }
      assert.ok(nonempty(proof.persistedControlId) && Date.parse(proof.attemptAt) < Date.parse(proof.prerequisiteAt)
        && Date.parse(proof.prerequisiteAt) <= Date.parse(proof.controlAt),
      'Exercise both sides of the prerequisite effective date.')
    } else if (entry.kind === 'interval-overlap') {
      assert.ok(['inclusive', 'exclusive-end'].includes(proof.boundary), 'Declare how the business treats interval endpoints.')
      const precedes = proof.boundary === 'inclusive' ? (a, b) => a <= b : (a, b) => a < b
      const start = Date.parse(proof.priorStart)
      const end = Date.parse(proof.priorEnd)
      const from = Date.parse(proof.attemptStart)
      const to = Date.parse(proof.attemptEnd)
      assert.ok(nonempty(proof.resourceId) && nonempty(proof.closedRecordId)
        && precedes(start, end) && precedes(from, to) && precedes(from, end) && precedes(start, to),
      'Attempt to overlap a completed interval for the same business key.')
      const controlFrom = Date.parse(proof.controlStart)
      const controlTo = Date.parse(proof.controlEnd)
      assert.ok(precedes(controlFrom, controlTo) && !(precedes(controlFrom, end) && precedes(start, controlTo))
        && nonempty(proof.persistedControlId) && proof.persistedControlId !== proof.closedRecordId,
      'Read back the successful nonoverlapping control.')
    } else {
      assert.ok(Number.isSafeInteger(proof.pageLimit) && proof.pageLimit > 0
        && Array.isArray(proof.newerIds) && proof.newerIds.length >= proof.pageLimit
        && proof.newerIds.every(nonempty) && new Set(proof.newerIds).size === proof.newerIds.length
        && nonempty(proof.conflictId) && !proof.newerIds.includes(proof.conflictId),
      'Place the conflicting key beyond the first full page of distinct newer records.')
    }
  } else if (entry.kind === 'snapshot-stability') {
    assert.ok(nonempty(proof.sourceId) && nonempty(proof.consumerId)
      && Date.parse(proof.capturedAt) <= Date.parse(proof.changedAt)
      && Date.parse(proof.changedAt) < Date.parse(proof.consumedAt),
    'Change the source after capture and before completing the consuming operation.')
    if (proof.from !== undefined || proof.until !== undefined) {
      assert.ok(nonempty(proof.from) && nonempty(proof.until) && proof.from !== proof.until
        && proof.capture?.operation === proof.from && proof.consumption?.operation === proof.until
        && proof.capture.id === proof.sourceId && proof.consumption.id === proof.consumerId
        && successful(proof.capture.status) && successful(proof.consumption.status),
      'Read the captured and consumed facts through the two reviewed public operations.')
    }
    for (const key of ['original', 'changed', 'expected', 'observed', 'freshExpected', 'freshObserved']) {
      assert.ok(proof[key] != null, `Missing snapshot observation: ${key}`)
    }
    assert.notDeepEqual(proof.original, proof.changed, 'Change the classification, rate or calendar.')
    assert.deepEqual(proof.observed, proof.expected, 'The consumer must use the captured business fact.')
    assert.notDeepEqual(proof.expected, proof.freshExpected, 'Use a fresh transaction affected by the change.')
    assert.deepEqual(proof.freshObserved, proof.freshExpected, 'The new source value must take effect on new work.')
    if (proof.report !== undefined) {
      assert.ok(proof.until === entry.target && nonempty(proof.groupField) && successful(proof.reportStatus)
        && successful(proof.freshReportStatus) && nonempty(proof.freshConsumerId)
        && proof.freshConsumerId !== proof.consumerId && nonempty(proof.original) && nonempty(proof.changed)
        && [proof.expected, proof.freshExpected].every(pair => Array.isArray(pair) && pair.length === 2
          && pair.every(Number.isFinite)), 'Read both groups for the posted document and a distinct fresh control.')
    }
  } else if (entry.kind === 'report-export') {
    assert.ok(successful(proof.downloadStatus) && proof.contentType === 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      'Download the workbook through the report endpoint.')
    validateExportCells(proof.expectedCells)
    assert.deepEqual(proof.screenCells, proof.expectedCells, 'Incorrect report figures for the selected scope.')
    assert.deepEqual(proof.worksheetCells, proof.expectedCells, 'Incorrect exported cells for the selected scope.')
  } else if (entry.kind === 'business-day-cutoff') {
    assert.match(proof.businessDate, /^\d{4}-\d{2}-\d{2}$/)
    const instant = Date.parse(proof.businessDate + 'T12:00:00Z')
    const days = [-1, 0, 1].map(offset => new Date(instant + offset * 86400000).toISOString().slice(0, 10))
    assert.equal(days[1], proof.businessDate, 'Use a valid calendar date.')
    assert.ok(nonempty(proof.recordId) && Array.isArray(proof.zones) && proof.zones.length >= 2,
      'Read the dated record at three consecutive business cutoffs in distinct configured zones.')
    assert.ok(proof.opening != null && proof.closing != null, 'Read both business positions.')
    assert.notDeepEqual(proof.opening, proof.closing, 'Use a dated event that changes the measured position.')
    const offsets = new Set()
    for (const sample of proof.zones) {
      const zone = new Intl.DateTimeFormat('en-CA', { timeZone: sample.zone, year: 'numeric', month: '2-digit', day: '2-digit' })
      assert.ok(nonempty(sample.zone) && sample.configuredZone === sample.zone, 'Read back the configured business zone.')
      offsets.add(zone.format(instant))
      assert.deepEqual(sample.dates, days, 'Record the actual three consecutive report cutoffs.')
      assert.deepEqual(sample.values, [proof.opening, proof.closing, proof.closing], 'Reconcile the previous, selected and next business days.')
    }
    assert.ok(offsets.size >= 2, 'Include a zone where noon UTC falls on a different business date.')
  } else if (entry.kind === 'restore-rehearsal') {
    assert.ok(nonempty(proof.sourceInstance) && nonempty(proof.restoredInstance)
      && proof.sourceInstance !== proof.restoredInstance && Number.isSafeInteger(proof.backupBytes) && proof.backupBytes > 0,
    'Restore a nonempty backup into a separate, disposable instance.')
    assert.match(proof.backupSha256 ?? '', /^[a-f0-9]{64}$/)
    assert.ok(successful(proof.readStatus) && nonempty(proof.recordId), 'Reopen a known business record after restoring.')
    unchanged(proof.beforeBackup, proof.afterRestore)
    if (entry.stores?.includes('identity')) {
      const login = proof.restoredLogin
      assert.ok(login && successful(login.status) && nonempty(login.subject) && login.subject === login.originalSubject,
        'Log in with the restored identity and compare its subject with the original.')
    }
    if (entry.stores?.includes('attachments')) {
      const file = proof.restoredAttachment
      assert.ok(file && successful(file.status) && nonempty(file.id) && Number.isSafeInteger(file.bytes) && file.bytes > 0,
        'Download the restored attachment.')
      assert.match(file.originalSha256, /^[a-f0-9]{64}$/)
      assert.equal(file.sha256, file.originalSha256, 'Compare the downloaded contents with the original attachment.')
    }
  } else if (entry.kind === 'query-growth') {
    const { small, large } = proof
    validateValueFields(proof.valueFields)
    for (const [sample, rows] of [[small, entry.budget.smallRows], [large, entry.budget.largeRows]]) {
      assert.ok(sample && successful(sample.status) && sample.returnedRows === rows
        && Number.isSafeInteger(sample.queries) && sample.queries > 0, 'Measure a successful, instrumented read of the declared row count.')
      assert.ok(nonempty(sample.actor) && sample.operation === entry.key, 'Measure the bound operation with an identified actor.')
      if (proof.operation !== undefined) {
        assert.equal(sample.requestOperation, proof.operation, 'Measure the reviewed public operation in each request trace.')
      }
      checkPageValues(sample, proof.valueFields)
    }
    assert.equal(small.actor, large.actor, 'Use the same actor for both requests.')
    assert.ok(large.queries - small.queries <= entry.budget.maxAdditionalQueries, 'Database command growth exceeded the reviewed row budget.')
  } else if (entry.kind === 'capacity') {
    const budget = entry.budget
    for (const [key, minimum] of Object.entries(budget.populations)) {
      assert.ok(Number.isSafeInteger(proof.populations?.[key]) && proof.populations[key] >= minimum,
        `Insufficient persisted population for ${key}.`)
    }
    assert.ok(Array.isArray(proof.samples) && proof.samples.length >= budget.samples, 'Run the declared business workload.')
    for (const sample of proof.samples) {
      assert.ok(successful(sample.status) && Number.isFinite(sample.milliseconds) && sample.milliseconds > 0
        && Number.isSafeInteger(sample.queries) && sample.queries >= 0 && sample.queries <= budget.maxQueries,
      'Business request failed or exceeded its query budget.')
      if (budget.maxWorkingSetBytes !== undefined) {
        assert.ok(Number.isSafeInteger(sample.workingSetBytes) && sample.workingSetBytes > 0
          && sample.workingSetBytes <= budget.maxWorkingSetBytes, 'Missing or excessive server working set during the workload.')
      }
    }
    assert.ok(proof.samples.some(sample => sample.queries > 0), 'Include an instrumented database read in the measured workload.')
    const times = proof.samples.map(sample => sample.milliseconds).sort((a, b) => a - b)
    assert.ok(times[Math.ceil(times.length * 0.95) - 1] <= budget.p95Milliseconds, 'Business workload exceeded its latency budget.')
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
    verifyCutoffPeriod(proof)
    assert.ok(nonempty(proof.laterEvent) && successful(proof.laterStatus),
      'Commit a distinct later event affecting this measure.')
    assert.ok(Object.hasOwn(proof, 'expected') && Object.hasOwn(proof, 'before') && Object.hasOwn(proof, 'after'),
      'Capture the expected and both observed values for this measure.')
    assert.deepEqual(proof.before, proof.expected, 'Incorrect original historical value.')
    assert.deepEqual(proof.after, proof.expected, 'A later event changed the earlier result.')
    assert.ok(Object.hasOwn(proof, 'laterExpected') && Object.hasOwn(proof, 'laterActual'), 'Read a control result at the later cutoff.')
    assert.deepEqual(proof.laterActual, proof.laterExpected, 'Incorrect later-period control result.')
    validateHistoricalControl(proof.expected, proof.laterExpected)
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
