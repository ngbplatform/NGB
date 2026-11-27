import assert from 'node:assert/strict'
import { isHistoricalReport } from './business-checks.mjs'
import { objectKeys } from './kit-contracts.mjs'

const text = value => typeof value === 'string' && value.trim().length > 0
const reviewedChecks = new Set([
  'effective-prerequisite', 'owner-change', 'dated-withdrawal', 'snapshot-stability', 'interval-overlap',
  'boundary-withdrawal', 'competing-consumers', 'query-growth', 'paged-total',
  'intermediate-balance', 'dependency-window', 'terminal-state', 'required-reason',
])

function lookupTypes(field) {
  if (field.lookup?.kind === 'catalog') return ['catalog:' + field.lookup.catalogType]
  if (field.lookup?.kind === 'document') return field.lookup.documentTypes.map(type => 'document:' + type)
  return []
}

function headerReferences(document) {
  return document.form?.sections?.flatMap(section => section.rows.flatMap(row => row.fields))
    .filter(field => lookupTypes(field).length) ?? []
}

export function reviewedPolicyGaps(rows, obligations, catalogs) {
  const missing = new Set()
  for (const target of catalogs) {
    if (!rows.some(row => row.target === target && Array.isArray(row.terminalStates))) {
      missing.add('Review terminal states and their historical reports for ' + target + '.')
    }
  }
  for (const obligation of obligations.filter(item => item.effect === 'write')) {
    const reasonTargets = obligation.reasonOperations?.flatMap(group => group.targets) ?? []
    for (const proof of obligation.proofs) {
      if (proof.kind === 'state-invariant' && proof.key === 'reason-required' && !reasonTargets.includes(proof.target)) {
        missing.add('Bind the declared reason requirement for ' + proof.target + ' in ' + obligation.id + ' to its operation.')
      }
    }
    if (!Array.isArray(obligation.reasonOperations)) {
      const targets = new Set(obligation.proofs.filter(proof => proof.target).map(proof => proof.target))
      if (targets.size > 1 || obligation.proofs.some(proof => proof.kind === 'required-reason')) {
        missing.add('Classify the business-reason scope of source clause ' + obligation.id + '.')
      }
    }
  }
  for (const { target, facts } of rows) {
    for (const fact of facts.filter(item => item.kind === 'consumes')) {
      if (!fact.position) missing.add('Classify the intermediate dated position for ' + target + '/' + fact.key + '.')
      if (fact.withdrawable && !fact.corrections) missing.add('Review the dependency window for corrections of ' + fact.source + '/' + fact.key + '.')
    }
  }
  return missing
}

