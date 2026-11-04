import assert from 'node:assert/strict'
import test from 'node:test'
import { evaluationFamily, validateAgentEvaluations } from './evaluation-data.mjs'

const hash = 'a'.repeat(64)
const matrix = { version: '3.4.0', profile: 'test-v1', clients: ['codex', 'claude'], workingDirectories: ['root', 'subdirectory'],
  tasks: [{ id: 'skill-discovery', acceptance: ['versions', 'skill'] }], attempts: 1, maximumSecondsPerAttempt: 1800,
  platform: 'darwin', architecture: 'arm64', humanAssistance: 'none' }
const evaluator = { clientVersion: '1.2.3', profileSha256: hash, runnerSha256: hash, outputRedactorSha256: hash }
const inputs = { version: '3.4.0', matrixSha256: hash, kitSha256: hash, packages: { sdk: hash },
  oracles: { 'skill-discovery': hash },
  evaluators: { codex: { discovery: evaluator }, claude: { discovery: evaluator } } }
function evidence() {
  return { schemaVersion: 1, status: 'passed', inputs, profile: matrix.profile,
    attempts: matrix.clients.flatMap(client => matrix.workingDirectories.map(workingDirectory => ({
      client, workingDirectory, task: 'skill-discovery', attempt: 1, platform: 'darwin', architecture: 'arm64',
      humanAssistance: 'none', state: 'passed', elapsedSeconds: 10,
      clientVersion: '1.2.3', model: 'recorded-model', provider: 'test-provider', configurationSha256: hash,
      profileSha256: hash, runnerSha256: hash, outputRedactorSha256: hash,
      producedTreeSha256: hash, oracleSha256: hash, acceptance: [{ id: 'versions', state: 'passed' }, { id: 'skill', state: 'passed' }],
      ...Object.fromEntries(['diff', 'transcript', 'acceptanceReport', 'fixtureManifest',
        'configuration', 'profileSource', 'runnerSource', 'redactorSource'].map(name => [name,
        { path: `attempts/${client}/${workingDirectory}/skill-discovery/${name}.json`, sha256: hash }])),
    }))) }
}

test('saved evaluations require every client, context, task and independent invariant with exact relevant inputs', () => {
  assert.equal(validateAgentEvaluations(evidence(), matrix, inputs).attempts.length, 4)
  assert.throws(() => validateAgentEvaluations(null, matrix, inputs))
  assert.throws(() => validateAgentEvaluations(evidence(), { ...matrix, version: '4.0.0' }, inputs))
  assert.throws(() => validateAgentEvaluations(evidence(), matrix, { ...inputs, kitSha256: 'b'.repeat(64) }))
  const missingOracle = { ...inputs, oracles: { 'skill-discovery': null } }
  assert.throws(() => validateAgentEvaluations({ ...evidence(), inputs: missingOracle }, matrix, missingOracle), /oracle/)
  const missingEvaluator = { ...inputs, evaluators: { ...inputs.evaluators, codex: { discovery: null } } }
  assert.throws(() => validateAgentEvaluations({ ...evidence(), inputs: missingEvaluator }, matrix, missingEvaluator), /not implemented/)
  for (const key of ['profileSha256', 'runnerSha256', 'outputRedactorSha256']) {
    const invalidEvaluator = { ...inputs, evaluators: { ...inputs.evaluators,
      codex: { discovery: { ...evaluator, [key]: 'invalid' } } } }
    assert.throws(() => validateAgentEvaluations({ ...evidence(), inputs: invalidEvaluator }, matrix, invalidEvaluator))
  }
  for (const change of [
    value => { value.schemaVersion = 2 }, value => { value.status = 'not-run' }, value => { value.profile = 'other' },
    value => { value.attempts = null }, value => { value.attempts.pop() },
    value => { value.attempts.push(value.attempts[0]) },
    value => { value.attempts[0].attempt = 2 }, value => { value.attempts[0].platform = 'linux' },
    value => { value.attempts[0].architecture = 'x64' }, value => { value.attempts[0].humanAssistance = 'corrected-prompt' },
    value => { value.attempts[0].state = 'failed' }, value => { value.attempts[0].elapsedSeconds = 1801 },
    value => { value.attempts[0].elapsedSeconds = 0 }, value => { value.attempts[0].elapsedSeconds = '10' },
    value => { value.attempts[0].model = '' }, value => { value.attempts[0].model = 'x'.repeat(257) },
    value => { value.attempts[0].configurationSha256 = 'bad' },
    value => { value.attempts[0].clientVersion = '1.2.4' },
    value => { value.attempts[0].profileSha256 = 'b'.repeat(64) },
    value => { value.attempts[0].runnerSha256 = 'b'.repeat(64) },
    value => { value.attempts[0].outputRedactorSha256 = 'b'.repeat(64) },
    value => { value.attempts[0].redactorSource = null },
    value => { value.attempts[0].configuration = null },
    value => { value.attempts[0].oracleSha256 = 'b'.repeat(64) },
    value => { value.attempts[0].acceptance = null }, value => { value.attempts[0].acceptance.pop() },
    value => { value.attempts[0].acceptance[0].state = 'not-run' },
    value => { value.attempts[0].diff = null }, value => { value.attempts[0].diff.path = '../outside' },
    value => { value.attempts[0].diff.path = 'attempts/another-run/diff.txt' },
    value => { value.attempts[0].diff.sha256 = 'bad' },
  ]) {
    const value = evidence()
    change(value)
    assert.throws(() => validateAgentEvaluations(value, matrix, inputs))
  }
})

test('each task binds to its own discovery, development or upgrade implementation', () => {
  assert.equal(evaluationFamily('skill-discovery'), 'discovery')
  assert.equal(evaluationFamily('customized-kit-upgrade'), 'upgrade')
  for (const task of ['new-catalog', 'existing-document-change', 'report-extension', 'permission-correction']) {
    assert.equal(evaluationFamily(task), 'development')
  }
  assert.throws(() => evaluationFamily('unsupported'), /Unknown/)
})
