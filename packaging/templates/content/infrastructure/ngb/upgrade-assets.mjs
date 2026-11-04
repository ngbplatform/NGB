import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { basename, dirname, resolve } from 'node:path'
import { acquireCli } from './acquisition.mjs'
import { verifyCliArchive } from './archive.mjs'
import { customAdapters } from './kit-plan.mjs'
import { digest, resolveConflicts } from './kit-contracts.mjs'
import { readAsset, readBytes } from './kit-files.mjs'
import { planInitialKitUpgrade } from './upgrade-plan.mjs'

export async function initialUpgradeAssets(root, original, dependencies, candidate, signal) {
  const acquired = await acquireCli(dependencies.target, { candidate, applicationRoot: root, signal })
  const cache = resolve(dirname(acquired.entry), '../..')
  const archive = await readBytes(cache, 'cli.tgz', 16 * 1024 * 1024)
  assert.ok(archive, 'Verified target CLI archive disappeared during upgrade planning.')
  assert.equal(`sha512-${createHash('sha512').update(archive).digest('base64')}`, acquired.integrity,
    'The acquired CLI archive changed during upgrade planning.')
  const { files } = verifyCliArchive(archive, dependencies.target)
  const tooling = {}
  for (const [path, bytes] of files) {
    if (!path.startsWith('package/infrastructure/')) continue
    const text = bytes.toString('utf8')
    assert.ok(Buffer.from(text).equals(bytes), `Tooling must be UTF-8 text: ${path}`)
    tooling[path.slice('package/'.length)] = text
  }
  const incoming = JSON.parse(tooling['infrastructure/ngb/kit-payload.json'])
  const baseline = JSON.parse(tooling['infrastructure/ngb/baseline-3-3-0.json'])
  const customSources = {}
  const textAt = path => {
    const bytes = original[path]
    if (!bytes) return null
    const text = bytes.toString('utf8')
    assert.ok(Buffer.from(text).equals(bytes), `Managed path contains binary user data; preserve it before upgrading: ${path}`)
    return text
  }
  for (const path of Object.keys(original).filter(path => path.startsWith('ai/custom/skills/'))) {
    customSources[path] = textAt(path)
  }
  const paths = new Set(['ai/kit.json', ...incoming.files.map(file => file.path), ...Object.keys(tooling),
    ...Object.keys(customAdapters(customSources))])
  const current = Object.fromEntries([...paths].sort().map(path => [path, textAt(path)]))
  const artifactIdentity = candidate?.manifestSha256 ?? digest(acquired.integrity)
  const plan = planInitialKitUpgrade({ baseline, incoming, tooling, current, customSources, artifactIdentity })
  const sourceFiles = Object.entries(original)
    .filter(([path]) => /\.(?:csproj|props|targets|slnx?)$/.test(path) || path.endsWith('/packages.lock.json')
      || ['web/package.json', 'web/package-lock.json', 'ngb.config.json', '.gitignore'].includes(path))
    .map(([path, bytes]) => [path, digest(bytes)])
    .sort(([left], [right]) => left.localeCompare(right, 'en'))
  plan.sha256 = digest(JSON.stringify({ kit: plan.sha256, dependencies, sourceFiles }))
  return { plan, current, acquired }
}

export async function reviewedUpgradePlan(plan, path) {
  if (!path) return plan
  const absolute = resolve(path)
  const text = await readAsset(dirname(absolute), basename(absolute))
  assert.ok(text, 'Upgrade resolution file is missing.')
  return resolveConflicts(plan, JSON.parse(text))
}
