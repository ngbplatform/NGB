import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { mkdir, mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { basename, dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { assertArtifactIdentity, digest } from './release-contracts.mjs'
import { fetchRegistryBytes } from './registry-fetch.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const root = resolve(process.argv[2] ?? join(repository, 'artifacts/release-candidate'))
execFileSync(process.execPath, ['quality/external-consumer/release.mjs', 'verify-candidate', root], { cwd: repository, stdio: 'inherit' })
const manifestBytes = await readFile(join(root, 'release-manifest.json'))
const manifest = JSON.parse(manifestBytes)
const downloads = await mkdtemp(join(tmpdir(), 'ngb-registry-verification-'))
const results = []
for (const artifact of manifest.artifacts.filter(item => !item.path.endsWith('.snupkg'))) {
  const npm = artifact.path.endsWith('.tgz')
  let url
  if (npm) {
    const metadata = JSON.parse(await fetchRegistryBytes(`https://registry.npmjs.org/@ngbplatform%2fui/${manifest.version}`))
    assert.equal(metadata.version, manifest.version)
    assert.equal(metadata.dist.integrity, artifact.integrity, 'npm registry integrity differs from the certified archive.')
    url = metadata.dist.tarball
    assert.equal(new URL(url).origin, 'https://registry.npmjs.org')
  } else {
    const id = basename(artifact.path).slice(0, -`.${manifest.version}.nupkg`.length).toLowerCase()
    url = `https://api.nuget.org/v3-flatcontainer/${id}/${manifest.version}/${id}.${manifest.version}.nupkg`
  }
  const bytes = await fetchRegistryBytes(url)
  assertArtifactIdentity(artifact, bytes, true)
  const path = join(downloads, basename(artifact.path))
  await writeFile(path, bytes)
  if (!npm) execFileSync('dotnet', ['nuget', 'verify', path, '--all'], { cwd: downloads, stdio: 'inherit' })
  results.push({ path: artifact.path, registrySha256: digest(bytes), payloadSha256: artifact.payloadSha256, integrity: artifact.integrity, status: 'passed' })
}
// The consumer restores directly from registries with fresh caches. Downloaded
// archives above are used only for identity/signature verification, never as feeds.
execFileSync(process.execPath, ['quality/external-consumer/certify.mjs', 'clean-starter', root], {
  cwd: repository, env: { ...process.env, NGB_REGISTRY_ONLY: 'true' }, stdio: 'inherit',
})
const smoke = await readFile(join(root, 'certification/registry-clean-starter.json'))
assert.equal(JSON.parse(smoke).status, 'passed')
await mkdir(join(root, 'certification'), { recursive: true })
const identityPath = 'certification/registry-identities.json'
const identities = Buffer.from(`${JSON.stringify({ schemaVersion: 1, status: 'passed', results, smokeSha256: digest(smoke) }, null, 2)}\n`)
await writeFile(join(root, identityPath), identities)
const publication = {
  schemaVersion: 1, stage: 'publication', artifactManifestSha256: digest(manifestBytes),
  results: { 'publish-identity': { status: 'passed', path: identityPath, evidenceSha256: digest(identities) } },
}
await writeFile(join(root, 'publication-evidence.json'), `${JSON.stringify(publication, null, 2)}\n`)
await writeFile(join(root, 'promotion-evidence.json'), `${JSON.stringify({
  ...publication, stage: 'promotion',
  results: { 'registry-verification': { status: 'passed', path: identityPath, evidenceSha256: digest(identities) } },
}, null, 2)}\n`)
console.log('Registry identity, signatures and independent runtime smoke passed. Release promotion is permitted.')
