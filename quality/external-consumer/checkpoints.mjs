import assert from 'node:assert/strict'
import { randomUUID } from 'node:crypto'
import { mkdir, readFile, rename, unlink, writeFile } from 'node:fs/promises'
import { hostname } from 'node:os'
import { dirname, isAbsolute, relative, resolve } from 'node:path'
import { digest } from './release-contracts.mjs'

export async function atomicJson(path, value) {
  await mkdir(dirname(path), { recursive: true })
  const temporary = `${path}.${randomUUID()}.tmp`
  try {
    await writeFile(temporary, `${JSON.stringify(value, null, 2)}\n`, { flag: 'wx', mode: 0o600 })
    await rename(temporary, path)
  } finally {
    await unlink(temporary).catch(error => {
      if (error.code !== 'ENOENT') throw error
    })
  }
}

export async function readOptionalJson(path) {
  try {
    return JSON.parse(await readFile(path, 'utf8'))
  } catch (error) {
    if (error.code === 'ENOENT') return null
    throw error
  }
}

export async function lockRun(path) {
  await mkdir(dirname(path), { recursive: true })
  const owner = { host: hostname(), pid: process.pid, token: randomUUID() }
  try {
    await writeFile(path, JSON.stringify(owner), { flag: 'wx', mode: 0o600 })
  } catch (error) {
    if (error.code !== 'EEXIST') throw error
    const previous = await readOptionalJson(path)
    assert.ok(previous && previous.host === owner.host, `Certification lock requires inspection: ${path}`)
    assert.ok(Number.isInteger(previous.pid) && previous.pid > 0, `Invalid certification lock owner: ${path}`)
    try {
      process.kill(previous.pid, 0)
    } catch (failure) {
      if (failure.code === 'ESRCH') {
        // Serialize stale-lock recovery so a second starter cannot remove the
        // first starter's newly acquired lock between reading and unlinking.
        const releaseRecovery = await lockRun(`${path}.recovery`)
        try {
          assert.deepEqual(await readOptionalJson(path), previous, 'Certification lock changed during recovery; retry the command.')
          await unlink(path)
          return await lockRun(path)
        } finally {
          await releaseRecovery()
        }
      }
      throw failure
    }
    throw new Error(`Certification is already running (PID ${previous.pid}). Lock: ${path}`)
  }
  return async () => {
    assert.equal((await readOptionalJson(path))?.token, owner.token, 'Certification lock ownership changed.')
    await unlink(path)
  }
}

export async function checkpointRunner(root, identity, { log = console.log } = {}) {
  const statePath = resolve(root, 'run-state.json')
  const identitySha256 = digest(JSON.stringify(identity))
  const state = await readOptionalJson(statePath) ?? { schemaVersion: 1, identitySha256, identity, stages: {} }
  assert.equal(state.schemaVersion, 1)
  assert.equal(digest(JSON.stringify(state.identity)), state.identitySha256, 'Checkpoint identity was modified.')
  assert.equal(state.identitySha256, identitySha256,
    'Certification inputs or environment changed. Seal a new candidate; previous results cannot be resumed.')
  let predecessor = identitySha256
  return async (name, operation, outputs = []) => {
    assert.match(name, /^[a-z][a-z0-9-]*$/)
    const paths = outputs.map(path => {
      const key = relative(root, resolve(root, path))
      assert.ok(key && !key.startsWith('..') && !isAbsolute(key), 'Checkpoint output must stay inside its evidence directory.')
      return key
    })
    const previous = state.stages[name]
    let reusable = previous?.status === 'passed' && previous.predecessor === predecessor
      && JSON.stringify(Object.keys(previous.outputs ?? {})) === JSON.stringify(paths)
    if (reusable) {
      for (const path of paths) {
        try {
          if (digest(await readFile(resolve(root, path))) !== previous.outputs[path]) reusable = false
        } catch (error) {
          if (error.code !== 'ENOENT') throw error
          reusable = false
        }
      }
    }
    if (reusable) {
      log(`[resume] ${name}: passed (${(previous.durationMs / 1000).toFixed(1)}s previously)`)
      predecessor = digest(JSON.stringify(previous))
      return
    }
    const stage = { status: 'running', attempt: randomUUID(), predecessor, startedAt: new Date().toISOString() }
    state.stages[name] = stage
    await atomicJson(statePath, state)
    log(`[start] ${name}`)
    const started = performance.now()
    try {
      await operation()
      stage.outputs = {}
      for (const path of paths) stage.outputs[path] = digest(await readFile(resolve(root, path)))
      stage.status = 'passed'
    } catch (error) {
      stage.status = 'failed'
      throw error
    } finally {
      stage.durationMs = Math.round(performance.now() - started)
      stage.finishedAt = new Date().toISOString()
      await atomicJson(statePath, state)
      log(`[${stage.status}] ${name}: ${(stage.durationMs / 1000).toFixed(1)}s`)
    }
    predecessor = digest(JSON.stringify(stage))
  }
}