// Compare the frozen source review with the public metadata of the running application.
export function verifyRuntimeOperationReview(rows, obligations, plan, inventory) {
  const documents = new Map(inventory.documents.map(doc => ['document:' + doc.documentType, doc]))
  const catalogs = (inventory.catalogs ?? []).map(item => 'catalog:' + item.catalogType)
  const missing = reviewedPolicyGaps(rows, obligations, catalogs)
  const proofs = obligations.flatMap(item => item.proofs)
  const writeTargets = [...documents.keys(), ...catalogs].filter(target => target !== 'document:general_journal_entry')
  for (const obligation of obligations) {
    if (obligation.reasonOperations?.length) {
      const applicable = obligation.reasonOperations.flatMap(item => item.targets)
      const excluded = (obligation.reasonExclusions ?? []).flatMap(item => item.targets)
      for (const target of writeTargets) {
        if (!applicable.includes(target) && !excluded.includes(target)) missing.add('Classify ' + target + ' for the business reason in ' + obligation.id + '.')
      }
      for (const target of [...applicable, ...excluded]) {
        if (!plan.surfaces.includes(target)) missing.add('Unknown business-reason target ' + target + ' in ' + obligation.id + '.')
      }
    }
  }
  for (const { target, facts } of rows) {
    for (const fact of facts.filter(item => item.kind === 'consumes')) {
      const source = documents.get(fact.source)
      const consumer = documents.get(target)
      if (!source || !consumer || consumer.capabilities?.canEditDraft === false) continue
      for (const field of headerReferences(consumer)) {
        if (field.isReadOnly || field.readOnlyWhenStatusIn?.includes(1)) continue
        const types = lookupTypes(field)
        for (const origin of headerReferences(source)) {
          if (!lookupTypes(origin).some(type => types.includes(type))) continue
          const covered = proofs.some(proof => proof.kind === 'prerequisite-scope' && proof.target === target
            && proof.expect?.prerequisite === fact.key && proof.expect.source === fact.source
            && proof.expect.consumerField === field.key && proof.expect.sourceField === origin.key)
          if (!covered) missing.add(`Exercise the shared prerequisite reference: ${target}/${field.key}, ${fact.source}/${origin.key} (${fact.key}).`)
        }
      }
    }
  }
  for (const row of rows) {
    for (const state of row.terminalStates ?? []) {
      if (!plan.surfaces.includes(state.consumer) || (state.report !== null && !plan.surfaces.includes(state.report))) {
        missing.add('Use a registered consumer and historical report for the terminal state ' + row.target + '/' + state.key + '.')
      }
    }
  }

  for (const report of inventory.reports ?? []) {
    const target = 'report:' + report.reportCode
    const fields = report.dataset?.fields ?? []
    if (isHistoricalReport(plan, report)) {
      const references = new Set(fields.flatMap(lookupTypes))
      for (const row of rows.filter(item => references.has(item.target))) {
        for (const terminal of row.terminalStates ?? []) {
          const covered = proofs.some(proof => proof.kind === 'terminal-state' && proof.target === row.target
            && proof.expect?.state === terminal.state && proof.expect.report === target)
          if (!covered && !terminal.unaffectedReports?.some(item => item.report === target)) {
            missing.add('Exercise the historical cutoff after ' + row.target + '/' + terminal.state + ' in ' + target + '.')
          }
        }
      }
    }
    if (!plan.cases.some(entry => entry.target === target && entry.scenario === 'historical-classification')) continue
    const linkedDocuments = new Set(fields.flatMap(lookupTypes).filter(type => type.startsWith('document:')))
    function hasPostingProof(writer) {
      return proofs.some(proof => proof.kind === 'snapshot-stability' && proof.target === writer
        && proof.expect?.report === target && proof.expect.until === writer
        && fields.some(field => field.code === proof.expect.groupField && field.isGroupable !== false))
    }
    for (const writer of linkedDocuments) {
      const context = proofs.some(proof => proof.kind === 'classification-context' && proof.target === target
        && proof.expect?.document === writer && proof.expect.contributor !== writer
        && plan.surfaces.includes(proof.expect.contributor)
        && (proof.expect.contributor.startsWith('catalog:') || hasPostingProof(proof.expect.contributor)))
      if (!context && !hasPostingProof(writer)) {
        missing.add(`Exercise the historical report group before posting ${writer} to ${target}.`)
      }
    }
  }
  assert.equal(missing.size, 0, [...missing].join('\n'))
}

