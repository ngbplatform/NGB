import assert from 'node:assert/strict'
import { isDashboardMenuItem } from './acceptance-outcomes.mjs'
import { businessDateFormat, validateBusinessTarget, validateClassificationContext, validateExportCells, validateGrowthBudget,
  validateHistoricalControl, validatePageTotals, validatePrerequisiteScope, validateValueFields, validateWithdrawalState } from './business-checks.mjs'
import { reviewedPolicyGaps, verifyOperationReview, verifyRuntimeOperationReview } from './operation-review.mjs'
import { readAsset } from './file-data.mjs'
import { digest, distinctPaths, objectKeys } from './kit-contracts.mjs'
import { applyAssets } from './kit-files.mjs'

const binding = check => JSON.stringify([check.kind, check.target, check.key])
const obligationPath = 'ai/business-obligations.json'
const sourceListPath = 'ai/brief-sources.json'
const baselinePath = 'ai/business-obligations.lock.json'
const contextPath = 'ai/brief-context.json'
const reviewPath = 'ai/operation-review.json'
const tracePath = 'ai/requirement-trace.json'
const writeChecks = new Set(['source-membership', 'withdrawal-cycle', 'effective-prerequisite', 'dated-withdrawal',
  'intermediate-balance', 'dependency-window', 'terminal-state', 'required-reason',
  'state-invariant', 'history-conflict', 'interval-overlap', 'snapshot-stability', 'owner-change', 'prerequisite-scope', 'boundary-withdrawal', 'competing-consumers'])
const expectedFields = {
  'intermediate-balance': ['source', 'dates', 'change', 'opening', 'funded', 'expectedReason'],
  'dependency-window': ['operation', 'consumer', 'pageSize', 'expectedReason', 'expectedBefore', 'expectedAfterControl'],
  'terminal-state': ['state', 'consumer', 'report', 'historicalState', 'laterState', 'reverseReason', 'useReason'],
  'required-reason': ['operation', 'suppliedReason', 'expectedReason'],
  'prerequisite-scope': ['source', 'prerequisite', 'consumerField', 'sourceField', 'policy', 'expectedBefore', 'expectedAfter'],
  'classification-context': ['document', 'contributor', 'expectedGroups', 'expectedWithContributor'],
  'historical-cutoff': ['expected', 'laterExpected', 'timeZone'],
  'query-growth': ['valueFields'],
  'report-export': ['expectedCells'],
  'dated-withdrawal': ['expectedControl'],
  'paged-total': ['pageLimit', 'expectedTotals', 'operation'],
}

function verifyBriefCoverage(path, source, excerpts) {
  const ranges = []
  for (const quote of new Set(excerpts)) {
    let start = source.indexOf(quote)
    while (start !== -1) {
      const end = start + quote.length
      ranges.push([start, end])
      start = source.indexOf(quote, end)
    }
  }
  ranges.sort(([left], [right]) => left - right)
  let covered = 0
  for (const [start, end] of [...ranges, [source.length, source.length]]) {
    const gap = source.slice(covered, start)
    const missing = gap.search(/[\p{L}\p{N}]/u)
    if (missing >= 0) {
      const line = source.slice(0, covered + missing).split('\n').length
      assert.fail(`Unreviewed brief text at ${path}:${line}: ${gap.slice(missing, missing + 120).trim()}. Bind it to an obligation or explain its context in ${contextPath}.`)
    }
    covered = Math.max(covered, end)
  }
}

