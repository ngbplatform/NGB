import assert from 'node:assert/strict'
import { mkdir, readFile, realpath, writeFile } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { readBytes } from '../../packaging/templates/content/infrastructure/ngb/kit-files.mjs'
import { loadCandidate } from '../../packaging/templates/content/infrastructure/ngb/application.mjs'
import { evaluationFamily, validateAgentEvaluations } from './evaluation-data.mjs'
import { evaluationInputs } from './evaluation-inputs.mjs'
import { requiresFrontendAcceptance } from './development-evidence.mjs'
import { redactAgentOutput } from './agent-output.mjs'

assert.ok([4, 5].includes(process.argv.length), 'Usage: node verify-agents.mjs <candidate> <new-evidence-directory> [saved-evaluation-directory]')
const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const candidatePath = await realpath(resolve(process.argv[2]))
const output = resolve(process.argv[3])
await mkdir(output)
const candidate = await loadCandidate(candidatePath)
const manifestBytes = await readFile(join(candidatePath, 'release-manifest.json'))
const { matrix, inputs } = await evaluationInputs(repository, candidate)
const report = { schemaVersion: 1, status: 'not-ready', artifactManifestSha256: digest(manifestBytes), inputs,
  profile: matrix.profile, expectedAttempts: matrix.clients.length * matrix.workingDirectories.length * matrix.tasks.length }
