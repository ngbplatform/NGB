import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { mkdtemp, readFile, realpath, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { executionEnvironment, runTool } from '../../packaging/templates/content/infrastructure/ngb/process.mjs'

assert.equal(process.argv.length, 4, 'Usage: node verify-resource-recovery.mjs <generated-app> <new-report.json>')
const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const root = await realpath(resolve(process.argv[2]))
const output = resolve(process.argv[3])
const home = await realpath(await mkdtemp(join(tmpdir(), 'ngb-recovery-probe-')))
const env = executionEnvironment(home)
const cli = join(repository, 'packaging/templates/content/infrastructure/ngb.mjs')
const receipt = { schemaVersion: 1, status: 'not-ready', startedAt: new Date().toISOString(), checks: [] }
const child = spawn(process.execPath, [join(repository, 'quality/ai-devkit/fixtures/environment.mjs'), root], {
  cwd: root, env, shell: false, stdio: ['ignore', 'pipe', 'pipe'],
})
let result
let runId
let sentinel
let bytes = 0
const chunks = []
const collect = chunk => {
  bytes += chunk.length
  if (bytes > 1024 * 1024) child.kill('SIGTERM')
  else chunks.push(chunk)
}
child.stdout.on('data', collect)
child.stderr.on('data', collect)
const closed = new Promise(resolve => {
  child.once('error', error => { result = { error: error.code }; resolve(result) })
  child.once('close', (code, signal) => { result = { code, signal }; resolve(result) })
})
const timer = setTimeout(() => child.kill('SIGTERM'), 900000)
const docker = async args => {
  const response = await runTool('docker', args, { cwd: root, env, timeoutMs: 30000 })
  assert.equal(response.state, 'passed', response.output)
  return response.output.trim()
}
const recover = async apply => {
  const result = await runTool('node', [cli, 'recover', '--resources', '--app', root, '--format', 'json',
    ...apply ? ['--apply'] : []], { cwd: root, env, timeoutMs: 120000 })
  return { result, report: JSON.parse(result.output) }
}
try {
  const deadline = Date.now() + 840000
  while (!result && Date.now() < deadline) {
    const text = Buffer.concat(chunks).toString('utf8').trim()
    if (text) {
      const ready = JSON.parse(text)
      assert.match(ready.runId, /^[a-f0-9-]{36}$/)
      runId = ready.runId
      break
    }
    await new Promise(resolve => setTimeout(resolve, 1000))
  }
  assert.ok(runId, `Disposable environment did not become ready: ${Buffer.concat(chunks).toString('utf8').slice(-4096)}`)
  receipt.runId = runId
  const live = await recover(true)
  assert.equal(live.report.status, 'failed')
  assert.match(live.report.checks[0].message, /still running/)
  receipt.checks.push('active-owner-refused')
  child.kill('SIGKILL')
  await closed
  assert.equal(result.signal, 'SIGKILL')
  const ownerPath = join(root, '.ngb/test-runs', runId, 'owner.json')
  const ownerBytes = await readFile(ownerPath)
  receipt.ownerSha256 = digest(ownerBytes)
  assert.equal(JSON.parse(ownerBytes).provisioning, false)
  sentinel = `ngb-recovery-probe-${runId}`
  assert.equal(await docker(['volume', 'create', '--label', `com.docker.compose.project=ngb-kit-${runId}`,
    '--label', `com.ngb.devkit.run=${runId}`, '--label', `com.ngb.devkit.application=${'0'.repeat(64)}`, sentinel]), sentinel)
  const conflicting = await recover(true)
  assert.equal(conflicting.report.status, 'failed')
  assert.match(conflicting.report.checks[0].message, /Refusing/)
  assert.ok((await docker(['ps', '--all', '--quiet', '--filter', `label=com.ngb.devkit.run=${runId}`])).length > 0,
    'Conflicting ownership must be rejected before deleting any container.')
  assert.deepEqual(await readFile(ownerPath), ownerBytes)
  receipt.checks.push('foreign-application-resource-preserved')
  await docker(['volume', 'rm', sentinel])
  sentinel = null
  const preview = await recover(false)
  assert.equal(preview.report.status, 'passed')
  assert.equal(preview.report.recovery.state, 'preview')
  assert.ok(preview.report.recovery.runs[0].resources.length >= 7)
  assert.deepEqual(await readFile(ownerPath), ownerBytes)
  receipt.checks.push('preview-preserves-resources')
  const applied = await recover(true)
  assert.equal(applied.report.status, 'passed')
  assert.equal(applied.report.recovery.state, 'recovered')
  for (const kind of ['container', 'network', 'volume']) {
    const args = kind === 'container' ? ['ps', '--all', '--quiet'] : [kind, 'ls', '--quiet']
    assert.equal(await docker([...args, '--filter', `label=com.ngb.devkit.run=${runId}`]), '')
  }
  receipt.checks.push('sigkill-resources-removed')
  assert.equal((await recover(true)).report.recovery.state, 'not-needed')
  receipt.checks.push('recovery-idempotent')
  receipt.recovery = applied.report.recovery
  receipt.status = 'passed'
} catch (error) {
  receipt.failure = error.message
  process.exitCode = 1
} finally {
  clearTimeout(timer)
  if (!result) {
    child.kill('SIGTERM')
    await closed
  }
  if (sentinel) await docker(['volume', 'rm', sentinel])
  if (runId && receipt.status !== 'passed') receipt.failureCleanup = await recover(true)
  await rm(home, { recursive: true, force: true })
  receipt.completedAt = new Date().toISOString()
  await writeFile(output, `${JSON.stringify(receipt, null, 2)}\n`, { flag: 'wx', mode: 0o600 })
}
console.log(`Resource recovery: ${receipt.status}. Evidence: ${output}`)