async function readObligations(root) {
  const original = await readAsset(root, obligationPath)
  assert.ok(original, 'Capture ai/business-obligations.json from the brief before generating the plan. See server-acceptance.md.')
  const obligations = JSON.parse(original)
  assert.ok(Array.isArray(obligations) && obligations.length, 'Capture the individual obligations from the original brief.')
  const sourceListText = await readAsset(root, sourceListPath)
  assert.ok(sourceListText, 'List every authoritative brief and appendix in ai/brief-sources.json before capture.')
  const sourcePaths = JSON.parse(sourceListText)
  assert.ok(Array.isArray(sourcePaths) && sourcePaths.length > 0, 'List the reviewed brief source paths.')
  distinctPaths(sourcePaths)
  const sources = new Map()
  for (const path of sourcePaths) {
    assert.match(path, /\.(?:md|txt)$/i, 'Use a Markdown or text source for the brief.')
    const source = await readAsset(root, path)
    assert.ok(source !== null, 'Missing declared brief source: ' + path)
    sources.set(path, source)
  }
  const ids = new Set()
  for (const item of obligations) {
    objectKeys(item, ['id', 'source', 'quote', 'effect', 'proofs'], ['reasonOperations', 'reasonExclusions'])
    assert.ok(typeof item.id === 'string' && item.id.trim() && !ids.has(item.id), 'Use a distinct obligation ID.')
    ids.add(item.id)
    assert.ok(sources.has(item.source), 'Obligation cites a source outside ai/brief-sources.json: ' + item.source)
    assert.ok(typeof item.quote === 'string' && item.quote.trim() && sources.get(item.source)?.includes(item.quote),
      'Cannot find the cited requirement in the original brief: ' + item.id)
    assert.ok(Array.isArray(item.proofs) && item.proofs.length, 'Specify observable proof for ' + item.id)
    assert.ok(['write', 'read', 'presentation', 'recovery', 'operations'].includes(item.effect),
      'Classify the required effect from the source brief: ' + item.id)
    for (const proof of item.proofs) {
      if (proof.scenario) objectKeys(proof, ['scenario', 'target', 'outcome'])
      else if (proof.integrationTest) objectKeys(proof, ['integrationTest'])
      else {
        objectKeys(proof, ['kind', 'target', 'key'], ['stores', 'expect', 'budget'])
        validateBusinessTarget(proof.kind, proof.target)
      }
      for (const field of ['scenario', 'target', 'outcome', 'integrationTest', 'kind', 'key']) {
        if (field in proof) assert.ok(typeof proof[field] === 'string' && proof[field].trim(), 'Missing proof ' + field + ' for ' + item.id)
      }
      if ('stores' in proof) {
        assert.ok(proof.kind === 'restore-rehearsal' && Array.isArray(proof.stores) && proof.stores.includes('database')
          && proof.stores.every(store => ['database', 'identity', 'attachments'].includes(store))
          && new Set(proof.stores).size === proof.stores.length, 'Specify the distinct stores required for recovery.')
      }
      if ('expect' in proof) {
        assert.ok(proof.expect && typeof proof.expect === 'object' && !Array.isArray(proof.expect) && Object.keys(proof.expect).length,
          'Specify concrete observations from the brief.')
      }
      if (Object.hasOwn(expectedFields, proof.kind)) {
        for (const field of expectedFields[proof.kind]) {
          assert.ok(proof.expect && Object.hasOwn(proof.expect, field), 'Capture the independent fixture expectation ' + field + ' for ' + item.id)
        }
        if (proof.kind === 'historical-cutoff') {
          validateHistoricalControl(proof.expect.expected, proof.expect.laterExpected)
          if (proof.expect.timeZone !== null) businessDateFormat(proof.expect.timeZone)
        }
        if (proof.kind === 'paged-total') validatePageTotals(proof.expect)
        if (proof.kind === 'query-growth') validateValueFields(proof.expect.valueFields)
        if (proof.kind === 'report-export') validateExportCells(proof.expect.expectedCells)
        if (proof.kind === 'dated-withdrawal') validateWithdrawalState(proof.expect.expectedControl)
        if (proof.kind === 'prerequisite-scope') validatePrerequisiteScope(proof.expect)
        if (proof.kind === 'classification-context') validateClassificationContext(proof.expect)
      }
      if (proof.kind === 'query-growth') validateGrowthBudget(proof.budget)
      else assert.ok(!('budget' in proof), 'Only query-growth obligations carry a row-growth budget.')
      if (proof.kind === 'snapshot-stability' && proof.expect?.report !== undefined) {
        assert.ok(/^report:[^\s:]+$/.test(proof.expect.report) && proof.expect.until === proof.target
          && typeof proof.expect.groupField === 'string' && proof.expect.groupField.trim()
          && [proof.expect.expected, proof.expect.freshExpected].every(pair =>
            Array.isArray(pair) && pair.length === 2 && pair.every(Number.isFinite)),
        'Pin the report, classification field, consuming document and two group totals for both snapshots.')
      }
    }
    if (item.effect === 'write') {
      assert.ok(item.proofs.some(proof => writeChecks.has(proof.kind) || (proof.scenario && proof.target.startsWith('document:')
        && ((proof.scenario === 'pricing' && (proof.outcome === 'revised-price'
          || proof.outcome === 'posted-override:' + proof.target.slice('document:'.length)))
          || proof.outcome === 'persisted-retry:' + proof.target.slice('document:'.length)
          || (proof.outcome.startsWith('command:' + proof.target.slice('document:'.length) + '/') && proof.outcome.split('/').at(-1))))), 'A write obligation needs a verified mutation and persisted readback: ' + item.id)
    }
    if (item.effect === 'recovery') {
      assert.ok(item.proofs.some(proof => proof.kind === 'restore-rehearsal' && proof.stores),
        'Bind the declared storage scope to a restore rehearsal: ' + item.id)
    }
  }
  const contextText = await readAsset(root, contextPath)
  const context = contextText === null ? [] : JSON.parse(contextText)
  assert.ok(Array.isArray(context), 'List the reviewed non-requirement excerpts.')
  const exclusionHeadings = new Set(context.filter(item => item?.category === 'out-of-scope')
    .map(item => JSON.stringify([item.source, item.quote])))
  for (const item of context) {
    objectKeys(item, ['source', 'quote', 'reason'], ['category'])
    assert.ok(typeof item.quote === 'string' && item.quote.trim() && sources.get(item.source)?.includes(item.quote)
      && typeof item.reason === 'string' && item.reason.trim(), 'Explain each exact context excerpt from the reviewed brief.')
    const quote = item.quote.trim()
    const lines = sources.get(item.source).split(/\r?\n/u)
    const heading = /^#{1,6} [^\n]+$/u.test(quote) && lines.includes(quote)
    if (!heading) {
      const listItem = /^(?:[-*+] |\d+[.)] )/u.test(quote)
      // A repeated bullet can occur under both an exclusion and a required workflow.
      let section = ''
      const clause = quote.replace(/^(?:[-*+] |\d+[.)] )/u, '')
      const excludedListItem = listItem && item.category === 'out-of-scope' && lines.every(line => {
        if (/^#{1,6}\s/u.test(line)) section = line.trim()
        return line.trim() !== quote || exclusionHeadings.has(JSON.stringify([item.source, section]))
          || obligations.some(rule => rule.source === item.source && rule.quote.includes(clause))
      })
      assert.ok(['background', 'definition', 'out-of-scope'].includes(item.category)
        && !/[\r\n]/u.test(quote) && (!listItem || excludedListItem)
        && !/\b(?:must|shall|required|mandatory)\b/iu.test(quote),
      'Capture each required clause as an obligation. Context must be a heading or one classified non-requirement.')
      // A substring of a mandatory sentence must not turn that sentence into background.
      assert.ok(lines.some(line => line.trim() === quote),
        'Classify one complete source line as context.')
    }
  }
  for (const [path, source] of sources) {
    verifyBriefCoverage(path, source, [...obligations, ...context].filter(item => item.source === path).map(item => item.quote))
  }
  const reviewText = await readAsset(root, reviewPath)
  const review = reviewText && JSON.parse(reviewText)
  verifyOperationReview(review, obligations)
  const fingerprints = {
    operationReviewSha256: digest(reviewText),
    sourceListSha256: digest(sourceListText),
    obligationsSha256: digest(original),
    sources: Object.fromEntries([...sources].map(([path, value]) => [path, digest(value)])),
    ...(contextText === null ? {} : { contextSha256: digest(contextText) }),
  }
  const baseline = { ...fingerprints, baselineSha256: digest(JSON.stringify(fingerprints)) }
  if (process.env.NGB_OBLIGATIONS_SHA256) {
    assert.equal(baseline.baselineSha256, process.env.NGB_OBLIGATIONS_SHA256, 'The reviewed brief or business obligations changed.')
  }
  return { obligations, review, baseline, snapshot: { [reviewPath]: reviewText, ...Object.fromEntries(sources), [sourceListPath]: sourceListText, [obligationPath]: original, [contextPath]: contextText } }
}

