import assert from 'node:assert/strict'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'

export const reviewSchema = {
  type: 'object', additionalProperties: false,
  required: ['approved', 'reason', 'reviewedFiles', 'integrationClasses', 'upgradeActionsVerified'],
  properties: {
    approved: { type: 'boolean' }, reason: { type: 'string' },
    reviewedFiles: { type: 'array', items: { type: 'string' } },
    integrationClasses: { type: 'array', items: { type: 'string' } },
    upgradeActionsVerified: { type: 'boolean' },
  },
}

export function integrationTestInventory(files) {
  const inventory = []
  for (const [path, bytes] of Object.entries(files).sort(([left], [right]) => left.localeCompare(right, 'en'))) {
    if (!/^EquipmentInspection\.IntegrationTests\/.+\.cs$/.test(path)) continue
    const text = bytes.toString('utf8')
    if (!/\[(?:Fact|Theory)(?:\(|\])/.test(text)) continue
    assert.match(text, /namespace\s+EquipmentInspection\.IntegrationTests\s*[;{]/)
    const classes = [...text.matchAll(/public\s+(?:(?:sealed|partial)\s+)*class\s+([A-Za-z_][A-Za-z0-9_]*)/g)]
    assert.equal(classes.length, 1, `Automatic test selection requires one public test class per source: ${path}`)
    inventory.push({ path, className: `EquipmentInspection.IntegrationTests.${classes[0][1]}` })
  }
  return inventory
}

export function sourceReviewFailure(events, fallback) {
  const result = events.findLast(event => event?.type === 'result')
  if (!result || (result.subtype === 'success' && !result.is_error)) return fallback
  const reasons = Array.isArray(result.errors) ? result.errors.filter(error => typeof error === 'string') : []
  const cost = Number.isFinite(result.total_cost_usd) ? ` Estimated cost: $${result.total_cost_usd}.` : ''
  return `Source reviewer did not complete (${result.subtype}): ${reasons.join('; ') || 'No successful verdict.'}${cost}`
}

export function validateIntegrationClasses(names, files) {
  assert.ok(Array.isArray(names), 'Missing reviewed integration test selection.')
  assert.equal(new Set(names).size, names.length, 'Duplicate integration test selection.')
  const inventory = integrationTestInventory(files)
  const knownClasses = new Set(inventory.map(entry => entry.className))
  for (const name of names) assert.ok(knownClasses.has(name), `Unknown integration class: ${name}`)
  return inventory
}

export function integrationBatches(names, files, before) {
  const inventory = validateIntegrationClasses(names, files)
  const added = new Set(inventory.filter(entry => !Object.hasOwn(before, entry.path)).map(entry => entry.className))
  return [
    { name: 'integration-new', classes: names.filter(name => added.has(name)) },
    { name: 'integration', classes: names.filter(name => !added.has(name)) },
  ].filter(batch => batch.classes.length > 0)
}

export function reviewSelection(answer, changed, files, producedTreeSha256, upgrade = false) {
  assert.ok(answer?.approved === true, `Independent source review rejected the output: ${answer?.reason ?? 'missing verdict'}`)
  assert.equal(typeof answer.reason, 'string')
  assert.ok(answer.reason.length > 0 && answer.reason.length <= 4096)
  assert.deepEqual([...answer.reviewedFiles].sort(), [...changed].sort(), 'The reviewer must assess every changed source file.')
  const testFiles = changed.filter(path => /^EquipmentInspection\.(?:IntegrationTests|Tests)\/.+\.cs$/.test(path))
  const changedClasses = new Set()
  for (const { path, className } of validateIntegrationClasses(answer.integrationClasses, files)) {
    if (changed.includes(path)) changedClasses.add(className)
  }
  for (const name of changedClasses) assert.ok(answer.integrationClasses.includes(name), `Changed tests omitted: ${name}`)
  if (upgrade) assert.equal(answer.upgradeActionsVerified, true, 'Review must confirm preview, conflict resolution and absence of deployment from the transcript.')
  else assert.ok(testFiles.length > 0, 'The task requires application-authored tests.')
  if (testFiles.some(path => path.startsWith('EquipmentInspection.IntegrationTests/'))) {
    assert.ok(answer.integrationClasses.length > 0, 'Changed integration helpers require a regression test selection.')
  }
  return { producedTreeSha256, reviewedTestFiles: testFiles.sort(),
    reviewedFrontendFiles: changed.filter(path => /^web\/tests\/.+\.ts$/.test(path)).sort(),
    integrationClasses: [...answer.integrationClasses].sort(), review: answer }
}

export function commandInventory(transcript, client) {
  const events = transcript.trim().split('\n').filter(Boolean).map(line => JSON.parse(line))
  if (client === 'codex') {
    return events.filter(event => event.type === 'item.completed' && event.item.type === 'command_execution')
      .map(({ item }) => {
        const command = { command: item.command, exitCode: item.exit_code }
        if (item.exit_code && /\bdotnet test\b/.test(item.command)
          && /SocketException \(13\): Permission denied/.test(item.aggregated_output)
          && /TestPlatform\.CommunicationUtilities\.SocketServer\.Start\(/.test(item.aggregated_output)
          && /Test Run Aborted\./.test(item.aggregated_output)) {
          command.diagnostic = 'VSTest reported that its local socket listener was denied and the test run aborted.'
        }
        return command
      })
  }
  assert.equal(client, 'claude')
  return events.flatMap(event => event.type === 'assistant' ? event.message.content : [])
    .filter(item => item.type === 'tool_use').map(item => ({ tool: item.name, input: item.input }))
}

export function sourceTree(files) {
  return digest(JSON.stringify(Object.entries(files).map(([path, bytes]) => [path, digest(bytes)]).sort()))
}

export function verifySourceReview(artifacts, acceptance, family, inputs) {
  const json = path => JSON.parse(artifacts[path])
  const profile = json('source-review-profile.json')
  const review = json('source-review.json')
  const selection = json('source-review-selection.json')
  const raw = json('raw-acceptance.json')
  assert.equal(digest(artifacts['source-review-profile.json']), inputs.acceptanceSupport['source-review-profile.json'])
  assert.equal(review.profileSha256, inputs.acceptanceSupport['source-review-profile.json'])
  assert.equal(review.promptSha256, digest(artifacts['source-review-prompt.txt']))
  assert.equal(review.clientVersion, profile.clientVersion)
  assert.equal(review.status, 'passed')
  assert.equal(review.producedTreeSha256, acceptance.producedTreeSha256)
  assert.equal(review.selectionSha256, digest(artifacts['source-review-selection.json']))
  assert.equal(selection.producedTreeSha256, acceptance.producedTreeSha256)
  assert.equal(selection.review.approved, true)
  const events = artifacts['source-review-transcript.jsonl'].toString('utf8').trim().split('\n').map(line => JSON.parse(line))
  const result = events.findLast(event => event.type === 'result')
  const init = events.find(event => event.type === 'system' && event.subtype === 'init')
  assert.deepEqual(init?.mcp_servers, [])
  assert.equal(init.model, review.model)
  assert.ok(result?.subtype === 'success' && !result.is_error)
  assert.deepEqual(result.structured_output, selection.review)
  assert.equal(result.total_cost_usd, review.estimatedCostUsd)
  assert.ok(Number.isFinite(review.estimatedCostUsd) && review.estimatedCostUsd >= 0
    && review.estimatedCostUsd <= profile.maximumEstimatedCostUsd)
  const calls = events.flatMap(event => event.type === 'assistant' ? event.message.content : []).filter(item => item.type === 'tool_use')
  assert.ok(calls.every(call => call.name === 'StructuredOutput'))
  for (const key of ['status', 'producedTreeSha256', 'runnerSha256', 'oracleSha256', 'candidateSha256']) {
    assert.equal(raw[key], acceptance[key], `Raw independent acceptance differs: ${key}`)
  }
  for (const [path, hash] of Object.entries(raw.artifacts)) assert.equal(acceptance.artifacts[path], hash)
  if (family === 'upgrade') {
    assert.equal(selection.review.upgradeActionsVerified, true)
    assert.deepEqual([...raw.checks].sort(), ['custom-content-preserved', 'coordinated-upgrade', 'both-agent-adapters',
      'exact-official-content', 'trusted-preview-read-only', 'clean-locked-builds', 'installed-kit-integrity', 'source-fixture-unchanged'].sort())
  } else {
    assert.deepEqual(raw.checks, acceptance.checks)
    assert.deepEqual(raw.authoredTests, acceptance.authoredTests)
    assert.equal(acceptance.authoredTests.reviewSha256, review.selectionSha256)
  }
}
