import assert from 'node:assert/strict'
import { mkdir, mkdtemp, readFile, realpath, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import { verifyBusinessObservations } from '../../packaging/templates/content/infrastructure/ngb/business-checks.mjs'
import { verifyOutcomeProof } from '../../packaging/templates/content/infrastructure/ngb/acceptance-outcomes.mjs'
import { verifyOperationReview, verifyRuntimeOperationReview } from '../../packaging/templates/content/infrastructure/ngb/operation-review.mjs'
import { captureObligations } from '../../packaging/templates/content/infrastructure/ngb/requirement-trace.mjs'

const request = 'document:example.request'
const credit = 'document:example.credit'
const release = 'document:example.release'
const post = { operation: 'post', requirement: 'integrity', target: request }
const prerequisite = { kind: 'consumes', key: 'available', source: credit, owner: 'party', effectiveAt: 'workDate',
  editableOwner: true, withdrawable: true, replaceable: true }
const facts = [
  prerequisite,
  { kind: 'snapshot', key: 'calendar-open', from: 'opened', until: 'closed' },
  { kind: 'snapshot', key: 'group-issue', from: 'opened', until: 'issued' },
  { kind: 'exclusive', key: 'resource', correctable: true, releaseTarget: release, boundary: 'exclusive-end' },
  { kind: 'contended', key: 'competing', scope: 'shared-source' },
]
const proofs = [
  { kind: 'effective-prerequisite', key: 'available', target: request, expect: { source: credit, owner: 'party', effectiveAt: 'workDate' } },
  { kind: 'owner-change', key: 'available', target: request, expect: { owner: 'party' } },
  { kind: 'dated-withdrawal', key: 'available', target: credit, expect: { consumer: request, replaceable: true } },
  { kind: 'snapshot-stability', key: 'calendar-open', target: request, expect: { from: 'opened', until: 'closed' } },
  { kind: 'snapshot-stability', key: 'group-issue', target: request, expect: { from: 'opened', until: 'issued' } },
  { kind: 'interval-overlap', key: 'resource', target: request },
  { kind: 'boundary-withdrawal', key: 'resource', target: release, expect: { boundary: 'exclusive-end' } },
  { scenario: 'concurrent-operation', target: request, outcome: 'competing-requests' },
  { kind: 'competing-consumers', target: request, key: 'competing' },
]

test('independently reviewed facts survive an omission or substitution in the generated proofs', () => {
  const rows = [{ ...post, facts }]
  const verify = entries => verifyOperationReview(rows, [{ id: 'integrity', proofs: entries }])
  verify(proofs)
  for (const omitted of proofs) assert.throws(() => verify(proofs.filter(proof => proof !== omitted)))
  assert.throws(() => verify(proofs.map(proof => proof.key === 'group-issue' ? { ...proof, expect: { from: 'issued', until: 'reviewed' } } : proof)), /Bind from/)
  assert.throws(() => verify(proofs.map(proof => proof.scenario ? { ...proof, target: credit } : proof)), /own target/)
  assert.throws(() => verifyOperationReview([{ ...post, facts: [], reason: 'The current balance is sufficient.' }],
    [{ id: 'integrity', proofs }]), /Review the business fact/)
  assert.throws(() => verifyOperationReview([], [], { documents: [{ type: 'example.request' }] }), /Review each application document/)
})

test('the review distinguishes journal, worker and local editor operations', () => {
  const job = 'job:example.notifications'
  const target = request
  const override = { scenario: 'pricing', target, outcome: 'posted-override:example.request' }
  const rows = [
    { ...post, facts: [{ kind: 'manual-price', key: 'adjust' }, { kind: 'contended', key: 'edit', scope: 'record' }] },
    { ...post, target: job, facts: [], reason: 'Sends the scheduled summary.' },
  ]
  const obligations = [{ id: post.requirement, proofs: [override, { scenario: 'concurrent-operation', target, outcome: 'competing-requests' }] }]
  const plan = { documents: [{ type: 'example.request', asynchronous: false }, { type: 'general_journal_entry' }],
    cases: [override, { scenario: 'lines', target }, { scenario: 'concurrent-operation', target }], businessChecks: [{ target: job }] }
  verifyOperationReview(rows, obligations, plan)
  assert.throws(() => verifyOperationReview(rows, [{ ...obligations[0], proofs: obligations[0].proofs.slice(1) }], plan), /manual override/)
  assert.throws(() => verifyOperationReview(rows, obligations, { ...plan, businessChecks: [] }), /Unknown reviewed surface/)
  const local = { ...rows[0], facts: rows[0].facts.slice(0, 1) }
  assert.throws(() => verifyOperationReview([local], obligations, plan), /contended operation/)
  assert.throws(() => verifyOperationReview([{ ...post, facts: [{ kind: 'unknown', key: 'edit' }] }], obligations), /Unknown reviewed/)
})

test('live lookups and contributing document types expose omissions even when the plan agrees with the review', () => {
  const report = 'report:example.activity'
  const lookup = key => ({ key, lookup: { kind: 'catalog', catalogType: 'example.party' } })
  const metadata = (target, fields) => ({ documentType: target.slice(9), form: { sections: [{ rows: [{ fields }] }] } })
  const context = 'document:example.folder'
  const inventory = {
    documents: [metadata(request, [lookup('beneficiary'), { ...lookup('creator'), isReadOnly: true }]),
      metadata(credit, [lookup('recipient')])],
    reports: [{ reportCode: 'example.activity', dataset: { fields: [
      { code: 'group', isGroupable: true },
      { code: 'record', lookup: { kind: 'document', documentTypes: [request.slice(9), release.slice(9), context.slice(9)] } },
    ] } }],
  }
  const rows = [{ ...post, facts: [{ ...prerequisite, owner: 'parent', editableOwner: false, position: 'eligibility',
    corrections: [{ operation: 'unpost', cardinality: 'single' }] }] }]
  const plan = { cases: [{ scenario: 'historical-classification', target: report }], surfaces: [request, release, context, report] }
  const scope = { kind: 'prerequisite-scope', target: request, expect: {
    prerequisite: prerequisite.key, source: credit, consumerField: 'beneficiary', sourceField: 'recipient',
  } }
  const phase = target => ({ kind: 'snapshot-stability', target, expect: { report, groupField: 'group', until: target } })
  const link = { kind: 'classification-context', target: report, expect: { document: context, contributor: request } }
  const verify = proofs => verifyRuntimeOperationReview(rows, [{ proofs }], plan, inventory)
  assert.throws(() => verify([]), /shared prerequisite reference/)
  assert.throws(() => verify([scope]), /historical report group/)
  verify([scope, phase(request), phase(release), link])
  assert.throws(() => verify([scope, phase(request), link]), /before posting document:example.release/)
  assert.throws(() => verify([scope, phase(release), link]), /before posting document:example.request/)
  assert.throws(() => verify([scope, phase(request), phase(release)]), /document:example.folder/)
  const wrongSource = { ...scope, expect: { ...scope.expect, sourceField: 'beneficiary' } }
  assert.throws(() => verify([wrongSource, phase(request), phase(release), link]), /shared prerequisite reference/)
  const afterPosting = { ...phase(request), expect: { ...phase(request).expect, until: 'render-report' } }
  assert.throws(() => verify([scope, afterPosting, phase(release), link]), /before posting document:example.request/)
  const history = [{ proofs: [phase(request), phase(release), link] }]
  const [consumer, source] = inventory.documents
  for (const documents of [[consumer], [source], [{ ...consumer, capabilities: { canEditDraft: false } }, source]]) {
    verifyRuntimeOperationReview(rows, history, plan, { ...inventory, documents })
  }

  const draftField = { ...lookup('beneficiary'), readOnlyWhenStatusIn: [1] }
  const unrelated = { key: 'department', lookup: { kind: 'catalog', catalogType: 'example.department' } }
  const reviewFields = (field, sourceFields) => verifyRuntimeOperationReview(rows, history, plan, {
    ...inventory, documents: [metadata(request, [field]), metadata(credit, sourceFields)],
  })
  reviewFields(draftField, [lookup('recipient')])
  reviewFields(lookup('beneficiary'), [unrelated])
  assert.throws(() => reviewFields({ ...draftField, readOnlyWhenStatusIn: [2] }, [unrelated, lookup('recipient')]),
    /shared prerequisite reference: document:example.request\/beneficiary, document:example.credit\/recipient/)
})

test('changing one draft reference preserves the two scoped positions or proves an independent reference', () => {
  const entry = { kind: 'prerequisite-scope', target: request, key: 'available-beneficiary', test: 'Fixture.Scope' }
  const plan = { surfaces: [request], businessChecks: [entry] }
  const proof = { ...entry, policy: 'same-owner', source: credit, prerequisite: 'available', consumerField: 'beneficiary',
    sourceField: 'recipient', sourceId: 'authorization', consumerId: 'draft', reopenedSourceId: 'authorization',
    reopenedConsumerId: 'draft', originalOwnerId: 'A', sourceOwnerId: 'A', revisedOwnerId: 'B',
    initialConsumerSourceId: 'authorization', persistedConsumerSourceId: 'authorization', initialConsumerOwnerId: 'A',
    persistedOwnerId: 'B', state: 'Draft', editStatus: 200, postStatus: 409, reason: 'not-funded', expectedReason: 'not-funded',
    controlId: 'control', reopenedControlId: 'control', controlSourceId: 'authorization', controlOwnerId: 'A', controlStatus: 200,
    before: [50, 0], after: [50, 0], expectedBefore: [50, 0], expectedAfter: [50, 0] }
  const check = changes => verifyBusinessObservations(plan, [{ test: entry.test, proof: { ...proof, ...changes } }])
  check({})
  check({ editStatus: 409, postStatus: null, persistedOwnerId: 'A' })
  check({ policy: 'independent', postStatus: 200, state: 'Posted', after: [40, 0], expectedAfter: [40, 0] })
  for (const invalid of [{ after: [0, 50] }, { postStatus: 200 }, { state: 'Posted' }, { reopenedConsumerId: 'other' },
    { controlSourceId: 'different' }, { persistedConsumerSourceId: 'other' }, { initialConsumerOwnerId: 'B' },
    { editStatus: 409, postStatus: null }, { policy: 'independent' }]) {
    assert.throws(() => check(invalid))
  }
})

test('a historical group observation distinguishes the timing and distribution of a posting', () => {
  const entry = { kind: 'snapshot-stability', target: release, key: 'report-group', test: 'Fixture.Groups' }
  const plan = { surfaces: [release], businessChecks: [entry] }
  const proof = { ...entry, report: 'report:example.activity', groupField: 'class', sourceId: 'source',
    consumerId: 'issued', freshConsumerId: 'later', from: request, until: release,
    capture: { operation: request, id: 'source', status: 200 }, consumption: { operation: release, id: 'issued', status: 200 },
    capturedAt: '2026-05-01', changedAt: '2026-05-02', consumedAt: '2026-05-03', original: 'A', changed: 'B',
    expected: [20, 0], observed: [20, 0], freshExpected: [0, 20], freshObserved: [0, 20], reportStatus: 200, freshReportStatus: 200 }
  const verify = changes => verifyBusinessObservations(plan, [{ test: entry.test, proof: { ...proof, ...changes } }])
  verify({})
  for (const invalid of [{ observed: [0, 20] }, { observed: [30, -10] }, { changedAt: '2026-05-04' },
    { freshConsumerId: 'issued' }, { reportStatus: 404 }, { freshObserved: [20, 0] }]) {
    assert.throws(() => verify(invalid))
  }

  const link = { kind: 'classification-context', target: proof.report, key: 'request-link', test: 'Fixture.Context' }
  const context = { ...link, document: request, documentId: 'context-id', reopenedId: 'context-id',
    contributor: release, contributorId: 'posting-id', postStatus: 200, controlStatus: 200, reportStatus: 200,
    expectedGroups: [4, 7], before: [4, 7], after: [4, 7], withContributor: [6, 7],
    expectedWithContributor: [6, 7], controlReportStatus: 200 }
  const check = changes => verifyBusinessObservations({ surfaces: [proof.report], businessChecks: [link] },
    [{ test: link.test, proof: { ...context, ...changes } }])
  check({})
  for (const invalid of [{ after: [5, 7] }, { withContributor: [4, 7] }, { withContributor: [9, 9] },
    { controlReportStatus: 403 }, { contributor: request }]) {
    assert.throws(() => check(invalid))
  }
})

test('the priced editor and long-history totals need their own measured operations', () => {
  const target = request
  const pricing = { scenario: 'pricing', outcome: 'revised-price', target }
  const priceOperation = 'POST /api/quotes'
  const plan = { cases: [{ ...pricing, priceOperation }, { target, scenario: 'lines' }, { target, scenario: 'settlement' }],
    documents: [], surfaces: [target] }
  const row = { target, operation: 'POST /api/quotes', requirement: 'price', facts: [{ kind: 'rows', key: 'quotes' }] }
  const history = { target, operation: 'GET /api/position', requirement: 'position', facts: [{ kind: 'history-total', key: 'history' }] }
  const checks = [
    { id: 'price', proofs: [pricing, { target, kind: 'query-growth', key: 'quotes', expect: { operation: row.operation } }] },
    { id: 'position', proofs: [{ target, kind: 'paged-total', key: 'history', expect: { operation: history.operation } }] },
  ]
  verifyOperationReview([row, history], checks, plan)
  verifyOperationReview([{ ...row, requirement: 'capacity' }, history], [
    { id: 'price', proofs: [pricing] }, { id: 'capacity', proofs: checks[0].proofs.slice(1) }, checks[1],
  ], plan)
  assert.throws(() => verifyOperationReview([row], [checks[0]], plan), /history page boundary/)
  assert.throws(() => verifyOperationReview([row, history], [{ ...checks[0], proofs: checks[0].proofs.slice(1) }, checks[1]], plan), /priced editor/)
  assert.throws(() => verifyOperationReview([{ ...row, operation: 'GET /api/list' }, history], checks, plan), /Bind operation/)
  const otherOperation = 'GET /api/list'
  const unrelated = { ...checks[0], proofs: [pricing, { ...checks[0].proofs[1], expect: { operation: otherOperation } }] }
  assert.throws(() => verifyOperationReview([{ ...row, operation: otherOperation }, history], [unrelated, checks[1]], plan), /priced editor/)
  const priced = { id: 'remote', target, outcomes: ['revised-price'], priceOperation }
  const observed = path => ({ outcomes: priced.outcomes,
    evidence: { 'revised-price': ['posted-price:example.request', 'price-request:' + path] } })
  verifyOutcomeProof(priced, observed(priceOperation))
  assert.throws(() => verifyOutcomeProof(priced, observed(otherOperation)), /measured price request/)
  const entry = { id: 'changed', target, outcomes: ['revised-price'] }
  for (const label of ['text', 'posted-price:example.other', 'current-retry', 'posted-override:example.request']) {
    assert.throws(() => verifyOutcomeProof(entry, { outcomes: entry.outcomes, evidence: { 'revised-price': [label] } }))
  }
})

test('capture rejects mandatory evidence disguised as context and keeps the last good lock', async t => {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'ngb-reviewed-')))
  t.after(() => rm(root, { recursive: true, force: true }))
  await mkdir(join(root, 'ai'))
  const save = (name, data) => writeFile(join(root, 'ai', name), JSON.stringify(data))
  const source = 'brief.md'
  const quote = 'Verify concurrent submissions.'
  const background = 'Operators share one work center.'
  await writeFile(join(root, source), quote + '\n' + background)
  await save('brief-sources.json', [source])
  await save('business-obligations.json', [{ id: 'submissions', quote, source, effect: 'operations',
    proofs: [{ scenario: 'concurrent-operation', target: request, outcome: 'competing-requests' }] }])
  await save('operation-review.json', [{ ...post, requirement: 'submissions', facts: [{ kind: 'contended', key: 'submit', scope: 'record' }] }])
  await save('brief-context.json', [{ quote: background, source, reason: 'Describes the audience.', category: 'background' }])
  await captureObligations(root, true)
  const lock = join(root, 'ai/business-obligations.lock.json')
  const previous = await readFile(lock, 'utf8')
  for (const block of ['- Concurrent submissions and releases', '- Controls\n- Verify contention', 'Controls must be tested.']) {
    await writeFile(join(root, source), quote + '\n' + block)
    await save('brief-context.json', [{ source, quote: block, reason: 'Claimed to be covered.', category: 'background' }])
    await assert.rejects(captureObligations(root, true), /Capture each required clause/)
    assert.equal(await readFile(lock, 'utf8'), previous)
  }
  const item = '- Batch review'
  const excluded = { source, quote: item, category: 'out-of-scope', reason: 'Excluded by the brief.' }
  const heading = title => ({ source, quote: title, reason: 'Section heading.' })
  const excludedSection = { ...heading('## Out of scope'), category: 'out-of-scope', reason: 'Names the excluded features.' }
  await writeFile(join(root, source), [quote, '## Out of scope', item].join('\n'))
  await save('brief-context.json', [excludedSection, excluded])
  await captureObligations(root, false)
  await writeFile(join(root, source), [quote, '## Out of scope', item, '## Verification', item].join('\n'))
  await save('brief-context.json', [excludedSection, heading('## Verification'), excluded])
  await assert.rejects(captureObligations(root, true), /Capture each required clause/)
  assert.equal(await readFile(lock, 'utf8'), previous)
  const path = join(root, 'ai/business-obligations.json')
  const existing = JSON.parse(await readFile(path, 'utf8'))
  await save('business-obligations.json', [...existing, { id: 'batch', quote: 'Batch review', source,
    effect: 'operations', proofs: [{ integrationTest: 'Example.BatchReview' }] }])
  await captureObligations(root, false)
})

