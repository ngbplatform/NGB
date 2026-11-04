import assert from 'node:assert/strict'
import { conflictIdentity, digest, distinctPaths, objectKeys, reconcile, validatePayload } from './kit-contracts.mjs'
import { materializePlan, payloadIdentity, planKit } from './kit-plan.mjs'

const sourceToolPaths = ['infrastructure/configuration.mjs', 'infrastructure/ngb.mjs',
  'infrastructure/ngb/application.mjs', 'infrastructure/ngb/cli.mjs', 'infrastructure/ngb/contracts.mjs']

export function toolingBaseline(document) {
  objectKeys(document, ['schemaVersion', 'version', 'source', 'files'])
  assert.equal(document.schemaVersion, 1)
  assert.equal(document.version, '3.3.0')
  objectKeys(document.source, ['package', 'version', 'url', 'sha256', 'repositorySignatureSha256'])
  assert.equal(document.source.package, 'NGB.Platform.Templates')
  assert.equal(document.source.version, document.version)
  assert.equal(document.source.url,
    'https://api.nuget.org/v3-flatcontainer/ngb.platform.templates/3.3.0/ngb.platform.templates.3.3.0.nupkg')
  assert.match(document.source.sha256, /^[a-f0-9]{64}$/)
  assert.match(document.source.repositorySignatureSha256, /^[A-F0-9]{64}$/)
  assert.ok(Array.isArray(document.files))
  assert.deepEqual(document.files.map(file => file.path).sort(), [...sourceToolPaths].sort())
  for (const file of document.files) {
    objectKeys(file, ['path', 'content', 'sha256'])
    assert.ok(typeof file.content === 'string' && Buffer.byteLength(file.content) <= 1024 * 1024)
    assert.equal(digest(file.content), file.sha256, `Published CLI baseline was modified: ${file.path}`)
  }
  return document
}

export function planInitialKitUpgrade({ baseline, incoming, tooling, current, customSources = {}, artifactIdentity }) {
  toolingBaseline(baseline)
  validatePayload(incoming)
  assert.equal(incoming.version, '3.4.0', 'Only the registered 3.3.0 → 3.4.0 Kit installation is supported.')
  assert.match(artifactIdentity, /^[a-f0-9]{64}$/, 'The upgrade requires a verified target artifact identity.')
  distinctPaths(Object.keys(tooling))
  for (const [path, text] of Object.entries(tooling)) {
    assert.match(path, /^infrastructure\/(?:ngb\.mjs|configuration\.mjs|ngb\/[a-z0-9-]+\.(?:json|mjs)|validation\/[A-Za-z0-9.-]+\.(?:cs|csproj|json|runsettings))$/,
      'Upgrade tooling is outside its managed directory.')
    assert.ok(typeof text === 'string' && Buffer.byteLength(text) <= 20 * 1024 * 1024, 'Invalid incoming tooling text.')
    assert.ok(Object.hasOwn(current, path), `Missing tool snapshot: ${path}`)
  }
  for (const path of sourceToolPaths) assert.ok(Object.hasOwn(tooling, path), `Target tooling is incomplete: ${path}`)
  const kitPlan = planKit({ baseline: null, incoming, installed: null, current, customSources })
  const source = baseline.source.sha256
  const target = digest(JSON.stringify({ artifactIdentity, kit: payloadIdentity(incoming) }))
  const old = new Map(baseline.files.map(file => [file.path, file.content]))
  const entries = [...kitPlan.entries]
  for (const [path, text] of Object.entries(tooling)) {
    const base = old.get(path) ?? null
    const local = current[path]
    const reconciled = reconcile(base, local, text)
    const state = reconciled === 'customized' ? 'conflict' : reconciled
    entries.push({ path, role: 'file', state, base, local, incoming: text,
      result: ['update', 'converged', 'unchanged'].includes(state) ? text : local })
  }
  const manifest = `${JSON.stringify(kitPlan.manifest, null, 2)}\n`
  assert.ok(Object.hasOwn(current, 'ai/kit.json'), 'Missing installed-manifest snapshot.')
  entries.push({ path: 'ai/kit.json', role: 'file', state: current['ai/kit.json'] === null ? 'update' : 'conflict',
    base: null, local: current['ai/kit.json'], incoming: manifest,
    result: current['ai/kit.json'] === null ? manifest : current['ai/kit.json'] })
  entries.sort((left, right) => left.path.localeCompare(right.path, 'en'))
  distinctPaths(entries.map(entry => entry.path))
  for (const entry of entries) entry.id = conflictIdentity(source, target, entry.path, entry.base, entry.local, entry.incoming)
  const sha256 = digest(JSON.stringify({ source, target, entries, current, customSources }))
  return { schemaVersion: 1, source, target, sha256, entries, manifest: kitPlan.manifest }
}

export function materializeInitialKitUpgrade(plan, current) {
  for (const entry of plan.entries) {
    if (entry.path.startsWith('infrastructure/') || entry.path === 'ai/kit.json') {
      assert.equal(entry.result, entry.incoming,
        `The required target tool contract is customized: ${entry.path}. Preserve the customization separately and review the incoming version.`)
    }
  }
  return materializePlan(plan, current)
}
