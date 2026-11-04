import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { exactVersion } from './contracts.mjs'

export const kitSchemaVersion = 1
export const maximumAssetBytes = 2 * 1024 * 1024
export const maximumAssets = 4096
export const blockStart = '<!-- ngb:instructions:start -->'
export const blockEnd = '<!-- ngb:instructions:end -->'

export function digest(bytes) {
  return bytes === null ? null : createHash('sha256').update(bytes).digest('hex')
}

export function portablePath(path) {
  assert.ok(typeof path === 'string' && path.length > 0 && path.length <= 240, 'Invalid asset path length.')
  assert.ok(!/[\\\x00-\x1f\x7f:<>"|?*]/.test(path), `Nonportable asset path: ${path}`)
  assert.equal(path, path.normalize('NFC'), 'Asset paths must use NFC Unicode normalization.')
  for (const part of path.split('/')) {
    assert.ok(part !== '' && part !== '.' && part !== '..' && !/[. ]$/.test(part), `Unsafe asset path: ${path}`)
    assert.ok(!/^(con|prn|aux|nul|com[0-9]|lpt[0-9])(?:\.|$)/i.test(part), `Reserved asset path: ${path}`)
  }
  return path
}

export function distinctPaths(paths) {
  assert.ok(paths.length <= maximumAssets, 'Asset inventory exceeds its size limit.')
  const seen = new Set()
  for (const path of paths) {
    const normalized = portablePath(path).toLowerCase()
    assert.ok(!seen.has(normalized), `Case-insensitive asset collision: ${path}`)
    seen.add(normalized)
  }
  for (const path of seen) {
    const parts = path.split('/')
    while (parts.length > 1) {
      parts.pop()
      assert.ok(!seen.has(parts.join('/')), `An asset is also a directory: ${path}`)
    }
  }
}

export function objectKeys(value, required, optional = []) {
  assert.ok(value && typeof value === 'object' && !Array.isArray(value), 'Expected an object.')
  assert.ok(required.every(key => Object.hasOwn(value, key)), `Missing required field: ${required.join(', ')}`)
  for (const key of Object.keys(value)) {
    assert.ok([...required, ...optional].includes(key), `Unknown field: ${key}`)
  }
}

export function officialPath(path, role) {
  portablePath(path)
  const rootBlock = ['AGENTS.md', 'CLAUDE.md'].includes(path)
  const vendor = /^ai\/vendor\/[A-Za-z0-9_./-]+$/.test(path)
  const adapter = /^\.(agents|claude)\/skills\/ngb-[a-z0-9-]+\/[A-Za-z0-9_./-]+$/.test(path)
  assert.ok(rootBlock || vendor || adapter, `Path is outside official Kit ownership: ${path}`)
  assert.equal(role, rootBlock ? 'block' : 'file', `Invalid ownership role: ${path}`)
}

export function textBytes(text) {
  assert.ok(typeof text === 'string', 'Asset content must be UTF-8 text.')
  assert.ok(!text.includes('\r') && !text.includes('\0') && !text.startsWith('\uFEFF'), 'Official text must be UTF-8/LF without a BOM or NUL.')
  const bytes = Buffer.from(text)
  assert.equal(bytes.toString('utf8'), text, 'Invalid Unicode asset content.')
  assert.ok(bytes.length <= maximumAssetBytes, 'Asset exceeds its size limit.')
  return bytes
}

export function validatePayload(payload) {
  objectKeys(payload, ['schemaVersion', 'version', 'source', 'files'])
  assert.equal(payload.schemaVersion, kitSchemaVersion, 'Unsupported Kit payload schema.')
  exactVersion(payload.version)
  objectKeys(payload.source, ['package', 'version'])
  assert.equal(payload.source.package, 'NGB.Platform.Templates')
  assert.equal(payload.source.version, payload.version)
  assert.ok(Array.isArray(payload.files) && payload.files.length > 0, 'Kit payload is empty.')
  distinctPaths(payload.files.map(file => file.path))
  let total = 0
  for (const file of payload.files) {
    objectKeys(file, ['path', 'role', 'content', 'sha256'])
    officialPath(file.path, file.role)
    const bytes = textBytes(file.content)
    total += bytes.length
    assert.equal(digest(bytes), file.sha256, `Kit payload integrity mismatch: ${file.path}`)
    if (file.role === 'block') {
      assert.equal(managedBlock(file.content).content, file.content, 'Payload must contain exactly one managed block.')
    }
  }
  assert.ok(total <= 16 * 1024 * 1024, 'Kit payload exceeds its total size limit.')
  return payload
}

export function managedBlock(text) {
  const starts = [...text.matchAll(/<!-- ngb:instructions:start -->/g)]
  const ends = [...text.matchAll(/<!-- ngb:instructions:end -->/g)]
  if (starts.length === 0 && ends.length === 0) return null
  assert.ok(starts.length === 1 && ends.length === 1 && starts[0].index < ends[0].index,
    'Missing, duplicated or reversed NGB instruction markers; review the root instruction file.')
  const start = starts[0].index
  const end = ends[0].index + blockEnd.length
  return { start, end, content: text.slice(start, end) }
}

export function replaceBlock(text, replacement) {
  const block = managedBlock(text)
  if (block) return text.slice(0, block.start) + (replacement ?? '') + text.slice(block.end)
  if (replacement === null) return text
  const separator = text.length === 0 ? '' : (text.endsWith('\n') ? '\n' : '\n\n')
  return `${text}${separator}${replacement}\n`
}

export function reconcile(base, local, incoming) {
  if (base === null && local !== null) return 'conflict'
  if (local === base) return local === incoming ? 'unchanged' : 'update'
  if (local === null) return 'deleted'
  if (incoming === base) return 'customized'
  if (local === incoming) return 'converged'
  return 'conflict'
}

export function conflictIdentity(source, target, path, base, local, incoming) {
  return digest(JSON.stringify({ source, target, path, base: digest(base), local: digest(local), incoming: digest(incoming) }))
}

export function resolveConflicts(plan, document) {
  objectKeys(document, ['schemaVersion', 'planSha256', 'decisions'])
  assert.equal(document.schemaVersion, kitSchemaVersion, 'Unsupported resolution schema.')
  assert.equal(document.planSha256, plan.sha256, 'Conflict resolutions are stale or belong to another plan.')
  assert.ok(Array.isArray(document.decisions), 'Resolutions must be an array.')
  const decisions = new Map()
  for (const decision of document.decisions) {
    objectKeys(decision, ['id', 'choice'], ['content'])
    assert.ok(!decisions.has(decision.id), 'Duplicate conflict resolution.')
    assert.ok(['local', 'incoming', 'replacement'].includes(decision.choice), 'Unknown conflict choice.')
    assert.equal(Object.hasOwn(decision, 'content'), decision.choice === 'replacement', 'Only replacement resolutions accept content.')
    if (decision.choice === 'replacement') textBytes(decision.content)
    decisions.set(decision.id, decision)
  }
  const entries = plan.entries.map(entry => {
    const decision = decisions.get(entry.id)
    if (!decision) return entry
    assert.ok(['conflict', 'deleted'].includes(entry.state), 'A decision does not refer to a conflict.')
    decisions.delete(entry.id)
    const content = decision.choice === 'replacement' ? decision.content : entry[decision.choice]
    return { ...entry, state: 'resolved', resolution: decision.choice, result: content }
  })
  assert.equal(decisions.size, 0, 'Resolution refers to an unknown conflict.')
  return { ...plan, entries }
}
