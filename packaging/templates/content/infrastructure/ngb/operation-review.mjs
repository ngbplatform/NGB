import assert from 'node:assert/strict'
import { objectKeys } from './kit-contracts.mjs'

const text = value => typeof value === 'string' && value.trim().length > 0
const reviewedChecks = new Set([
  'effective-prerequisite', 'owner-change', 'dated-withdrawal', 'snapshot-stability', 'interval-overlap',
  'boundary-withdrawal', 'competing-consumers', 'query-growth', 'paged-total',
])

// This inventory comes from the brief before the author classifies the implementation.
export function verifyOperationReview(rows, obligations, plan) {
  assert.ok(Array.isArray(rows), 'Review the public business operations in ai/operation-review.json before capture.')
  const reviewed = new Set()
  const covered = new Map()
  const boundProofs = new Set()
  for (const row of rows) {
    objectKeys(row, ['target', 'operation', 'requirement', 'facts'], ['reason'])
    assert.match(row.target, /^(?:document|catalog|report|navigation|job):[^\s:]+$/)
    const id = JSON.stringify([row.target, row.operation, row.requirement])
    const requirement = obligations.find(item => item.id === row.requirement)
    assert.ok(text(row.operation) && !reviewed.has(id) && requirement, 'Bind each distinct operation to its source obligation.')
    reviewed.add(id)
    assert.ok(Array.isArray(row.facts), 'Classify the facts used by the public operation.')
    assert.ok(row.facts.length ? row.reason === undefined : text(row.reason),
      'Explain which operation has no applicable business facts.')
    if (!covered.has(row.target)) {
      covered.set(row.target, new Set())
    }
    const classes = covered.get(row.target)
    function check(kind, key, expected = {}, target = row.target) {
      const proof = requirement.proofs.find(item => item.kind === kind && item.target === target && item.key === key)
      assert.ok(proof, `Review requires ${kind} for ${target}/${key} in ${row.requirement}.`)
      boundProofs.add(proof)
      for (const [field, value] of Object.entries(expected)) {
        assert.deepEqual(proof.expect?.[field], value, `Bind ${field} to the reviewed ${kind} proof for ${key}.`)
      }
    }
    const keys = new Set()
    for (const fact of row.facts) {
      assert.ok(text(fact.key) && !keys.has(fact.key), 'Give each fact a distinct key within the operation.')
      keys.add(fact.key)
      classes.add(fact.kind)
      if (fact.kind === 'consumes') {
        objectKeys(fact, ['kind', 'key', 'source', 'owner', 'effectiveAt', 'editableOwner', 'withdrawable'], ['replaceable'])
        assert.match(fact.source, fact.withdrawable ? /^document:[^\s:]+$/ : /^(?:document|catalog):[^\s:]+$/)
        assert.ok(text(fact.owner) && text(fact.effectiveAt) && typeof fact.editableOwner === 'boolean'
          && typeof fact.withdrawable === 'boolean', 'Review the source, owner, effective date and correction rights.')
        check('effective-prerequisite', fact.key, { source: fact.source, owner: fact.owner, effectiveAt: fact.effectiveAt })
        if (fact.editableOwner) {
          check('owner-change', fact.key, { owner: fact.owner })
        }
        if (fact.withdrawable) {
          assert.equal(typeof fact.replaceable, 'boolean', 'Classify whether another source can replace the withdrawn prerequisite.')
          check('dated-withdrawal', fact.key, { consumer: row.target, replaceable: fact.replaceable }, fact.source)
        }
      } else if (fact.kind === 'snapshot') {
        objectKeys(fact, ['kind', 'key', 'from', 'until'])
        assert.ok(text(fact.from) && text(fact.until) && fact.from !== fact.until, 'Name the distinct capture and consumption phases.')
        check('snapshot-stability', fact.key, { from: fact.from, until: fact.until })
      } else if (fact.kind === 'exclusive') {
        objectKeys(fact, ['kind', 'key', 'correctable'], ['releaseTarget', 'boundary'])
        assert.equal(typeof fact.correctable, 'boolean', 'Classify correction of the interval boundary.')
        check('interval-overlap', fact.key)
        if (fact.correctable) {
          assert.match(fact.releaseTarget, /^document:[^\s:]+$/)
          assert.ok(['inclusive', 'exclusive-end'].includes(fact.boundary), 'Declare the endpoint convention of the corrected interval.')
          check('boundary-withdrawal', fact.key, { boundary: fact.boundary }, fact.releaseTarget)
        }
      } else if (fact.kind === 'manual-price') {
        objectKeys(fact, ['kind', 'key'])
        assert.ok(requirement.proofs.some(proof => proof.target === row.target && proof.scenario === 'pricing'
          && proof.outcome === 'posted-override:' + row.target.slice('document:'.length)),
        'Bind the authorized manual override and its persisted reason to the pricing case.')
      } else if (fact.kind === 'contended') {
        objectKeys(fact, ['kind', 'key', 'scope'])
        assert.ok(['record', 'shared-source'].includes(fact.scope), 'Identify what the competing writes share.')
        assert.ok(requirement.proofs.some(proof => proof.target === row.target && proof.scenario === 'concurrent-operation'
          && proof.outcome === 'competing-requests'), 'Bind a competing write to its own target and source obligation: ' + row.target)
        if (fact.scope === 'shared-source') {
          check('competing-consumers', fact.key)
        }
      } else if (fact.kind === 'rows' || fact.kind === 'history-total') {
        objectKeys(fact, ['kind', 'key'])
        check(fact.kind === 'rows' ? 'query-growth' : 'paged-total', fact.key, { operation: row.operation })
      } else {
        assert.fail('Unknown reviewed operation fact: ' + fact.kind)
      }
    }
  }
  for (const requirement of obligations) {
    for (const proof of requirement.proofs) {
      if (reviewedChecks.has(proof.kind)) {
        assert.ok(boundProofs.has(proof), 'Review the business fact independently of the test plan: ' + proof.key)
      }
    }
  }
  if (!plan) return
  const surfaces = new Set(plan.surfaces ?? plan.documents.map(row => 'document:' + row.type))
  for (const target of covered.keys()) assert.ok(surfaces.has(target) || (target.startsWith('job:')
    && plan.businessChecks.some(check => check.target === target)), 'Unknown reviewed surface: ' + target)
  for (const doc of plan.documents) {
    if (doc.type === 'general_journal_entry') continue
    assert.ok(covered.has('document:' + doc.type), 'Review each application document independently: ' + doc.type)
  }
  for (const entry of plan.cases ?? []) {
    if (entry.scenario === 'settlement') assert.ok(covered.get(entry.target)?.has('history-total'),
      'Exercise the settlement total across its history page boundary: ' + entry.target)
    if (entry.scenario === 'concurrent-operation') assert.ok(covered.get(entry.target)?.has('contended'),
      'Bind each contended operation to the reviewed brief: ' + entry.target)
    const document = plan.documents.find(doc => entry.target === 'document:' + doc.type)
    if (entry.scenario === 'pricing' && document?.asynchronous !== false && !document?.synchronousPriceReason
      && plan.cases.some(other => other.target === entry.target && other.scenario === 'lines')) {
      const reviewedPrice = obligations.some(item => item.proofs.some(proof => proof.scenario === 'pricing'
        && proof.target === entry.target && proof.outcome === 'revised-price'))
      const measured = rows.some(row => row.target === entry.target && row.operation === entry.priceOperation
        && row.facts.some(fact => fact.kind === 'rows'))
      assert.ok(reviewedPrice && measured, 'Measure row growth for the priced editor request observed in the browser: ' + entry.target)
    }
  }
}
