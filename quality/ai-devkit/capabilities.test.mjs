import assert from 'node:assert/strict'
import test from 'node:test'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { verifyCapabilities } from '../../ai-devkit/capabilities.mjs'
import { inspectCapabilities, preparePayload } from '../../ai-devkit/prepare.mjs'

function fixture() {
  const id = 'NGB.Platform.Example:NGB.Example.Contract'
  const hash = 'a'.repeat(64)
  const snapshots = [{ package: 'NGB.Platform.Example', version: '3.4.0',
    types: [{ name: 'NGB.Example.Contract', contractSha256: hash, methods: ['Run'] }] }]
  const files = new Map([
    ['skills/ngb-example/SKILL.md', 'Skill'], ['references/example.md', 'Reference'],
    ['examples/ExampleTests.cs', 'void Expected_result()'],
    ['known-limitations.json', '{"limitations":[{"id":"example.unsupported"}]}'],
  ])
  const capability = { id: 'example.run', name: 'Run example', category: 'example', operation: 'operate',
    support: 'supported', layer: 'runtime', constraints: ['Bounded execution'], prerequisites: ['Registered contract'],
    contracts: [{ id, sha256: hash, members: ['Run'] }], skills: ['ngb-example'], references: ['references/example.md'],
    validators: ['tests.discovery'], acceptance: [{ id: 'example.run.acceptance', outcome: 'The expected result is observed.',
      kind: 'integration', source: 'examples/ExampleTests.cs', test: 'Expected_result',
      cases: ['ExampleTests.Expected_result'] }] }
  const registry = { schemaVersion: 1, version: '3.4.0', surfaceSha256: digest(JSON.stringify([[id, hash]])),
    capabilities: [capability], classifications: [{ contract: id, sha256: hash, kind: 'extension',
      reason: 'Explicit public contribution contract.', capabilities: ['example.run'] }] }
  return { registry, snapshots, files, capability }
}

test('capability readiness requires complete classifications and input-bound executable acceptance', () => {
  const { registry, snapshots, files, capability } = fixture()
  const run = evidence => verifyCapabilities(registry, snapshots, files, evidence)
  const pending = run()
  assert.equal(pending.status, 'not-ready')
  const evidence = { schemaVersion: 1, inputsSha256: pending.inputsSha256,
    results: [{ id: 'example.run.acceptance', state: 'passed', reportSha256: 'b'.repeat(64) }] }
  assert.equal(run(evidence).status, 'passed')
  evidence.results[0].state = 'failed'
  assert.equal(run(evidence).results[0].state, 'failed')
  evidence.results[0].state = 'not-run'
  assert.equal(run(evidence).status, 'not-ready')
  assert.throws(() => run({ ...evidence, inputsSha256: 'c'.repeat(64) }), /stale/)
  delete capability.acceptance[0].source
  delete capability.acceptance[0].test
  delete capability.acceptance[0].cases
  assert.equal(run({ ...evidence, inputsSha256: run().inputsSha256,
    results: [{ ...evidence.results[0], state: 'passed' }] }).status, 'not-ready')
  registry.classifications = []
  assert.equal(run().unclassified.length, 1)
})

test('unsupported operations require a known limitation and a real negative acceptance path', () => {
  const { registry, snapshots, files, capability } = fixture()
  capability.support = 'unsupported'
  capability.limitation = 'example.unsupported'
  assert.equal(verifyCapabilities(registry, snapshots, files).results[0].support, 'unsupported')
  capability.acceptance[0].kind = 'compile'
  assert.throws(() => verifyCapabilities(registry, snapshots, files), /negative acceptance/)
  capability.acceptance[0].kind = 'integration'
  capability.limitation = 'unknown'
  assert.throws(() => verifyCapabilities(registry, snapshots, files), /Unknown capability limitation/)
  delete capability.limitation
  assert.throws(() => verifyCapabilities(registry, snapshots, files), /documented limitation/)
})

