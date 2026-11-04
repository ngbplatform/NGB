import assert from 'node:assert/strict'
import { randomUUID } from 'node:crypto'
import { lstat, mkdir, open, realpath, rename, rm } from 'node:fs/promises'
import { hostname } from 'node:os'
import { dirname, join } from 'node:path'
import { digest, distinctPaths, objectKeys, portablePath } from './kit-contracts.mjs'
import { readAsset, readBytes, safePath } from './file-data.mjs'

const journalPath = '.ngb/transaction.json'
const lockPath = '.ngb/operation.lock'
const maximumFileBytes = 128 * 1024 * 1024
const maximumTotalBytes = 512 * 1024 * 1024

function writablePath(path) {
  portablePath(path)
  assert.ok(!/^\.git(?:\/|$)/i.test(path) && !/^\.env(?:\.|$)/i.test(path)
    && (!path.startsWith('.ngb/') || path === '.ngb/packages.json' || /^\.ngb\/nuget\/NGB\.Platform\.[A-Za-z0-9.-]+\.nupkg$/.test(path)),
  `Path is outside application transaction ownership: ${path}`)
}

async function replace(root, path, bytes, mode = 0o600) {
  const location = await safePath(root, path)
  if (bytes === null) {
    await rm(location, { force: true })
    return
  }
  await mkdir(dirname(location), { recursive: true, mode: 0o700 })
  const temporary = `${location}.ngb-${randomUUID()}`
  let handle
  try {
    handle = await open(temporary, 'wx', mode)
    await handle.writeFile(bytes)
    await handle.sync()
    await handle.close()
    handle = null
    await safePath(root, path)
    await rename(temporary, location)
  } finally {
    await handle?.close()
    await rm(temporary, { force: true })
  }
}

async function saveJournal(root, journal) {
  const bytes = Buffer.from(`${JSON.stringify(journal, null, 2)}\n`)
  assert.ok(bytes.length <= 8 * 1024 * 1024, 'Transaction journal exceeds its size limit.')
  await replace(root, journalPath, bytes)
}

async function lock(root) {
  const location = await safePath(root, lockPath)
  await mkdir(dirname(location), { recursive: true, mode: 0o700 })
  await mkdir(location, { mode: 0o700 })
  const owner = { schemaVersion: 1, id: randomUUID(), pid: process.pid, host: hostname() }
  try {
    await replace(root, `${lockPath}/owner.json`, Buffer.from(`${JSON.stringify(owner)}\n`))
  } catch (error) {
    await rm(location, { recursive: true })
    throw error
  }
  return async () => {
    const current = JSON.parse(await readAsset(root, `${lockPath}/owner.json`))
    assert.deepEqual(current, owner, 'Operation lock ownership changed; preserving the lock.')
    await rm(location, { recursive: true })
  }
}