// This inventory comes from the brief before the author classifies the implementation.
export function verifyOperationReview(rows, obligations, plan) {
  assert.ok(Array.isArray(rows), 'Review the public business operations in ai/operation-review.json before capture.')
  const reviewed = new Set()
  const covered = new Map()
  const boundProofs = new Set()
  for (const row of rows) {
    objectKeys(row, ['target', 'operation', 'requirement', 'facts'], ['reason', 'terminalStates'])
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
    if (row.terminalStates !== undefined) {
      assert.ok(Array.isArray(row.terminalStates), 'List the terminal states in this operation.')
      for (const state of row.terminalStates) {
        objectKeys(state, ['key', 'state', 'consumer', 'report'], ['unaffectedReports'])
        assert.ok(text(state.key) && text(state.state) && /^(?:document|catalog):[^\s:]+$/.test(state.consumer)
          && (state.report === null || /^report:[^\s:]+$/.test(state.report)), 'Bind a terminal state to the affected operation and report.')
        check('terminal-state', state.key, { state: state.state, consumer: state.consumer, report: state.report })
        for (const item of state.unaffectedReports ?? []) {
          objectKeys(item, ['report', 'reason'])
          assert.ok(/^report:[^\s:]+$/.test(item.report) && item.report !== state.report && text(item.reason),
            'Explain reports which only display a contextual reference to the terminal record.')
        }
      }
    }
    const keys = new Set()
    for (const fact of row.facts) {
      assert.ok(text(fact.key) && !keys.has(fact.key), 'Give each fact a distinct key within the operation.')
      keys.add(fact.key)
      classes.add(fact.kind)
      if (fact.kind === 'consumes') {
        objectKeys(fact, ['kind', 'key', 'source', 'owner', 'effectiveAt', 'editableOwner', 'withdrawable'], ['replaceable', 'position', 'corrections'])
        assert.match(fact.source, fact.withdrawable ? /^document:[^\s:]+$/ : /^(?:document|catalog):[^\s:]+$/)
        assert.ok(text(fact.owner) && text(fact.effectiveAt) && typeof fact.editableOwner === 'boolean'
          && typeof fact.withdrawable === 'boolean', 'Review the source, owner, effective date and correction rights.')
        check('effective-prerequisite', fact.key, { source: fact.source, owner: fact.owner, effectiveAt: fact.effectiveAt })
        if (fact.position !== undefined) {
          assert.ok(['bounded', 'eligibility'].includes(fact.position), 'Classify a depleted balance or a dated eligibility rule.')
          if (fact.position === 'bounded') check('intermediate-balance', fact.key, { source: fact.source })
        }
        if (fact.corrections !== undefined) {
          assert.ok(Array.isArray(fact.corrections) && (!fact.withdrawable || fact.corrections.some(item => item.operation === 'unpost')),
            'Classify unposting and each supported edit of the prerequisite.')
          const operations = new Set()
          for (const correction of fact.corrections) {
            objectKeys(correction, ['operation', 'cardinality'])
            assert.ok(text(correction.operation) && !operations.has(correction.operation)
              && ['single', 'multiple'].includes(correction.cardinality), 'Review the dependency count for each distinct correction.')
            operations.add(correction.operation)
            if (correction.cardinality === 'multiple') {
              check('dependency-window', fact.key + '/' + correction.operation,
                { consumer: row.target, operation: correction.operation }, fact.source)
            }
          }
        }
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
    assert.ok(requirement.reasonExclusions === undefined || (requirement.reasonOperations?.length
      && Array.isArray(requirement.reasonExclusions)), 'Bind scope exclusions to a reviewed business reason.')
    if (requirement.reasonOperations !== undefined) {
      assert.ok(Array.isArray(requirement.reasonOperations), 'List the operations requiring a business reason.')
      const included = new Set()
      for (const group of requirement.reasonOperations) {
        objectKeys(group, ['term', 'operation', 'targets'])
        assert.ok(text(group.term) && requirement.quote.includes(group.term) && text(group.operation)
          && Array.isArray(group.targets) && group.targets.length, 'Map each term from the source to its concrete operations.')
        for (const target of group.targets) {
          assert.match(target, /^(?:document|catalog|navigation):[^\s:]+$/)
          included.add(target)
          const proof = requirement.proofs.find(item => item.kind === 'required-reason' && item.target === target
            && item.expect?.operation === group.operation)
          assert.ok(proof, 'Test the required reason for ' + target + '/' + group.operation + ' in ' + requirement.id)
          boundProofs.add(proof)
        }
      }
      for (const group of requirement.reasonExclusions ?? []) {
        objectKeys(group, ['reason', 'targets'])
        assert.ok(text(group.reason) && Array.isArray(group.targets) && group.targets.length, 'Explain unrelated business-reason targets.')
        for (const target of group.targets) {
          assert.ok(!included.has(target), 'Conflicting business-reason scope: ' + target)
          included.add(target)
        }
      }
    }
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
