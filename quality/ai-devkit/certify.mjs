import assert from 'node:assert/strict'
import { randomUUID } from 'node:crypto'
import { copyFile, mkdir, mkdtemp, readFile, realpath, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { assetPaths, readBytes } from '../../packaging/templates/content/infrastructure/ngb/kit-files.mjs'
import { executionEnvironment, runTool } from '../../packaging/templates/content/infrastructure/ngb/process.mjs'
import { atomicJson, lockRun } from '../external-consumer/checkpoints.mjs'
import { certificationChecks, validateCertification } from './certification-data.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const profile = process.argv[2]
assert.ok(process.argv.length === 4 && Object.hasOwn(certificationChecks, profile),
  'Usage: node quality/ai-devkit/certify.mjs artifacts|contracts|application|upgrade|agents <sealed-candidate>')
const candidate = await realpath(resolve(process.argv[3]))
const manifestBytes = await readFile(join(candidate, 'release-manifest.json'))
const relativeOutput = `certification/ai-kit/${profile}/${randomUUID()}`
const output = join(candidate, relativeOutput)
const unlock = await lockRun(join(candidate, `ai-kit-${profile}.lock`))
const home = await realpath(await mkdtemp(join(tmpdir(), 'ngb-kit-certification-')))
const env = executionEnvironment(home)
const abort = new AbortController()
const cancel = () => abort.abort()
process.once('SIGINT', cancel)
process.once('SIGTERM', cancel)
const result = { schemaVersion: 1, profile, status: 'not-ready', artifactManifestSha256: digest(manifestBytes),
  startedAt: new Date().toISOString(), checks: [], files: {} }
const run = async (id, args) => {
  const processResult = await runTool('node', args, { cwd: repository, env, signal: abort.signal,
    timeoutMs: 3600000, maxBytes: 16 * 1024 * 1024 })
  await writeFile(join(output, `${id}.log`), processResult.output, { mode: 0o600 })
  result.checks.push({ id, state: processResult.state, exitCode: processResult.exitCode,
    reason: processResult.reason, elapsedMs: processResult.elapsedMs })
  console.log(`[${processResult.state}] ai-kit/${profile}/${id}`)
  return processResult.state === 'passed'
}
try {
  await mkdir(output, { recursive: true })
  const valid = await run('candidate.before', ['quality/external-consumer/release.mjs', 'verify', candidate])
  if (valid) {
    if (profile === 'artifacts') {
      await run('package-parity', ['quality/ai-devkit/verify-artifacts.mjs', candidate, join(output, 'artifacts.json')])
      await run('public-contracts', ['quality/ai-devkit/prepare-contracts.mjs', '--packages', candidate])
    } else if (profile === 'contracts') {
      if (await run('tooling', ['quality/ai-devkit/test-tooling.mjs'])) {
        await copyFile(join(repository, 'artifacts/ai-devkit/tooling.json'), join(output, 'tooling.json'))
      }
    } else if (profile === 'application') {
      await run('starter', ['quality/ai-devkit/verify-starter.mjs', candidate, join(output, 'starter')])
      await run('examples', ['quality/ai-devkit/verify-examples.mjs', '--packages', candidate, '--output', join(output, 'examples')])
      await run('capabilities', ['quality/ai-devkit/verify-capabilities.mjs', '--packages', candidate,
        '--examples', join(output, 'examples'), '--output', join(output, 'capabilities.json')])
    } else if (profile === 'upgrade') {
      await run('source-fixture', ['quality/upgrade-certification/verify-source.mjs'])
      await run('coordinated-upgrade', ['quality/ai-devkit/verify-upgrade.mjs', candidate, join(output, 'upgrade')])
      await run('failure-recovery', ['--test', 'quality/ai-devkit/transaction.test.mjs',
        'quality/ai-devkit/upgrade-process.test.mjs', 'quality/ai-devkit/resources.test.mjs'])
    } else {
      await run('evaluations', ['quality/ai-devkit/verify-agents.mjs', candidate, join(output, 'agents'),
        ...process.env.NGB_AI_EVALUATION_EVIDENCE ? [resolve(process.env.NGB_AI_EVALUATION_EVIDENCE)] : []])
    }
  }
  await run('candidate.after', ['quality/external-consumer/release.mjs', 'verify', candidate])
} catch (error) {
  result.failure = error.message
} finally {
  try {
    for (const id of ['candidate.before', ...certificationChecks[profile], 'candidate.after']) {
      if (!result.checks.some(check => check.id === id)) {
        result.checks.push({ id, state: 'not-run', exitCode: null, reason: 'A prerequisite failed or execution was cancelled.' })
      }
    }
    for (const path of await assetPaths(candidate, relativeOutput)) {
      result.files[path] = digest(await readBytes(candidate, path, 16 * 1024 * 1024))
    }
    if (!result.failure && result.checks.every(check => check.state === 'passed')) {
      result.status = 'passed'
      validateCertification(result, profile, digest(manifestBytes))
    }
    result.completedAt = new Date().toISOString()
    await atomicJson(join(candidate, `certification/ai-kit/${profile}.json`), result)
  } finally {
    process.removeListener('SIGINT', cancel)
    process.removeListener('SIGTERM', cancel)
    try {
      await rm(home, { recursive: true, force: true })
    } finally {
      await unlock()
    }
  }
}
if (result.status !== 'passed') process.exitCode = 2
console.log(`AI Kit ${profile}: ${result.status.toUpperCase()}.`)