try {
  assert.ok(process.argv[4], 'Real Codex/Claude evaluations have not been supplied. No model is invoked by artifact packaging or this evidence verifier.')
  assert.ok(Object.values(inputs.oracles).every(Boolean), 'Independent frozen agent-task oracles must be implemented before evaluating or accepting saved results.')
  const saved = await realpath(resolve(process.argv[4]))
  const bytes = await readBytes(saved, 'evaluations.json', 8 * 1024 * 1024)
  assert.ok(bytes, 'Saved agent evaluation inventory is missing.')
  const evaluations = JSON.parse(bytes)
  validateAgentEvaluations(evaluations, matrix, inputs)
  report.evaluationSha256 = digest(bytes)
  report.files = { 'evaluations.json': digest(bytes) }
  await writeFile(join(output, 'evaluations.json'), bytes, { mode: 0o600 })
  const copyArtifact = async (artifact, transcript = false) => {
    const data = await readBytes(saved, artifact.path, 32 * 1024 * 1024)
    assert.ok(data && digest(data) === artifact.sha256, `Missing or changed evaluation artifact: ${artifact.path}`)
    assert.ok(!transcript || redactAgentOutput(data) === data.toString('utf8'),
      'Agent transcript does not satisfy the frozen redaction policy; do not export raw credentials.')
    if (Object.hasOwn(report.files, artifact.path)) {
      assert.equal(report.files[artifact.path], artifact.sha256, 'Conflicting evaluation artifact identities.')
    } else {
      report.files[artifact.path] = artifact.sha256
      await mkdir(dirname(join(output, artifact.path)), { recursive: true })
      await writeFile(join(output, artifact.path), data, { mode: 0o600, flag: 'wx' })
    }
    return data
  }
  for (const attempt of evaluations.attempts) {
    const family = evaluationFamily(attempt.task)
    for (const artifact of [attempt.diff, attempt.transcript, attempt.acceptanceReport, attempt.fixtureManifest,
      attempt.configuration, attempt.profileSource, attempt.runnerSource, attempt.redactorSource]) {
      const data = await copyArtifact(artifact, artifact === attempt.transcript)
      if (artifact === attempt.profileSource) assert.equal(digest(data), attempt.profileSha256)
      if (artifact === attempt.runnerSource) assert.equal(digest(data), attempt.runnerSha256)
      if (artifact === attempt.redactorSource) assert.equal(digest(data), attempt.outputRedactorSha256)
      if (artifact === attempt.configuration) {
        const configuration = JSON.parse(data)
        assert.equal(digest(JSON.stringify(configuration)), attempt.configurationSha256)
        assert.equal(configuration.profileSha256, attempt.profileSha256)
        if (attempt.client === 'claude' && family === 'upgrade') {
          const profile = JSON.parse(await readFile(join(repository, 'quality/ai-devkit/claude-upgrade-profile.json')))
          const installer = configuration.boundedInstaller
          assert.equal(installer?.generatorSha256, profile.installer.sourceSha256,
            'The bounded upgrade installer changed.')
          assert.equal(digest(installer.generator), profile.installer.sourceSha256)
          assert.equal(digest(await readFile(join(repository, profile.installer.source))), profile.installer.sourceSha256)
        }
      }
      if (artifact === attempt.fixtureManifest) {
        const fixture = JSON.parse(data)
        assert.equal(fixture.inputs.matrixSha256, inputs.matrixSha256)
        assert.equal(fixture.inputs.kitSha256, inputs.kitSha256)
        assert.deepEqual(fixture.inputs.artifactsSha256, inputs.artifactsSha256,
          'The attempt used different CLI, template or SDK/UI package bytes.')
        assert.equal(fixture.inputs.runnerSha256, attempt.runnerSha256)
        assert.equal(fixture.inputs.outputRedactorSha256, attempt.outputRedactorSha256)
        assert.equal(fixture.inputs.profileSha256, attempt.profileSha256)
        assert.equal(fixture.inputs.oracleSha256, attempt.oracleSha256)
        if (family !== 'discovery') {
          assert.equal(fixture.inputs.fixturePreparerSha256, inputs.fixturePreparers[family],
            'The application was prepared by a different fixture implementation.')
        }
      }
      if (artifact === attempt.acceptanceReport) {
        const acceptance = JSON.parse(data)
        assert.equal(acceptance.status, 'passed', 'Independent acceptance failed.')
        assert.deepEqual(acceptance.checks, attempt.acceptance, 'Acceptance output differs from the recorded evaluation.')
        assert.equal(acceptance.producedTreeSha256, attempt.producedTreeSha256, 'Acceptance tested a different agent output tree.')
        assert.equal(acceptance.oracleSha256, attempt.oracleSha256, 'Acceptance used a different independent oracle.')
        assert.equal(acceptance.runnerSha256, family === 'discovery' ? attempt.runnerSha256
          : inputs.acceptanceRunners[family], 'The independent acceptance runner changed.')
        if (family === 'development') {
          assert.equal(acceptance.authoredTests?.status, 'passed',
            'Development acceptance must execute the reviewed application-authored tests.')
          assert.match(acceptance.authoredTests.reviewSha256, /^[a-f0-9]{64}$/)
          assert.equal(acceptance.artifacts?.['frozen/development-evidence.mjs'],
            inputs.acceptanceSupport['development-evidence.mjs'], 'Authored frontend acceptance implementation changed.')
          if (requiresFrontendAcceptance(attempt.task)) {
            assert.equal(acceptance.authoredTests.frontend?.status, 'passed',
              'Document and permission tasks must pass the application frontend suites.')
            for (const name of ['unit', 'coverage', 'browser']) {
              assert.match(acceptance.artifacts?.[`authored-frontend-${name}.json`], /^[a-f0-9]{64}$/,
                'Authored frontend acceptance must retain every raw report.')
            }
          }
        }
        if (family !== 'discovery') {
          assert.ok(acceptance.artifacts && Object.keys(acceptance.artifacts).length > 0,
            'Independent acceptance must retain its raw evidence.')
          for (const [path, sha256] of Object.entries(acceptance.artifacts)) {
            const evidence = await copyArtifact({ path: `${dirname(artifact.path)}/${path}`, sha256 })
            if (family === 'development' && path === 'frozen/test-selection.json') {
              assert.equal(digest(evidence), acceptance.authoredTests.reviewSha256)
            }
          }
          if (family === 'development') {
            assert.ok(Object.hasOwn(acceptance.artifacts, 'frozen/test-selection.json'))
            assert.ok(Object.hasOwn(acceptance.artifacts, 'authored-unit.trx'))
          }
        }
      }
    }
  }
  report.status = 'passed'
} catch (error) {
  report.failure = error.message
  process.exitCode = 2
}
await writeFile(join(output, 'agents.json'), `${JSON.stringify(report, null, 2)}\n`, { mode: 0o600 })
console.log(`Real agent evaluations: ${report.status.toUpperCase()}. ${report.failure ?? ''}`)