test('browser acceptance identifies its project and title without a fabricated C# method name', () => {
  const { registry, snapshots, files, capability } = fixture()
  const check = capability.acceptance[0]
  check.kind = 'browser'
  check.cases = ['["","Expected_result"]']
  assert.equal(verifyCapabilities(registry, snapshots, files).status, 'not-ready')
  for (const name of ['{}', 'null', '["Expected_result"]', '[null,"Expected_result"]',
    '["","Unrelated"]', '[ "", "Expected_result" ]', 'not-json']) {
    check.cases = [name]
    assert.throws(() => verifyCapabilities(registry, snapshots, files))
  }
})

test('capability contracts reject drift, invented members, missing guidance and ambiguous evidence', () => {
  const mutations = [
    ({ registry }) => { registry.schemaVersion = 2 },
    ({ snapshots }) => { snapshots[0].version = '3.3.0' },
    ({ snapshots }) => { snapshots.push(snapshots[0]) },
    ({ snapshots }) => { snapshots[0].types.push(snapshots[0].types[0]) },
    ({ registry }) => { registry.surfaceSha256 = 'changed' },
    ({ registry }) => { registry.capabilities.push(registry.capabilities[0]) },
    ({ capability }) => { capability.operation = 'execute-shell' },
    ({ capability }) => { capability.support = 'assumed' },
    ({ capability }) => { capability.name = '' },
    ({ capability }) => { capability.validators = [] },
    ({ capability }) => { capability.skills = ['ngb-missing'] },
    ({ capability }) => { capability.references = ['missing.md'] },
    ({ capability }) => { capability.contracts[0].members = ['Invented'] },
    ({ capability }) => { capability.contracts[0].sha256 = 'changed' },
    ({ capability }) => { capability.contracts.push(capability.contracts[0]) },
    ({ capability }) => { capability.acceptance[0].source = 'missing.cs' },
    ({ capability }) => { capability.acceptance[0].test = 'Missing_test' },
    ({ capability }) => { capability.acceptance[0].cases = [] },
    ({ capability }) => { capability.acceptance[0].cases = ['Unrelated.Test'] },
    ({ capability }) => { capability.acceptance[0].cases.push(capability.acceptance[0].cases[0]) },
    ({ capability }) => { capability.limitation = 'example.unsupported' },
    ({ registry }) => { registry.classifications.push(registry.classifications[0]) },
    ({ registry }) => { registry.classifications[0].sha256 = 'changed' },
    ({ registry }) => { registry.classifications[0].kind = 'ignored' },
    ({ registry }) => { registry.classifications[0].capabilities = ['missing'] },
    ({ registry }) => { registry.classifications[0].capabilities = [] },
  ]
  for (const mutate of mutations) {
    const value = fixture()
    mutate(value)
    assert.throws(() => verifyCapabilities(value.registry, value.snapshots, value.files))
  }
  const { registry, snapshots, files } = fixture()
  const pending = verifyCapabilities(registry, snapshots, files)
  const outcome = { id: 'example.run.acceptance', state: 'passed', reportSha256: 'b'.repeat(64) }
  for (const results of [[outcome, outcome], [{ ...outcome, id: 'unknown' }], [{ ...outcome, state: 'skipped' }],
    [{ ...outcome, reportSha256: 'invalid' }]]) {
    assert.throws(() => verifyCapabilities(registry, snapshots, files, {
      schemaVersion: 1, inputsSha256: pending.inputsSha256, results,
    }))
  }
})

test('the actual registry cannot claim acceptance without current executable evidence', async () => {
  const payload = await preparePayload()
  const result = inspectCapabilities(payload)
  assert.equal(result.version, payload.version)
  assert.ok(result.capabilities > 0)
  assert.equal(result.classifiedContracts + result.unclassified.length, result.publicContracts)
  assert.equal(result.passedCapabilities, 0)
  assert.equal(result.status, 'not-ready')
  const corrupted = structuredClone(payload)
  corrupted.files.find(file => file.path.endsWith('/contracts/ui.json')).content += ' '
  assert.throws(() => inspectCapabilities(corrupted), /integrity/)
})
