import assert from 'node:assert/strict'
import { portablePath } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'

export function evaluationFamily(task) {
  if (task === 'skill-discovery') return 'discovery'
  if (task === 'customized-kit-upgrade') return 'upgrade'
  assert.ok(['new-catalog', 'existing-document-change', 'report-extension', 'permission-correction'].includes(task),
    'Unknown real-agent task.')
  return 'development'
}

export function validateAgentEvaluations(value, matrix, inputs) {
  assert.ok(value && value.schemaVersion === 1 && value.status === 'passed', 'Missing successful real-agent evaluation evidence.')
  assert.equal(matrix.version, inputs.version)
  assert.deepEqual(value.inputs, inputs, 'Agent evaluation inputs changed; repeat the affected evaluations.')
  assert.equal(value.profile, matrix.profile)
  const expected = matrix.clients.flatMap(client => matrix.workingDirectories.flatMap(directory =>
    matrix.tasks.map(task => `${client}/${directory}/${task.id}`))).sort()
  assert.ok(Array.isArray(value.attempts), 'Missing real-agent attempts.')
  assert.deepEqual(value.attempts.map(attempt => `${attempt.client}/${attempt.workingDirectory}/${attempt.task}`).sort(), expected,
    'Real-agent task discovery is incomplete or contains duplicate attempts.')
  for (const attempt of value.attempts) {
    const task = matrix.tasks.find(task => task.id === attempt.task)
    const evaluator = inputs.evaluators[attempt.client][evaluationFamily(attempt.task)]
    assert.ok(evaluator, 'The required client profile and runner are not implemented.')
    assert.equal(attempt.clientVersion, evaluator.clientVersion, 'The tested client version changed.')
    for (const key of ['profileSha256', 'runnerSha256', 'outputRedactorSha256']) {
      assert.match(evaluator[key], /^[a-f0-9]{64}$/)
      assert.equal(attempt[key], evaluator[key], `Agent evaluation ${key} changed.`)
    }
    assert.equal(attempt.attempt, matrix.attempts, 'Unexpected evaluation retry count.')
    assert.equal(attempt.platform, matrix.platform)
    assert.equal(attempt.architecture, matrix.architecture)
    assert.equal(attempt.humanAssistance, matrix.humanAssistance)
    assert.equal(attempt.state, 'passed', 'A required agent attempt did not pass.')
    assert.match(inputs.oracles[task.id] ?? '', /^[a-f0-9]{64}$/, 'Independent task oracle is not frozen.')
    assert.equal(attempt.oracleSha256, inputs.oracles[task.id], 'Independent acceptance oracle changed.')
    assert.ok(Number.isFinite(attempt.elapsedSeconds) && attempt.elapsedSeconds > 0
      && attempt.elapsedSeconds <= matrix.maximumSecondsPerAttempt, 'Agent evaluation exceeded its time budget.')
    for (const key of ['clientVersion', 'model', 'provider', 'configurationSha256', 'producedTreeSha256', 'oracleSha256']) {
      assert.ok(typeof attempt[key] === 'string' && attempt[key].length > 0 && attempt[key].length <= 256,
        `Missing agent evaluation identity: ${key}`)
      if (key.endsWith('Sha256')) assert.match(attempt[key], /^[a-f0-9]{64}$/)
    }
    assert.ok(Array.isArray(attempt.acceptance), 'Missing independent acceptance checks.')
    assert.deepEqual(attempt.acceptance.map(check => check.id).sort(), [...task.acceptance].sort(), 'Independent acceptance discovery differs from the frozen task.')
    assert.ok(attempt.acceptance.every(check => check.state === 'passed'), 'Critical agent invariants cannot be averaged against successful checks.')
    for (const artifact of [attempt.diff, attempt.transcript, attempt.acceptanceReport, attempt.fixtureManifest,
      attempt.configuration, attempt.profileSource, attempt.runnerSource, attempt.redactorSource]) {
      assert.ok(artifact, 'Missing agent-produced diff, transcript, fixture or acceptance evidence.')
      portablePath(artifact.path)
      assert.ok(artifact.path.startsWith(`attempts/${attempt.client}/${attempt.workingDirectory}/${attempt.task}/`),
        'Evaluation artifacts must stay in their unique attempt directory.')
      assert.match(artifact.sha256, /^[a-f0-9]{64}$/)
    }
  }
  return value
}