export async function captureObligations(root, apply) {
  const { baseline, snapshot, review, obligations } = await readObligations(root)
  const catalogs = new Set(review.filter(row => row.target.startsWith('catalog:')).map(row => row.target))
  const gaps = reviewedPolicyGaps(review, obligations, catalogs)
  assert.equal(gaps.size, 0, [...gaps].join('\n'))
  const current = await readAsset(root, baselinePath)
  const incoming = JSON.stringify(baseline, null, 2) + '\n'
  if (apply && current !== incoming) {
    await applyAssets(root, { ...snapshot, [baselinePath]: current }, { [baselinePath]: incoming }, digest(incoming))
  }
  return baseline
}

// A frozen schema can exercise migrations without establishing that an earlier
// application release exists. Only report a release upgrade against its named artifact.
export async function verifyApplicationUpgrade(root, observations) {
  const raw = await readAsset(root, 'ai/application-upgrade.json')
  assert.ok(raw, 'Declare the application release baseline in ai/application-upgrade.json.')
  const entry = JSON.parse(raw)
  const identity = value => typeof value === 'string' && value.trim().length > 0
  assert.ok(identity(entry.currentRelease), 'Identify the application release being tested.')
  if (entry.previousRelease === null) {
    objectKeys(entry, ['currentRelease', 'previousRelease', 'reason'])
    assert.ok(identity(entry.reason), 'Explain why a supported previous application release is unavailable.')
    assert.equal(observations.length, 0, 'Release upgrade observations conflict with the unavailable baseline.')
    return { status: 'unavailable', ...entry }
  }

  objectKeys(entry, ['currentRelease', 'previousRelease', 'sourceManifest', 'sourceManifestSha256', 'test'])
  assert.ok(identity(entry.previousRelease) && entry.previousRelease !== entry.currentRelease,
    'Identify distinct source and target application releases.')
  assert.ok(identity(entry.test), 'Bind the release upgrade to its integration test.')
  assert.match(entry.sourceManifestSha256, /^[a-f0-9]{64}$/)
  const manifest = await readAsset(root, entry.sourceManifest)
  assert.ok(manifest && digest(manifest) === entry.sourceManifestSha256, 'Previous application release manifest differs from its pinned digest.')
  assert.equal(observations.length, 1, 'Require one release upgrade observation from this integration run.')
  const [{ test, proof }] = observations
  assert.equal(test, entry.test, 'The release upgrade observation belongs to a different test.')
  for (const key of ['previousRelease', 'currentRelease', 'sourceManifestSha256']) {
    assert.equal(proof[key], entry[key], 'The observed release upgrade differs from its declaration: ' + key)
  }
  for (const status of [proof.seedStatus, proof.readStatus]) {
    assert.ok(Number.isInteger(status) && status >= 200 && status < 300, 'Seed the prior application and reopen its record after the upgrade.')
  }
  assert.ok(identity(proof.recordId) && proof.recordId === proof.reopenedId, 'Reopen the same business record.')
  assert.ok(proof.before && typeof proof.before === 'object' && !Array.isArray(proof.before) && Object.keys(proof.before).length,
    'Capture persisted business values before the upgrade.')
  assert.deepEqual(proof.after, proof.before, 'The application upgrade changed the preserved business state.')
  return { status: 'verified', ...entry, recordId: proof.recordId }
}

