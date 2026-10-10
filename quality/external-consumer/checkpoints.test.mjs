import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { once } from 'node:events'
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { hostname, tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import { atomicJson, checkpointRunner, lockRun, readOptionalJson } from './checkpoints.mjs'

const identity = { manifest: 'candidate-sha', environment: { sdk: '10.0.401', image: 'image-sha' } }
const quiet = { log() {} }
async function directory(t) {
  const root = await mkdtemp(join(tmpdir(), 'ngb-checkpoints-test-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  return root
}

test('resume retains earlier passes, repeats a failed stage and runs later stages', async t => {
  const root = await directory(t)
  let calls = 0
  let stage = await checkpointRunner(root, identity, quiet)
  const first = async () => {
    calls += 1
    await writeFile(join(root, 'result.json'), 'verified')
  }
  await stage('first', first, ['result.json'])
  await assert.rejects(stage('second', async () => { throw new Error('test failure') }), /test failure/)
  assert.equal((await readOptionalJson(join(root, 'run-state.json'))).stages.second.status, 'failed')
  stage = await checkpointRunner(root, identity, quiet)
  await stage('first', first, ['result.json'])
  await stage('second', async () => { calls += 1 })
  await stage('third', async () => { calls += 1 })
  assert.equal(calls, 3)
  stage = await checkpointRunner(root, identity, quiet)
  for (const name of ['first', 'second', 'third']) {
    await stage(name, () => assert.fail('Completed stage must not run'), name === 'first' ? ['result.json'] : [])
  }
})

test('changed or missing output repeats that stage and invalidates downstream passes', async t => {
  const root = await directory(t)
  let calls = 0
  const run = async () => {
    const stage = await checkpointRunner(root, identity, quiet)
    await stage('first', () => writeFile(join(root, 'first.json'), 'verified'), ['first.json'])
    await stage('second', async () => { calls += 1 })
  }
  await run()
  await writeFile(join(root, 'first.json'), 'tampered')
  await run()
  assert.equal(calls, 2)
  await rm(join(root, 'first.json'))
  await run()
  assert.equal(calls, 3)
})

test('candidate or environment changes and malformed identity are rejected before any work', async t => {
  const root = await directory(t)
  const stage = await checkpointRunner(root, identity, quiet)
  await stage('first', async () => {})
  for (const changed of [{ ...identity, manifest: 'other' }, { ...identity, environment: { sdk: 'other' } }]) {
    await assert.rejects(checkpointRunner(root, changed, quiet), /environment changed/)
  }
  const state = await readOptionalJson(join(root, 'run-state.json'))
  state.identity.manifest = 'altered'
  await atomicJson(join(root, 'run-state.json'), state)
  await assert.rejects(checkpointRunner(root, identity, quiet), /identity was modified/)
  await writeFile(join(root, 'run-state.json'), '{broken')
  await assert.rejects(checkpointRunner(root, identity, quiet), SyntaxError)
})

test('interrupted stage is not a pass; missing evidence cannot record success', async t => {
  const root = await directory(t)
  let stage = await checkpointRunner(root, identity, quiet)
  await stage('first', async () => {})
  const state = await readOptionalJson(join(root, 'run-state.json'))
  state.stages.first.status = 'running'
  await atomicJson(join(root, 'run-state.json'), state)
  let called = false
  stage = await checkpointRunner(root, identity, quiet)
  await stage('first', async () => { called = true })
  assert.equal(called, true)
  await assert.rejects(stage('second', async () => {}, ['missing.json']), /ENOENT/)
  assert.equal((await readOptionalJson(join(root, 'run-state.json'))).stages.second.status, 'failed')
  await assert.rejects(stage('../escape', async () => {}))
  await assert.rejects(stage('escape', async () => {}, ['../outside.json']))
})

test('exclusive lock refuses an active owner and recovers after owner process dies', async t => {
  const root = await directory(t)
  const path = join(root, 'run.lock')
  const unlock = await lockRun(path)
  await assert.rejects(lockRun(path), /already running/)
  await unlock()
  const child = spawn(process.execPath, ['-e', 'process.exit(0)'])
  await once(child, 'exit')
  await writeFile(path, JSON.stringify({ host: hostname(), pid: child.pid, token: 'dead-owner' }))
  const recovered = await lockRun(path)
  await recovered()
  await writeFile(path, JSON.stringify({ host: 'another-host', pid: 1 }))
  await assert.rejects(lockRun(path), /requires inspection/)
})

test('atomic records replace complete JSON and invalid JSON is never interpreted as absent', async t => {
  const root = await directory(t)
  const path = join(root, 'nested/record.json')
  assert.equal(await readOptionalJson(path), null)
  await atomicJson(path, { value: 1 })
  await atomicJson(path, { value: 2 })
  assert.deepEqual(JSON.parse(await readFile(path, 'utf8')), { value: 2 })
  await writeFile(path, 'broken')
  await assert.rejects(readOptionalJson(path), SyntaxError)
})

test('concurrent recovery cannot grant the same abandoned lock to multiple starters', async t => {
  const root = await directory(t)
  const path = join(root, 'abandoned.lock')
  const child = spawn(process.execPath, ['-e', 'process.exit(0)'])
  await once(child, 'exit')
  await writeFile(path, JSON.stringify({ host: hostname(), pid: child.pid, token: 'abandoned' }))
  const attempts = await Promise.allSettled(Array.from({ length: 8 }, () => lockRun(path)))
  const owners = attempts.filter(result => result.status === 'fulfilled')
  assert.equal(owners.length, 1)
  await owners[0].value()
})
