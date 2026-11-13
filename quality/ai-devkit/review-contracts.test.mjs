import assert from 'node:assert/strict'
import test from 'node:test'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { commandInventory, integrationBatches, integrationTestInventory, reviewSchema, reviewSelection, sourceReviewFailure, sourceTree, validateIntegrationClasses, verifySourceReview } from './review-contracts.mjs'

const testPath = 'EquipmentInspection.IntegrationTests/CatalogTests.cs'
const className = 'EquipmentInspection.IntegrationTests.CatalogTests'
const testSource = 'namespace EquipmentInspection.IntegrationTests; public sealed class CatalogTests { [Fact] public void Works() {} }'
const tree = digest('source tree')
const verdict = { approved: true, reason: 'Reviewed source and tests.', reviewedFiles: [testPath],
  integrationClasses: [className], upgradeActionsVerified: false }

test('new classes run first and every selected regression class runs exactly once', () => {
  const newPath = 'EquipmentInspection.IntegrationTests/NewTests.cs'
  const newClass = 'EquipmentInspection.IntegrationTests.NewTests'
  const files = { [testPath]: testSource, [newPath]: testSource.replaceAll('CatalogTests', 'NewTests') }
  const before = { [testPath]: digest(testSource) }
  assert.deepEqual(integrationBatches([className, newClass], files, before), [
    { name: 'integration-new', classes: [newClass] }, { name: 'integration', classes: [className] },
  ])
  assert.deepEqual(integrationBatches([className], files, before), [{ name: 'integration', classes: [className] }])
  assert.deepEqual(integrationBatches([], files, before), [])
})

test('review context indexes trusted integration classes without copying unchanged test sources', () => {
  const otherPath = 'EquipmentInspection.IntegrationTests/OtherTests.cs'
  const files = { [otherPath]: Buffer.from(testSource.replaceAll('CatalogTests', 'OtherTests')),
    [testPath]: Buffer.from(testSource + '\n// unrelated test context'.repeat(10000)),
    'EquipmentInspection.IntegrationTests/Helper.cs': Buffer.from('helper'), 'other.cs': Buffer.from(testSource) }
  assert.deepEqual(integrationTestInventory(files), [
    { path: testPath, className },
    { path: otherPath, className: 'EquipmentInspection.IntegrationTests.OtherTests' },
  ])
  assert.ok(Buffer.byteLength(JSON.stringify(integrationTestInventory(files))) < 512)
})

test('shared test helper changes can select the complete discovered suite beyond 32 classes', () => {
  const files = Object.fromEntries(Array.from({ length: 64 }, (_, index) => [
    `EquipmentInspection.IntegrationTests/Tests${index}.cs`, Buffer.from(testSource.replaceAll('CatalogTests', `Tests${index}`)),
  ]))
  const helper = 'EquipmentInspection.IntegrationTests/Requests.cs'
  files[helper] = Buffer.from('public static class Requests {}')
  const classes = integrationTestInventory(files).map(entry => entry.className)
  assert.equal(validateIntegrationClasses(classes, files).length, 64)
  const selection = reviewSelection({ ...verdict, reviewedFiles: [helper], integrationClasses: classes }, [helper], files, tree)
  assert.deepEqual(selection.integrationClasses, [...classes].sort())
  assert.throws(() => validateIntegrationClasses([...classes, classes[0]], files), /Duplicate/)
  assert.throws(() => validateIntegrationClasses([...classes, 'EquipmentInspection.IntegrationTests.UnknownTests'], files), /Unknown integration/)
  assert.throws(() => validateIntegrationClasses(null, files), /Missing reviewed/)
})

test('review failures preserve native budget and provider reasons without accepting incomplete verdicts', () => {
  const budget = { type: 'result', subtype: 'error_max_budget_usd', is_error: true,
    errors: ['Reached maximum budget ($2)'], total_cost_usd: 2.689424 }
  assert.equal(sourceReviewFailure([null, { type: 'system' }, budget], 'process failed'),
    'Source reviewer did not complete (error_max_budget_usd): Reached maximum budget ($2) Estimated cost: $2.689424.')
  assert.equal(sourceReviewFailure([], 'transport failed'), 'transport failed')
  assert.equal(sourceReviewFailure([budget, { type: 'result', subtype: 'success', is_error: false }], 'validation failed'), 'validation failed')
  assert.equal(sourceReviewFailure([{ type: 'result', subtype: 'success', is_error: true }], 'process failed'),
    'Source reviewer did not complete (success): No successful verdict.')
  assert.equal(sourceReviewFailure([{ type: 'result', subtype: 'error_during_execution', errors: [17, 'usage limit', 'retry later'] }], ''),
    'Source reviewer did not complete (error_during_execution): usage limit; retry later')
})

