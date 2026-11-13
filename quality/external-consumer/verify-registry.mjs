import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { mkdir, mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { basename, dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { assertArtifactIdentity, digest } from './release-contracts.mjs'
import { fetchRegistryBytes, registryTimeoutMs } from './registry-fetch.mjs'
import { buildScope, publicationOptions } from './build-release-contracts.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const options = publicationOptions(process.argv.slice(2))
const root = resolve(options.path ?? join(repository, 'artifacts/release-candidate'))
execFileSync(process.execPath, [options.verifier, options.command, root], { cwd: repository, stdio: 'inherit' })
const manifestBytes = await readFile(join(root, 'release-manifest.json'))
const manifest = JSON.parse(manifestBytes)
const downloads = await mkdtemp(join(tmpdir(), 'ngb-registry-verification-'))
// All NuGet and npm downloads share one budget; each package must not restart it.
const registryOptions = { deadline: performance.now() + registryTimeoutMs }
console.log(`[registry] Verifying published packages with a shared ${registryTimeoutMs / 60_000}-minute download deadline.`)
const results = []
for (const artifact of manifest.artifacts.filter(item => !item.path.endsWith('.snupkg'))) {
  const npm = artifact.path.endsWith('.tgz')
  let url
  if (npm) {
    const name = basename(artifact.path) === `ngbplatform-cli-${manifest.version}.tgz` ? 'cli' : 'ui'
    assert.equal(basename(artifact.path), `ngbplatform-${name}-${manifest.version}.tgz`, 'Unexpected npm artifact.')
    const metadata = JSON.parse(await fetchRegistryBytes(`https://registry.npmjs.org/@ngbplatform%2f${name}/${manifest.version}`, registryOptions))
    assert.equal(metadata.name, `@ngbplatform/${name}`)
    assert.equal(metadata.version, manifest.version)
    assert.equal(metadata.dist.integrity, artifact.integrity, 'npm registry integrity differs from the built archive.')
    url = metadata.dist.tarball
    assert.equal(url, `https://registry.npmjs.org/@ngbplatform/${name}/-/${name}-${manifest.version}.tgz`)
  } else {
    const id = basename(artifact.path).slice(0, -`.${manifest.version}.nupkg`.length).toLowerCase()
    url = `https://api.nuget.org/v3-flatcontainer/${id}/${manifest.version}/${id}.${manifest.version}.nupkg`
  }
  const bytes = await fetchRegistryBytes(url, registryOptions)
  assertArtifactIdentity(artifact, bytes, true)
  const path = join(downloads, basename(artifact.path))
  await writeFile(path, bytes)
  if (!npm) execFileSync('dotnet', ['nuget', 'verify', path, '--all'], { cwd: downloads, stdio: 'inherit' })
  results.push({ path: artifact.path, registrySha256: digest(bytes), payloadSha256: artifact.payloadSha256, integrity: artifact.integrity, status: 'passed' })
  console.log(`[registry] Verified ${artifact.path}`)
}
const details = {}
if (options.ciBuild) {
  details.scope = buildScope
} else {
  // Local registry certification restores a consumer directly from registries.
  // GitHub publication only verifies package identity and signatures.
  execFileSync(process.execPath, ['quality/external-consumer/certify.mjs', 'clean-starter', root], {
    cwd: repository, env: { ...process.env, NGB_REGISTRY_ONLY: 'true' }, stdio: 'inherit',
  })
  const smoke = await readFile(join(root, 'certification/registry-clean-starter.json'))
  assert.equal(JSON.parse(smoke).status, 'passed')
  details.smokeSha256 = digest(smoke)
}
await mkdir(join(root, 'certification'), { recursive: true })
const identityPath = 'certification/registry-identities.json'
const identities = Buffer.from(`${JSON.stringify({ schemaVersion: 1, status: 'passed', results, ...details }, null, 2)}\n`)
await writeFile(join(root, identityPath), identities)
const publication = {
  schemaVersion: 1, stage: 'publication', artifactManifestSha256: digest(manifestBytes),
  ...(options.ciBuild ? { scope: buildScope } : {}),
  results: { 'publish-identity': { status: 'passed', path: identityPath, evidenceSha256: digest(identities) } },
}
await writeFile(join(root, 'publication-evidence.json'), `${JSON.stringify(publication, null, 2)}\n`)
await writeFile(join(root, 'promotion-evidence.json'), `${JSON.stringify({
  ...publication, stage: 'promotion',
  results: { 'registry-verification': { status: 'passed', path: identityPath, evidenceSha256: digest(identities) } },
}, null, 2)}\n`)
console.log(options.ciBuild
  ? 'Registry package identities and signatures passed. Release promotion is permitted.'
  : 'Registry identity, signatures and independent runtime smoke passed. Release promotion is permitted.')
