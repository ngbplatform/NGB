import assert from 'node:assert/strict'
import {
  conflictIdentity, digest, distinctPaths, managedBlock, objectKeys, portablePath,
  reconcile, replaceBlock, textBytes, validatePayload,
} from './kit-contracts.mjs'

export function payloadIdentity(payload) {
  validatePayload(payload)
  return digest(JSON.stringify(payload))
}

function customPath(path) {
  portablePath(path)
  const match = path.match(/^ai\/custom\/skills\/(app-[a-z0-9-]+)\/(.+)$/)
  assert.ok(match && match[1].length <= 64, `Custom skills require app-* names: ${path}`)
  return match
}

export function customAdapters(sources) {
  distinctPaths(Object.keys(sources))
  const adapters = {}
  let bytes = 0
  for (const [path, content] of Object.entries(sources)) {
    const [, name, suffix] = customPath(path)
    bytes += textBytes(content).length
    assert.ok(bytes <= 4 * 1024 * 1024, 'Custom skill sources exceed the 4 MiB aggregate limit.')
    const entry = sources[`ai/custom/skills/${name}/SKILL.md`]
    assert.ok(entry && entry.startsWith(`---\nname: ${name}\n`), `Custom skill frontmatter must name ${name}.`)
    const end = entry.indexOf('\n---\n', 4)
    assert.ok(end > 0, `Custom skill frontmatter is not closed: ${name}`)
    const metadata = entry.slice(0, end + 5)
    assert.match(metadata, /\ndescription: [^\n]+\n/, `Missing skill description: ${name}`)
    if (suffix !== 'SKILL.md') continue
    // Read the canonical document in place so relative links, resource paths and
    // commands keep their original base directory. No Markdown rewriting or
    // duplicated application-owned resources are needed in either agent tree.
    const rendered = `${metadata}\nRead [the canonical application skill](../../../${path}) before performing this task.\n`
      + 'Resolve its relative paths from that canonical file, including supporting resources outside its directory.\n'
    for (const agent of ['agents', 'claude']) {
      const destination = `.${agent}/skills/${name}/${suffix}`
      adapters[destination] = {
        path: destination,
        source: path,
        content: rendered,
        sha256: digest(rendered),
        role: 'file',
      }
    }
  }
  return adapters
}

export function installedManifest(payload, custom = {}) {
  const identity = payloadIdentity(payload)
  return {
    schemaVersion: 1,
    version: payload.version,
    versions: Object.fromEntries(['sdk', 'ui', 'template', 'cli', 'kit'].map(name => [name, payload.version])),
    source: payload.source,
    payloadSha256: identity,
    capabilitySha256: payload.files.find(file => file.path === 'ai/vendor/capability-registry.json')?.sha256 ?? null,
    files: payload.files.map(({ path, role, sha256 }) => ({ path, role, sha256 })),
    custom: Object.values(custom).map(({ path, source, content, sha256 }) => ({ path, source, content, sha256 })),
  }
}

export function validateInstalled(manifest, baseline) {
  objectKeys(manifest, ['schemaVersion', 'version', 'versions', 'source', 'payloadSha256', 'capabilitySha256', 'files', 'custom'])
  assert.equal(manifest.schemaVersion, 1, 'Unsupported installed Kit schema.')
  const expected = installedManifest(baseline)
  for (const field of ['version', 'versions', 'source', 'payloadSha256', 'capabilitySha256', 'files']) {
    assert.deepEqual(manifest[field], expected[field], `Installed ${field} differs from the official baseline; restore the exact source package.`)
  }
  assert.ok(Array.isArray(manifest.custom), 'Invalid custom adapter inventory.')
  distinctPaths(manifest.custom.map(file => file.path))
  for (const file of manifest.custom) {
    objectKeys(file, ['path', 'source', 'content', 'sha256'])
    const [, name, suffix] = customPath(file.source)
    assert.ok(['agents', 'claude'].some(agent => file.path === `.${agent}/skills/${name}/${suffix}`),
      'Custom adapter destination does not match its canonical source.')
    assert.equal(digest(textBytes(file.content)), file.sha256, 'Corrupt custom adapter baseline.')
  }
  return manifest
}

export function planKit({ baseline, incoming, installed, current, customSources = {} }) {
  validatePayload(incoming)
  if (installed) validateInstalled(installed, baseline)
  else assert.equal(baseline, null, 'A baseline requires an installed manifest.')
  const custom = customAdapters(customSources)
  const before = new Map([
    ...(baseline?.files ?? []).map(file => [file.path, file]),
    ...(installed?.custom ?? []).map(file => [file.path, { ...file, role: 'file' }]),
  ])
  const after = new Map([...incoming.files.map(file => [file.path, file]), ...Object.entries(custom)])
  const paths = [...new Set([...before.keys(), ...after.keys()])].sort()
  distinctPaths(paths)
  const source = baseline ? payloadIdentity(baseline) : null
  const target = payloadIdentity(incoming)
  const entries = paths.map(path => {
    assert.ok(Object.hasOwn(current, path), `Missing file snapshot: ${path}`)
    const previous = before.get(path)
    const next = after.get(path)
    const role = next?.role ?? previous.role
    let local = current[path]
    if (role === 'block' && local !== null) local = managedBlock(local)?.content ?? null
    const base = previous?.content ?? null
    const content = next?.content ?? null
    const state = reconcile(base, local, content)
    return {
      id: conflictIdentity(source, target, path, base, local, content),
      path,
      role,
      state,
      base,
      local,
      incoming: content,
      result: ['update', 'converged', 'unchanged'].includes(state) ? content : local,
    }
  })
  const manifest = installedManifest(incoming, custom)
  const sha256 = digest(JSON.stringify({ source, target, entries, current, customSources }))
  return { schemaVersion: 1, source, target, sha256, entries, manifest }
}

export function materializePlan(plan, current) {
  const conflicts = plan.entries.filter(entry => ['conflict', 'deleted'].includes(entry.state))
  assert.equal(conflicts.length, 0, `Kit has ${conflicts.length} unresolved conflicts; provide hash-bound resolutions.`)
  const changes = {}
  for (const entry of plan.entries) {
    const output = entry.role === 'block'
      ? replaceBlock(current[entry.path] ?? '', entry.result)
      : entry.result
    if (entry.role === 'block' && entry.result !== null) {
      assert.equal(managedBlock(entry.result)?.content, entry.result, 'A root resolution must retain exactly one managed block.')
    }
    if (output !== current[entry.path]) changes[entry.path] = output
  }
  const manifestText = `${JSON.stringify(plan.manifest, null, 2)}\n`
  if (current['ai/kit.json'] !== manifestText) changes['ai/kit.json'] = manifestText
  return changes
}

export function planSummary(plan) {
  return {
    schemaVersion: plan.schemaVersion,
    source: plan.source,
    target: plan.target,
    planSha256: plan.sha256,
    entries: plan.entries.map(entry => ({
      id: entry.id,
      path: entry.path,
      role: entry.role,
      state: entry.state,
      baseSha256: digest(entry.base),
      localSha256: digest(entry.local),
      incomingSha256: digest(entry.incoming),
      ...(['conflict', 'deleted', 'customized'].includes(entry.state)
        ? { content: { base: entry.base, local: entry.local, incoming: entry.incoming } } : {}),
    })),
  }
}