test('independent review selects every changed test class and rejects incomplete or unsafe verdicts', () => {
  const files = { [testPath]: Buffer.from(testSource), 'EquipmentInspection.IntegrationTests/Helper.cs': Buffer.from('helper'),
    'EquipmentInspection.Tests/UnitTests.cs': Buffer.from('unit'), 'web/tests/unit/catalog.ts': Buffer.from('test') }
  const changed = Object.keys(files)
  const answer = { ...verdict, reviewedFiles: changed }
  const selection = reviewSelection(answer, changed, files, tree)
  assert.equal(selection.producedTreeSha256, tree)
  assert.deepEqual(selection.reviewedFrontendFiles, ['web/tests/unit/catalog.ts'])
  assert.equal(selection.reviewedTestFiles.length, 3)
  assert.ok(reviewSchema.required.includes('approved'))
  assert.throws(() => reviewSelection(null, changed, files, tree), /missing verdict/)
  for (const patch of [
    { approved: false }, { reason: '' }, { reason: 'x'.repeat(4097) }, { reason: 3 }, { reviewedFiles: [] },
    { integrationClasses: [className, className] }, { integrationClasses: ['Unknown'] }, { integrationClasses: [] },
    { integrationClasses: null },
  ]) assert.throws(() => reviewSelection({ ...answer, ...patch }, changed, files, tree))
  assert.throws(() => reviewSelection(verdict, [testPath], { [testPath]: Buffer.from(testSource.replace('EquipmentInspection.IntegrationTests', 'Wrong')) }, tree))
  assert.throws(() => reviewSelection(verdict, [testPath], { [testPath]: Buffer.from(testSource + ' public class Other {}') }, tree))
  const helper = 'EquipmentInspection.IntegrationTests/Helper.cs'
  assert.throws(() => reviewSelection({ ...verdict, reviewedFiles: [helper], integrationClasses: [] }, [helper], files, tree), /helpers require/)
  assert.throws(() => reviewSelection({ ...verdict, reviewedFiles: [], integrationClasses: [] }, [], {}, tree), /authored tests/)
  assert.throws(() => reviewSelection({ ...verdict, reviewedFiles: [], integrationClasses: [] }, [], {}, tree, true), /confirm preview/)
  assert.deepEqual(reviewSelection({ ...verdict, reviewedFiles: [], integrationClasses: [], upgradeActionsVerified: true }, [], {}, tree, true).integrationClasses, [])
  assert.deepEqual(reviewSelection({ ...verdict, reviewedFiles: [], integrationClasses: [className], upgradeActionsVerified: true }, [], files, tree, true).integrationClasses, [className])
})

test('source tree identity is order independent and command evidence excludes prose claims', () => {
  const files = { b: Buffer.from('B'), a: Buffer.from('A') }
  assert.equal(sourceTree(files), sourceTree({ a: files.a, b: files.b }))
  assert.notEqual(sourceTree(files), sourceTree({ a: files.a }))
  const command = { type: 'item.completed', item: { type: 'command_execution', command: 'node cli preview', exit_code: 0 } }
  assert.deepEqual(commandInventory([JSON.stringify({ type: 'thread.started' }), JSON.stringify(command), ''].join('\n'), 'codex'),
    [{ command: 'node cli preview', exitCode: 0 }])
  const assistant = { type: 'assistant', message: { content: [{ type: 'text', text: 'claimed success' },
    { type: 'tool_use', name: 'Bash', input: { command: 'node cli preview' } }] } }
  assert.deepEqual(commandInventory([JSON.stringify({ type: 'system' }), JSON.stringify(assistant)].join('\n'), 'claude'),
    [{ tool: 'Bash', input: { command: 'node cli preview' } }])
  assert.throws(() => commandInventory('{}', 'unknown'))
  assert.throws(() => commandInventory('invalid', 'codex'))
  assert.deepEqual(commandInventory('', 'codex'), [])
})

test('review distinguishes the VSTest socket startup error from an ordinary failed test command', () => {
  const command = 'dotnet test Example.csproj --no-restore'
  const collect = (output, invoked = command, code = 1) => commandInventory(JSON.stringify({
    type: 'item.completed', item: { type: 'command_execution', command: invoked, exit_code: code, aggregated_output: output },
  }), 'codex')[0]
  const socket = 'System.Net.Sockets.SocketException (13): Permission denied'
  const frame = 'at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.SocketServer.Start(String endPoint)'
  const abort = 'Test Run Aborted.'
  const output = [socket, frame, abort].join('\n')
  assert.match(collect(output).diagnostic, /local socket listener was denied/)
  for (const incomplete of [abort, socket, [socket, frame].join('\n'), [socket, abort].join('\n'), 'Failed: 1, Passed: 5']) {
    assert.deepEqual(collect(incomplete), { command, exitCode: 1 })
  }
  assert.equal(collect(output, 'dotnet build').diagnostic, undefined)
  assert.equal(collect(output, command, 0).diagnostic, undefined)
})