// Capture the source-to-proof inventory before implementation. An independent
// requirement binding survives removing a rule and its test from the plan.
export async function verifyRequirementTrace(root, plan, passedTests, observations, inventory) {
  if (plan.schemaVersion === 1) {
    const inputs = await Promise.all([sourceListPath, obligationPath, baselinePath, tracePath, contextPath, reviewPath].map(path => readAsset(root, path)))
    assert.ok(!plan.documents?.length && !process.env.NGB_OBLIGATIONS_SHA256 && inputs.every(input => input === null),
      'Use schema 2 for application documents or business obligations. Only the empty starter uses schema 1.')
    return
  }
  const raw = await readAsset(root, tracePath)
  assert.ok(raw, 'Map the original business brief to executable evidence in ai/requirement-trace.json.')
  const trace = JSON.parse(raw)
  objectKeys(trace, ['requirements', 'documentRules'])
  assert.ok(Array.isArray(trace.requirements), 'List the original requirements.')
  assert.deepEqual(trace.requirements.map(row => row.id).sort(), plan.requirements.map(row => row.id).sort(),
    'Reconcile the source requirements with ai/acceptance.json.')
  const { obligations, review, baseline } = await readObligations(root)
  const captured = await readAsset(root, baselinePath)
  assert.ok(captured, 'Run ai capture --apply on the reviewed obligations before implementation.')
  assert.deepEqual(JSON.parse(captured), baseline, 'The source brief or obligations differ from the reviewed baseline.')
  assert.deepEqual(obligations.map(item => item.id).sort(), trace.requirements.map(row => row.id).sort(),
    'Retain each original business obligation in the trace and acceptance plan.')
  verifyOperationReview(review, obligations, plan)
  if (inventory) verifyRuntimeOperationReview(review, obligations, plan, inventory)
  const sources = new Map()
  const checked = new Set()
  const reviewedBindings = new Set(obligations.flatMap(item => item.proofs.filter(proof => proof.kind).map(binding)))
  const declared = new Map(plan.businessChecks.map(check => [binding(check), check]))
  for (const row of trace.requirements) {
    objectKeys(row, ['id', 'source', 'quote', 'checks'], ['integrationTests', 'browserOnlyReason'])
    assert.match(row.source, /\.(?:md|txt)$/i, 'Reference the original text of the business brief.')
    if (!sources.has(row.source)) sources.set(row.source, await readAsset(root, row.source))
    assert.ok(typeof row.quote === 'string' && row.quote.trim() && sources.get(row.source)?.includes(row.quote),
      `Cannot find the cited requirement in ${row.source}: ${row.id}`)
    assert.ok(Array.isArray(row.checks), 'List the required server observations.')
    const requirement = plan.requirements.find(item => item.id === row.id)
    const obligation = obligations.find(item => item.id === row.id)
    assert.ok(row.source === obligation.source && row.quote === obligation.quote, 'Keep the original obligation and its exact source excerpt: ' + row.id)
    for (const proof of obligation.proofs) {
      if (proof.scenario) {
        assert.ok(plan.cases.some(entry => requirement.cases.includes(entry.id) && entry.target === proof.target
          && entry.scenario === proof.scenario && entry.outcomes.includes(proof.outcome)), 'Missing required browser outcome for ' + row.id)
      } else if (proof.integrationTest) {
        assert.ok(row.integrationTests?.includes(proof.integrationTest), 'Missing required integration test for ' + row.id)
      } else {
        assert.ok(row.checks.some(check => binding(check) === binding(proof)), 'Missing required server proof for ' + row.id + ': ' + binding(proof))
        if (proof.kind === 'query-growth') {
          assert.deepEqual(declared.get(binding(proof))?.budget, proof.budget, 'Keep the reviewed query-growth sizes and budget for ' + row.id)
        }
        if (proof.stores) {
          assert.deepEqual([...(declared.get(binding(proof))?.stores ?? [])].sort(), [...proof.stores].sort(),
            'Rehearse the complete recovery scope from the original brief.')
        }
        if (proof.expect && observations) {
          const actual = observations.find(item => binding(item.proof) === binding(proof))
          for (const [field, expected] of Object.entries(proof.expect)) {
            assert.deepEqual(actual?.proof[field], expected, 'Unexpected observed ' + field + ' for ' + row.id)
          }
        }
      }
    }
    const tests = row.integrationTests ?? []
    assert.ok(Array.isArray(tests) && tests.every(name => typeof name === 'string' && name.trim())
      && tests.length === new Set(tests).size, 'Bind distinct integration test names.')
    if (passedTests) {
      for (const name of tests) assert.ok(passedTests.has(name), `Missing passed integration test for ${row.id}: ${name}`)
    }
    if (!row.checks.length && !tests.length) {
      assert.ok(requirement.cases.length && typeof row.browserOnlyReason === 'string' && row.browserOnlyReason.trim(),
        `Explain browser-only coverage or bind real server checks: ${row.id}`)
    }
    for (const check of row.checks) {
      objectKeys(check, ['kind', 'target', 'key'], ['workingSet'])
      const id = binding(check)
      if (Object.hasOwn(expectedFields, check.kind)) {
        assert.ok(reviewedBindings.has(id),
          'Capture the fixture expectations or growth budget in the independent brief review: ' + id)
      }
      assert.ok(declared.has(id), `Missing required server check for ${row.id}: ${id}`)
      if (check.workingSet !== undefined) {
        assert.ok(check.workingSet === true && check.kind === 'capacity' && declared.get(id).budget.maxWorkingSetBytes,
          'A memory requirement must bind a working-set budget.')
      }
      checked.add(id)
    }
  }
  assert.deepEqual([...checked].sort(), [...declared.keys()].sort(), 'Trace every server observation to a source requirement.')

  const documents = plan.documents.map(row => row.type).filter(type => type !== 'general_journal_entry')
  assert.ok(Array.isArray(trace.documentRules), 'Classify the dated business rules for each application document.')
  assert.deepEqual(trace.documentRules.map(row => row.type).sort(), documents.sort(), 'Review every application document.')
  for (const row of trace.documentRules) {
    objectKeys(row, ['type', 'effective-prerequisite', 'interval-overlap', 'snapshot-stability'])
    for (const kind of ['effective-prerequisite', 'interval-overlap', 'snapshot-stability']) {
      const rules = row[kind]
      const keys = plan.businessChecks.filter(check => check.target === 'document:' + row.type && check.kind === kind)
        .map(check => check.key).sort()
      if (Array.isArray(rules)) {
        assert.ok(rules.length, `Bind business keys or explain why ${kind} does not apply to ${row.type}.`)
        assert.deepEqual([...rules].sort(), keys, `Unverified ${kind} rule for ${row.type}.`)
      } else {
        objectKeys(rules, ['reason'])
        assert.ok(!keys.length && typeof rules.reason === 'string' && rules.reason.trim(),
          `Explain why ${kind} does not apply to ${row.type}.`)
      }
    }
  }
}