function validateJournal(value) {
  objectKeys(value, ['schemaVersion', 'id', 'identity', 'state', 'files'])
  assert.equal(value.schemaVersion, 1, 'Unsupported transaction schema.')
  assert.match(value.id, /^[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/, 'Invalid transaction identifier.')
  assert.ok(typeof value.identity === 'string' && value.identity.length <= 256, 'Invalid transaction identity.')
  assert.ok(['prepared', 'committed', 'rolled-back'].includes(value.state), 'Invalid transaction state.')
  assert.ok(Array.isArray(value.files) && value.files.length > 0, 'Empty transaction.')
  distinctPaths(value.files.map(file => file.path))
  for (const file of value.files) {
    objectKeys(file, ['path', 'before', 'after', 'mode'])
    writablePath(file.path)
    for (const side of ['before', 'after']) {
      assert.ok(file[side] === null || /^[a-f0-9]{64}$/.test(file[side]), 'Invalid transaction content identity.')
    }
    assert.ok(Number.isInteger(file.mode) && file.mode >= 0 && file.mode <= 0o777, 'Invalid transaction permissions.')
  }
  return value
}

async function content(root, journal, hash) {
  if (hash === null) return null
  const bytes = await readBytes(root, `.ngb/transactions/${journal.id}/objects/${hash}`, maximumFileBytes)
  assert.ok(bytes && digest(bytes) === hash, 'Transaction backup is missing or changed; no application files were recovered.')
  return bytes
}

async function snapshotMatches(root, snapshot) {
  for (const [path, bytes] of Object.entries(snapshot)) {
    assert.equal(digest(await readBytes(root, path, maximumFileBytes)), digest(bytes), `Concurrent change detected: ${path}`)
  }
}

async function archive(root, journal) {
  await rename(await safePath(root, journalPath),
    await safePath(root, `.ngb/transactions/${journal.id}/receipt.json`))
}

export async function applyTransaction(root, snapshot, changes, identity, { beforeWrite = async () => {},
  beforeCommit = async () => {}, replaceFile = null } = {}) {
  root = await realpath(root)
  distinctPaths(Object.keys(changes))
  assert.ok(Object.keys(changes).length > 0, 'Empty transaction.')
  let total = 0
  for (const [path, after] of Object.entries(changes)) {
    writablePath(path)
    assert.ok(Object.hasOwn(snapshot, path), 'Every transaction write requires an original snapshot.')
    for (const value of [snapshot[path], after]) {
      assert.ok(value === null || Buffer.isBuffer(value), 'Transactions require binary snapshots.')
      assert.ok((value?.length ?? 0) <= maximumFileBytes, 'Transaction file exceeds its size limit.')
      total += value?.length ?? 0
    }
  }
  assert.ok(total <= maximumTotalBytes, 'Transaction exceeds its total size limit.')
  const unlock = await lock(root)
  let journal
  try {
    assert.equal(await readAsset(root, journalPath, 8 * 1024 * 1024), null,
      'An interrupted transaction needs recovery. Run recover to inspect it, then recover --apply.')
    assert.equal(await readAsset(root, '.ngb/kit-transaction.json', 32 * 1024 * 1024), null,
      'An interrupted legacy Kit transaction requires manual recovery before another update.')
    await snapshotMatches(root, snapshot)
    await beforeCommit()
    journal = { schemaVersion: 1, id: randomUUID(), identity, state: 'prepared', files: [] }
    const stored = new Set()
    for (const [path, after] of Object.entries(changes)) {
      const before = snapshot[path]
      let mode = 0o600
      if (before !== null) mode = (await lstat(await safePath(root, path))).mode & 0o777
      journal.files.push({ path, before: digest(before), after: digest(after), mode })
      for (const value of [before, after]) {
        const hash = digest(value)
        if (hash === null || stored.has(hash)) continue
        await replace(root, `.ngb/transactions/${journal.id}/objects/${hash}`, value)
        stored.add(hash)
      }
    }
    validateJournal(journal)
    await saveJournal(root, journal)
    const applied = []
    try {
      for (const file of journal.files) {
        await beforeWrite(file.path)
        await snapshotMatches(root, { [file.path]: snapshot[file.path] })
        applied.push(file)
        if (replaceFile && changes[file.path] !== null) await replaceFile(await safePath(root, file.path), changes[file.path])
        else await replace(root, file.path, changes[file.path], file.mode)
      }
    } catch (error) {
      for (const file of applied.reverse()) {
        const current = digest(await readBytes(root, file.path, maximumFileBytes))
        if (current === file.after) await replace(root, file.path, snapshot[file.path], file.mode)
        else assert.equal(current, file.before, `Rollback conflicts with a subsequent edit: ${file.path}`)
      }
      journal.state = 'rolled-back'
      await saveJournal(root, journal)
      throw error
    }
    journal.state = 'committed'
    await saveJournal(root, journal)
  } finally {
    try {
      if (journal && journal.state !== 'prepared') await archive(root, journal)
    } finally {
      await unlock()
    }
  }
  return { id: journal.id, state: journal.state, files: journal.files.map(file => file.path) }
}

async function staleLock(root) {
  const location = await safePath(root, lockPath)
  let exists
  try {
    exists = await lstat(location)
  } catch (error) {
    if (error.code !== 'ENOENT') throw error
    return false
  }
  assert.ok(exists.isDirectory(), 'Unknown operation lock; preserve it for manual inspection.')
  const text = await readAsset(root, `${lockPath}/owner.json`)
  assert.ok(text, 'Operation lock has no owner; manual inspection is required.')
  const owner = JSON.parse(text)
  objectKeys(owner, ['schemaVersion', 'id', 'pid', 'host'])
  assert.ok(owner.schemaVersion === 1 && owner.host === hostname()
    && Number.isInteger(owner.pid) && owner.pid > 0, 'Cannot establish ownership of the interrupted operation lock.')
  try {
    process.kill(owner.pid, 0)
    throw new Error('The operation lock owner is still running; recovery cannot proceed.')
  } catch (error) {
    if (error.code !== 'ESRCH') throw error
  }
  return text
}

export async function recoverTransaction(root, apply = false) {
  root = await realpath(root)
  const text = await readAsset(root, journalPath, 8 * 1024 * 1024)
  const owner = await staleLock(root)
  if (text === null) {
    assert.ok(!owner, 'An abandoned operation lock has no journal; inspect it manually before removing it.')
    return { state: 'not-needed', files: [] }
  }
  const journal = validateJournal(JSON.parse(text))
  const snapshots = {}
  const originals = {}
  let total = 0
  for (const file of journal.files) {
    const current = await readBytes(root, file.path, maximumFileBytes)
    const hash = digest(current)
    assert.ok(hash === file.before || hash === file.after, `Recovery conflicts with a subsequent edit: ${file.path}`)
    snapshots[file.path] = current
    for (const side of ['before', 'after']) {
      const bytes = await content(root, journal, file[side])
      total += bytes?.length ?? 0
      assert.ok(total <= maximumTotalBytes, 'Recovery payload exceeds its total size limit.')
      if (side === 'before') originals[file.path] = bytes
    }
    if (journal.state === 'committed') assert.equal(hash, file.after, `Committed transaction changed: ${file.path}`)
    if (journal.state === 'rolled-back') assert.equal(hash, file.before, `Rolled-back transaction changed: ${file.path}`)
  }
  const result = { id: journal.id, state: journal.state, action: journal.state === 'prepared' ? 'rollback' : 'archive',
    files: journal.files.map(file => file.path) }
  if (!apply) return result
  if (owner) {
    assert.equal(await readAsset(root, `${lockPath}/owner.json`), owner, 'Operation lock changed during recovery inspection.')
    const abandoned = `.ngb/transactions/${journal.id}/abandoned-lock-${randomUUID()}`
    await rename(await safePath(root, lockPath), await safePath(root, abandoned))
  }
  const unlock = await lock(root)
  try {
    assert.equal(await readAsset(root, journalPath, 8 * 1024 * 1024), text, 'Transaction changed during recovery inspection.')
    await snapshotMatches(root, snapshots)
    if (journal.state === 'prepared') {
      for (const file of [...journal.files].reverse()) {
        await snapshotMatches(root, { [file.path]: snapshots[file.path] })
        await replace(root, file.path, originals[file.path], file.mode)
      }
      journal.state = 'rolled-back'
      await saveJournal(root, journal)
    }
    await archive(root, journal)
    return { ...result, state: journal.state }
  } finally {
    await unlock()
  }
}
