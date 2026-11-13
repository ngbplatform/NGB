import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { artifactIdentity } from './release-contracts.mjs'
import {
  assertBuildManifest, assertBuildPublication, buildProvenance, buildScope, packagePaths,
} from './build-release-contracts.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const [action, directory] = process.argv.slice(2)
assert.ok(['seal', 'verify', 'verify-publication', 'verify-promotion'].includes(action), 'Unknown build release command.')
assert.ok(process.argv.length <= 4, 'Unexpected build release arguments.')
const root = resolve(directory ?? join(repository, 'artifacts/release-candidate'))
const manifestPath = join(root, 'release-manifest.json')
const inventory = JSON.parse(await readFile(join(repository, 'packaging/nuget/baselines.json')))
const git = value => execFileSync('git', ['rev-parse', value], { cwd: repository, encoding: 'utf8' }).trim()
const commit = git('HEAD')
const tree = git('HEAD^{tree}')

if (action === 'seal') {
  const build = buildProvenance(process.env, commit)
  // The runner builds generated payloads from this commit. Their identities are
  // recorded separately from Git's source tree, without importing local reports.
  const artifacts = []
  for (const path of packagePaths(inventory)) {
    const bytes = await readFile(join(repository, 'artifacts', path))
    artifacts.push(artifactIdentity(path, bytes))
  }
  await mkdir(root)
  for (const artifact of artifacts) {
    const path = join(root, artifact.path)
    await mkdir(dirname(path), { recursive: true })
    const bytes = await readFile(join(repository, 'artifacts', artifact.path))
    assert.deepEqual(artifactIdentity(artifact.path, bytes), artifact, 'Package changed while recording the build.')
    await writeFile(path, bytes, { flag: 'wx' })
  }
  await writeFile(manifestPath, `${JSON.stringify({
    schemaVersion: 1, scope: buildScope, version: inventory.target,
    sourceCommit: commit, sourceGitTree: tree, build, artifacts,
  }, null, 2)}\n`, { flag: 'wx' })
}

const manifestBytes = await readFile(manifestPath)
const manifest = JSON.parse(manifestBytes)
if (process.env.NGB_RELEASE_COMMIT !== undefined) assert.equal(commit, process.env.NGB_RELEASE_COMMIT)
assertBuildManifest(manifest, inventory, {
  commit, tree, repository: process.env.GITHUB_REPOSITORY,
  runId: process.env.NGB_BUILD_RUN_ID, runAttempt: process.env.NGB_BUILD_RUN_ATTEMPT,
})
for (const artifact of manifest.artifacts) {
  const bytes = await readFile(join(root, artifact.path))
  assert.deepEqual(artifactIdentity(artifact.path, bytes), artifact, `Build package differs: ${artifact.path}`)
}

if (action === 'verify-publication' || action === 'verify-promotion') {
  const stages = action === 'verify-promotion' ? ['publication', 'promotion'] : ['publication']
  const receipt = JSON.parse(await readFile(join(root, 'upload-receipt.json')))
  const identities = await readFile(join(root, 'certification/registry-identities.json'))
  for (const stage of stages) {
    const evidence = JSON.parse(await readFile(join(root, `${stage}-evidence.json`)))
    assertBuildPublication(manifestBytes, receipt, evidence, identities, stage)
  }
}
console.log(`GitHub package build ${action}: passed (${root}).`)
