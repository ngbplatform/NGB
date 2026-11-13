import assert from 'node:assert/strict'
import { mkdtemp, readFile, realpath, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join, relative } from 'node:path'
import test from 'node:test'
import { evaluationState } from './evaluation-state.mjs'

async function fixture(t) {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'ngb-evaluation-state-')))
  t.after(() => rm(root, { recursive: true, force: true }))
  const identity = { candidate: 'exact-source-and-package-identity' }
  const read = async () => JSON.parse(await readFile(join(root, 'state.json'), 'utf8'))
  const success = async directory => {
    const file = join(directory, 'evidence.txt')
    await writeFile(file, 'independent result')
    return { value: { directory, status: 'passed' }, files: [relative(root, file)] }
  }
  return { root, identity, read, success }
}

test('completed stages are reused only with the same identity and unchanged evidence', async t => {
  const { root, identity, read, success } = await fixture(t)
  let step = await evaluationState(root, identity)
  const result = await step('codex/root/catalog/inference', success)
  step = await evaluationState(root, identity)
  assert.deepEqual(await step('codex/root/catalog/inference', () => assert.fail('must not invoke model')), result)
  assert.equal((await read()).steps['codex/root/catalog/inference'].length, 1)
  await assert.rejects(evaluationState(root, { candidate: 'different' }), /changed/)
  await writeFile(join(result.directory, 'evidence.txt'), 'changed')
  await assert.rejects(step('codex/root/catalog/inference', success), /evidence changed/)
})

test('interruption retains old output and restarts only the unfinished stage', async t => {
  const { root, identity, read, success } = await fixture(t)
  const abort = new AbortController()
  let step = await evaluationState(root, identity, { signal: abort.signal })
  await step('inference', success)
  let first
  await assert.rejects(step('acceptance', async directory => {
    first = directory
    await writeFile(join(directory, 'partial.log'), 'partial result')
    abort.abort()
    abort.signal.throwIfAborted()
  }))
  assert.equal((await read()).steps.acceptance[0].status, 'interrupted')
  step = await evaluationState(root, identity)
  await step('inference', () => assert.fail('no repeated inference'))
  const result = await step('acceptance', success, async () => null)
  assert.notEqual(result.directory, first)
  assert.equal(await readFile(join(first, 'partial.log'), 'utf8'), 'partial result')
  assert.deepEqual((await read()).steps.acceptance.map(row => row.status), ['interrupted', 'passed'])
})

test('a completed receipt is recovered after interruption without another operation', async t => {
  const { root, identity, read, success } = await fixture(t)
  let receipt
  let step = await evaluationState(root, identity)
  await assert.rejects(step('inference', async directory => {
    receipt = await success(directory)
    throw Object.assign(new Error('stopped after receipt'), { code: 'ABORT_ERR' })
  }))
  step = await evaluationState(root, identity)
  assert.deepEqual(await step('inference', () => assert.fail(), async () => receipt), receipt.value)
  assert.equal((await read()).steps.inference.length, 1)
})

test('ordinary failures never become automatic model retries', async t => {
  const { root, identity, read, success } = await fixture(t)
  let step = await evaluationState(root, identity)
  await assert.rejects(step('acceptance', async () => { throw new Error('independent test failed') }))
  step = await evaluationState(root, identity)
  await assert.rejects(step('acceptance', success, () => assert.fail()), /previously failed/)
  assert.equal((await read()).steps.acceptance.length, 1)
})

test('state rejects tampered identity, result, unsafe names and pre-cancelled calls', async t => {
  const { root, identity, read, success } = await fixture(t)
  const step = await evaluationState(root, identity)
  await assert.rejects(step('../escape', success))
  await step('saved', success)
  const state = await read()
  state.steps.saved[0].value.status = 'altered'
  await writeFile(join(root, 'state.json'), JSON.stringify(state))
  const changed = await evaluationState(root, identity)
  await assert.rejects(changed('saved', success), /result was modified/)
  state.identity.candidate = 'altered'
  await writeFile(join(root, 'state.json'), JSON.stringify(state))
  await assert.rejects(evaluationState(root, identity), /identity was modified/)
  const cancelled = await evaluationState(join(root, 'cancelled'), identity, { signal: AbortSignal.abort() })
  await assert.rejects(cancelled('inference', () => assert.fail()), { name: 'AbortError' })
})