function evidence(family = 'development') {
  const bytes = value => Buffer.from(JSON.stringify(value))
  const profile = { clientVersion: '2.1.308', maximumEstimatedCostUsd: 2 }
  const review = { ...verdict, upgradeActionsVerified: family === 'upgrade' }
  const selection = { producedTreeSha256: tree, review }
  const artifacts = { 'source-review-profile.json': bytes(profile), 'source-review-selection.json': bytes(selection),
    'source-review-prompt.txt': Buffer.from('Frozen independent review prompt') }
  const report = { status: 'passed', producedTreeSha256: tree, profileSha256: digest(artifacts['source-review-profile.json']),
    promptSha256: digest(artifacts['source-review-prompt.txt']), selectionSha256: digest(artifacts['source-review-selection.json']),
    clientVersion: profile.clientVersion, model: 'test-model', estimatedCostUsd: 0.5 }
  const events = [{ type: 'system', subtype: 'init', model: report.model, mcp_servers: [] },
    { type: 'assistant', message: { content: [{ type: 'text' }, { type: 'tool_use', name: 'StructuredOutput' }] } },
    { type: 'result', subtype: 'success', is_error: false, total_cost_usd: 0.5, structured_output: review }]
  artifacts['source-review.json'] = bytes(report)
  artifacts['source-review-transcript.jsonl'] = Buffer.from(events.map(value => JSON.stringify(value)).join('\n'))
  const raw = { status: 'passed', producedTreeSha256: tree, runnerSha256: digest('runner'), oracleSha256: digest('oracle'),
    candidateSha256: digest('candidate'), artifacts: { 'frozen/test-selection.json': report.selectionSha256 },
    authoredTests: { status: 'passed', reviewSha256: report.selectionSha256 }, checks: family === 'upgrade'
      ? ['custom-content-preserved', 'coordinated-upgrade', 'both-agent-adapters', 'exact-official-content',
        'trusted-preview-read-only', 'clean-locked-builds', 'installed-kit-integrity', 'source-fixture-unchanged']
      : [{ id: 'independent-test', state: 'passed' }] }
  artifacts['raw-acceptance.json'] = bytes(raw)
  const acceptance = structuredClone(raw)
  if (family === 'upgrade') acceptance.checks = [{ id: 'preview-read-only', state: 'passed' }]
  const inputs = { acceptanceSupport: { 'source-review-profile.json': report.profileSha256 } }
  return { artifacts, acceptance, inputs, events, report, raw, selection, bytes }
}

test('exported automatic reviews remain bound to raw model, tests, source and review profile', () => {
  for (const family of ['development', 'upgrade']) {
    const value = evidence(family)
    verifySourceReview(value.artifacts, value.acceptance, family, value.inputs)
  }
  const edits = [
    v => { v.artifacts['source-review-prompt.txt'] = Buffer.from('changed') },
    v => { v.report.profileSha256 = digest('wrong') },
    v => { v.report.status = 'failed' },
    v => { v.report.estimatedCostUsd = 3; v.events.at(-1).total_cost_usd = 3 },
    v => { v.report.estimatedCostUsd = -1; v.events.at(-1).total_cost_usd = -1 },
    v => { v.report.model = 'other' },
    v => { v.events[0].mcp_servers = ['untrusted'] },
    v => { v.events[1].message.content[1].name = 'Bash' },
    v => { v.events.at(-1).subtype = 'error' },
    v => { v.events.at(-1).is_error = true },
    v => { v.events.at(-1).structured_output = { approved: false } },
    v => { v.events.pop() },
    v => { v.events.shift() },
    v => { v.raw.producedTreeSha256 = digest('other') },
    v => { v.raw.artifacts['frozen/test-selection.json'] = digest('other') },
    v => { v.raw.checks = [] },
    v => { v.acceptance.authoredTests.reviewSha256 = digest('other') },
  ]
  for (const edit of edits) {
    const v = evidence()
    edit(v)
    v.artifacts['source-review.json'] = v.bytes(v.report)
    v.artifacts['raw-acceptance.json'] = v.bytes(v.raw)
    v.artifacts['source-review-transcript.jsonl'] = Buffer.from(v.events.map(value => JSON.stringify(value)).join('\n'))
    assert.throws(() => verifySourceReview(v.artifacts, v.acceptance, 'development', v.inputs))
  }
})
