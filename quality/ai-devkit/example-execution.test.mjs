import assert from 'node:assert/strict'
import { mkdir, mkdtemp, readFile, readdir, rm, symlink } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import { exampleRunner, exampleTestTimeoutMs } from './example-execution.mjs'
import { executionEnvironment } from '../../packaging/templates/content/infrastructure/ngb/process.mjs'

async function fixture(t) {
  const output = await mkdtemp(join(tmpdir(), 'ngb-example-execution-'))
  t.after(() => rm(output, { recursive: true, force: true }))
  const bin = join(output, 'bin')
  await mkdir(bin)
  await symlink(process.execPath, join(bin, 'dotnet'))
  const controller = new AbortController()
  const invoke = exampleRunner({
    output, candidateSha256: 'a'.repeat(64), signal: controller.signal,
    env: executionEnvironment(output, { PATH: bin }),
  })
  const json = async path => JSON.parse(await readFile(join(output, path)))
  return { output, controller, invoke, json }
}

test('example execution records process limits and results and redacts captured output', async t => {
  const { output, invoke, json } = await fixture(t)
  const result = await invoke('unit', ['-e', 'console.log("Bearer private-value")'], output)
  assert.equal(result.trim(), 'Bearer [REDACTED]')
  assert.equal(await readFile(join(output, 'unit.log'), 'utf8'), result)
  const process = await json('processes/unit.json')
  assert.equal(process.timeoutMs, 600_000)
  assert.equal(process.tool, 'dotnet')
  assert.equal(process.candidateSha256, 'a'.repeat(64))
  const receipt = await json('unit.result.json')
  assert.equal(receipt.state, 'passed')
  assert.equal(receipt.exitCode, 0)
  assert.ok(receipt.elapsedMs > 0)
  assert.equal(receipt.timeoutMs, process.timeoutMs)
})

test('a nonzero test exit preserves its log and failure receipt and is not retried', async t => {
  const { output, invoke, json } = await fixture(t)
  await assert.rejects(invoke('integration', ['-e', 'console.log("Failed case"); process.exit(23)'],
    output, 'node', exampleTestTimeoutMs), /integration.*reason: 23/)
  assert.equal((await json('integration.result.json')).exitCode, 23)
  assert.equal((await json('processes/integration.json')).timeoutMs, 45 * 60_000)
  assert.equal((await readFile(join(output, 'integration.log'), 'utf8')).trim(), 'Failed case')
  assert.deepEqual(await readdir(join(output, 'processes')), ['integration.json'])
})

test('timeout records the reason and partial output before reporting failure', async t => {
  const { output, invoke, json } = await fixture(t)
  await assert.rejects(invoke('timeout', ['-e', 'console.log("Started case"); setInterval(() => {}, 1000)'],
    output, 'node', 1000), /timeout.*reason: timeout/)
  const result = await json('timeout.result.json')
  assert.equal(result.state, 'failed')
  assert.equal(result.reason, 'timeout')
  assert.equal(result.timeoutMs, 1000)
  assert.equal((await readFile(join(output, 'timeout.log'), 'utf8')).trim(), 'Started case')
})

test('cancellation before launch creates neither a process nor success evidence', async t => {
  const { output, controller, invoke } = await fixture(t)
  controller.abort()
  await assert.rejects(invoke('cancelled', ['-e', 'process.exit(0)'], output), { name: 'AbortError' })
  assert.deepEqual(await readdir(output), ['bin'])
})