export function verifyPresentationInventory(plan, inventory, icons) {
  assert.ok(Array.isArray(icons) && icons.includes('home'), 'Missing SDK menu icon catalog.')
  const isDashboard = item => isDashboardMenuItem(item, plan.cases)
  if (plan.workflows?.dashboards?.length) {
    assert.ok(inventory.menuGroups.some(group => group.items.some(isDashboard)), 'Register the required dashboard in the main menu.')
  }
  for (const group of inventory.menuGroups) {
    assert.ok(group.icon == null || icons.includes(group.icon), `Unknown menu group icon: ${group.label}/${group.icon}`)
    for (const item of group.items) {
      assert.ok(item.icon == null || icons.includes(item.icon), `Unknown menu item icon: ${item.code}/${item.icon}`)
      if (!isDashboard(item)) continue
      assert.ok(inventory.menuGroups[0] === group && group.items.length === 1 && group.label === item.label,
        'Register Dashboard as the first standalone root link.')
      assert.equal(group.icon, 'home', `Set the Dashboard group icon to home: ${group.label}.`)
      assert.equal(item.icon, 'home', `Set the Dashboard item icon to home: ${item.code}.`)
      assert.ok(plan.cases.some(entry => entry.target === 'navigation:' + item.code
        && entry.outcomes.includes('dashboard-home')), 'Verify the rendered home icon and dashboard destination.')
      for (const size of ['desktop', 'mobile']) {
        assert.ok(plan.cases.some(entry => entry.target === 'navigation:' + item.code
          && entry.outcomes.includes('dashboard-layout:' + size)), `Missing ${size} dashboard layout proof.`)
      }
      assert.ok(plan.cases.some(entry => entry.target === 'navigation:' + item.code
        && entry.outcomes.includes('business-calendar')), 'Verify the dashboard business date across client time zones.')
      assert.ok(plan.cases.some(entry => entry.target === 'navigation:' + item.code
        && entry.outcomes.includes('dashboard-context')), 'Verify the visible period or scope in the dashboard header.')
    }
  }
  for (const report of inventory.reports) {
    const fields = report.dataset?.fields ?? []
    for (const field of fields) {
      const display = field.code.endsWith('_id')
        ? fields.find(other => other.code === field.code.slice(0, -3) + '_display')
        : undefined
      if (!display && !(field.lookup && ['uuid', 'guid'].includes(field.dataType?.toLowerCase()))) continue
      assert.ok(!field.isSelectable && !field.isGroupable, `Hide the supporting ID in ${report.reportCode}/${field.code}.`)
      if (!display?.isSelectable) continue
      assert.ok(plan.cases.some(entry => entry.target === 'report:' + report.reportCode
        && entry.outcomes.includes('report-reference:' + display.code)),
      `Verify the visible reference and its destination: ${report.reportCode}/${display.code}`)
    }
  }
}
