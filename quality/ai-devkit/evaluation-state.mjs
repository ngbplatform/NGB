import assert from 'node:assert/strict'
import { randomUUID } from 'node:crypto'
import { mkdir } from 'node:fs/promises'
import { join } from 'node:path'
import { atomicJson, readOptionalJson } from '../external-consumer/checkpoints.mjs'
import { digest, portablePath } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { readBytes } from '../../packaging/templates/content/infrastructure/ngb/kit-files.mjs'

// Failed evaluations are evidence, not a request to try the model again.
// Only an interrupted operation can be restarted, in a new evidence directory.
export async function evaluationState(root, identity, { signal, log = console.log } = {}) {
  await mkdir(root, { recursive: true })
  const path = join(root, 'state.json')
  const identitySha256 = digest(JSON.stringify(identity))
  const state = await readOptionalJson(path) ?? { schemaVersion: 1, identity, identitySha256, steps: {} }
  assert.equal(state.schemaVersion, 1)
  assert.equal(digest(JSON.stringify(state.identity)), state.identitySha256, 'Agent checkpoint identity was modified.')
  assert.equal(state.identitySha256, identitySha256, 'Agent inputs or clients changed; use a new candidate.')

  const validate = async receipt => {
    portablePath(receipt.directory)
    assert.equal(receipt.sha256, digest(JSON.stringify(receipt.value)), 'Agent checkpoint result was modified.')
    for (const [file, hash] of Object.entries(receipt.files)) {
      assert.ok(file.startsWith(`${receipt.directory}/`))
      const bytes = await readBytes(join(root, receipt.directory), file.slice(receipt.directory.length + 1), 64 * 1024 * 1024)
      assert.ok(bytes && digest(bytes) === hash, `Agent evidence changed: ${file}`)
    }
    return receipt.value
  }
  return async (name, operation, recover) => {
    portablePath(name)
    signal?.throwIfAborted()
    const history = state.steps[name] ??= []
    let previous = history.at(-1)
    if (previous?.status === 'passed') {
      const value = await validate(previous)
      log(`[resume] agents/${name}`)
      return value
    }
    assert.notEqual(previous?.status, 'failed',
      `Agent step ${name} previously failed. Evidence: ${previous?.directory}. No automatic model retry is permitted.`)
    const complete = async (record, result) => {
      const files = {}
      for (const file of result.files) {
        assert.ok(file.startsWith(`${record.directory}/`))
        files[file] = digest(await readBytes(join(root, record.directory), file.slice(record.directory.length + 1), 64 * 1024 * 1024))
      }
      Object.assign(record, { status: 'passed', value: result.value, sha256: digest(JSON.stringify(result.value)), files,
        completedAt: new Date().toISOString() })
      await atomicJson(path, state)
      return result.value
    }
    if (previous) {
      if (recover) {
        const result = await recover(join(root, previous.directory))
        if (result) return await complete(previous, result)
      }
      if (previous.status === 'running') previous.status = 'interrupted'
    }
    const record = { status: 'running', directory: `work/${name}/${randomUUID()}`, startedAt: new Date().toISOString() }
    history.push(record)
    await atomicJson(path, state)
    const directory = join(root, record.directory)
    await mkdir(directory, { recursive: true })
    log(`[start] agents/${name}`)
    try {
      const result = await operation(directory)
      return await complete(record, result)
    } catch (error) {
      record.status = signal?.aborted || error.code === 'ABORT_ERR' ? 'interrupted'
        : error.code === 'NGB_AGENT_UNAVAILABLE' ? 'waiting-for-access' : 'failed'
      record.failure = error.message
      record.completedAt = new Date().toISOString()
      await atomicJson(path, state)
      throw error
    }
  }
}
