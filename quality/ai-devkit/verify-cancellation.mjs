import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { executionEnvironment, runTool } from '../../packaging/templates/content/infrastructure/ngb/process.mjs'

const root = resolve(process.argv[2])
const output = resolve(process.argv[3])
const home = await mkdtemp(join(tmpdir(), 'ngb-cancellation-probe-'))
const env = executionEnvironment(home)
const docker = async args => {
  const result = await runTool('docker', args, { cwd: root, env: process.env, timeoutMs: 15000 })
  assert.equal(result.state, 'passed', result.output)
  return result.output.trim().split(/\s+/).filter(Boolean)
}
const existing = new Set(await docker(['ps', '--all', '--format', '{{.Label "com.ngb.devkit.run"}}',
  '--filter', 'label=com.ngb.devkit.run']))
const cli = join(root, 'infrastructure/ngb.mjs')
const cliSha256 = digest(await readFile(cli))
const startedAt = new Date().toISOString()
let child
let result
let runId
let timer
try {
  child = spawn(process.execPath, [cli, 'test', '--profile', 'e2e', '--format', 'json'], {
    cwd: root, env, shell: false, stdio: ['ignore', 'pipe', 'pipe'],
  })
  const chunks = []
  let bytes = 0
  const collect = chunk => {
    bytes += chunk.length
    assert.ok(bytes <= 8 * 1024 * 1024, 'Cancellation probe exceeded its output limit.')
    chunks.push(chunk)
  }
  child.stdout.on('data', collect)
  child.stderr.on('data', collect)
  const closed = new Promise((resolve, reject) => {
    child.once('error', reject)
    child.once('close', (code, signal) => {
      result = { code, signal }
      resolve(result)
    })
  })
  timer = setTimeout(() => child.kill('SIGKILL'), 600000)
  const deadline = Date.now() + 480000
  while (!result && Date.now() < deadline) {
    const runs = await docker(['ps', '--all', '--format', '{{.Label "com.ngb.devkit.run"}}',
      '--filter', 'label=com.ngb.devkit.run'])
    const created = [...new Set(runs.filter(id => !existing.has(id)))]
    assert.ok(created.length <= 1, 'Concurrent test provisioning makes the cancellation probe ambiguous.')
    if (created.length === 1) {
      runId = created[0]
      assert.match(runId, /^[a-f0-9-]{36}$/)
      child.kill('SIGTERM')
      break
    }
    await new Promise(resolve => setTimeout(resolve, 1000))
  }
  assert.ok(runId, 'No owned container was created before the cancellation deadline.')
  await closed
  assert.notEqual(result.code, 0, 'Cancelled test profile incorrectly succeeded.')
  assert.equal(result.signal, null, 'CLI did not finish its handled cancellation and cleanup.')
  const report = JSON.parse(Buffer.concat(chunks).toString('utf8'))
  assert.equal(report.status, 'failed')
  assert.ok(report.checks.some(check => check.id === 'test.e2e' && check.state === 'not-run'))
  for (const resource of ['container', 'network', 'volume']) {
    const args = resource === 'container' ? ['ps', '--all', '--quiet'] : [resource, 'ls', '--quiet']
    assert.deepEqual(await docker([...args, '--filter', `label=com.ngb.devkit.run=${runId}`]), [],
      `Cancelled profile leaked an owned ${resource}.`)
  }
  assert.equal(digest(await readFile(cli)), cliSha256)
  await writeFile(output, `${JSON.stringify({ schemaVersion: 1, status: 'passed', startedAt,
    completedAt: new Date().toISOString(), runId, cliSha256, exitCode: result.code,
    profileReportSha256: digest(JSON.stringify(report)), report }, null, 2)}\n`, { flag: 'wx' })
  console.log('Cancellation refused success and removed all owned containers, networks and volumes.')
} finally {
  clearTimeout(timer)
  if (child && !result) child.kill('SIGTERM')
  await rm(home, { recursive: true, force: true })
}