test('boundary, ownership and history proofs require persisted values and a successful control', () => {
  const base = { controlStatus: 200, refusalStatus: 409, expectedReason: 'dependent', reason: 'dependent',
    beforeRefusal: { activeCount: 1, activeId: 'next' }, afterRefusal: { activeCount: 1, activeId: 'next' }, persistedControlId: 'control' }
  function exercise(kind, proof, changes) {
    const entry = { target: request, key: 'position', kind, test: 'Fixture.Observation' }
    const plan = { surfaces: [request], businessChecks: [entry] }
    const check = payload => verifyBusinessObservations(plan, [{ test: entry.test, proof: { ...entry, ...payload } }])
    check(proof)
    for (const change of changes) assert.throws(() => check({ ...proof, ...change }))
  }
  exercise('owner-change', { ...base, owner: 'party', originalOwnerId: 'a', sourceOwnerId: 'a', controlOwnerId: 'a',
    revisedOwnerId: 'b', persistedDraftOwnerId: 'b', sourceId: 'credit' },
  [{ revisedOwnerId: 'a' }, { sourceOwnerId: 'b' }, { refusalStatus: 200 }])
  exercise('boundary-withdrawal', { ...base, resourceId: 'asset', originalId: 'first', successorId: 'next',
    releasedAt: '2026-04-08T12:00:00Z', successorAt: '2026-04-08T12:00:00Z', successorStatus: 200, boundary: 'exclusive-end' },
  [{ successorAt: '2026-04-08T12:01:00Z' }, { successorId: 'first' }, { afterRefusal: { activeCount: 2 } }])
  exercise('boundary-withdrawal', { ...base, resourceId: 'asset', originalId: 'first', successorId: 'next',
    releasedAt: '2026-04-08T12:00:00Z', successorAt: '2026-04-08T12:00:01Z', sameInstantStatus: 409,
    successorStatus: 200, boundary: 'inclusive' }, [{ sameInstantStatus: 200 }, { successorAt: '2026-04-08T12:00:00Z' }])
  const winner = { id: 'first', reopenedId: 'first', sourceId: 'source', state: 'Posted', status: 200 }
  const refused = { id: 'next', reopenedId: 'next', sourceId: 'source', state: 'Draft', status: 409, reason: 'unavailable' }
  exercise('competing-consumers', { sourceId: 'source', expectedReason: 'unavailable', attempts: [refused, winner] },
    [{ attempts: [winner, winner] }, { attempts: [winner, { ...refused, id: 'first', reopenedId: 'first' }] },
      { attempts: [winner, { ...refused, sourceId: 'another' }] }])
  const samples = [3, 2, 0].map((total, index) => ({ recordId: 'account', count: index + 1, status: 200,
    total, comparisonStatus: 200, comparisonTotal: total }))
  exercise('paged-total', { operation: 'GET /api/position', pageLimit: 2, recordId: 'account', expectedTotals: [3, 2, 0],
    samples, pageIds: ['a', 'b'], continuationIds: ['c'], lastId: 'c' },
  [{ continuationIds: ['b'] }, { lastId: 'missing' }, { samples: samples.slice(1) },
    { samples: [...samples.slice(0, 2), { ...samples[2], total: 2 }] }])
})
