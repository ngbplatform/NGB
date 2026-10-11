import assert from 'node:assert/strict'
import { execFileSync, spawnSync } from 'node:child_process'
import { readFile, writeFile } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { digest } from './release-contracts.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const root = resolve(process.argv[2] ?? join(repository, 'artifacts/release-candidate'))
execFileSync(process.execPath, ['quality/external-consumer/release.mjs', 'verify-candidate', root], { cwd: repository, stdio: 'inherit' })
assert.ok(process.env.NUGET_API_KEY, 'A short-lived NuGet credential is required.')
const bytes = await readFile(join(root, 'release-manifest.json'))
const manifest = JSON.parse(bytes)
for (const artifact of manifest.artifacts.filter(item => item.path.endsWith('.nupkg'))) {
  // The adjacent certified .snupkg is pushed by NuGet with the runtime package.
  const pushed = spawnSync('dotnet', [
    'nuget', 'push', join(root, artifact.path), '--api-key', process.env.NUGET_API_KEY,
    '--source', 'https://api.nuget.org/v3/index.json',
    '--symbol-api-key', process.env.NUGET_API_KEY, '--symbol-source', 'https://api.nuget.org/v3/index.json',
    '--skip-duplicate', '--timeout', '600', '--configfile', join(repository, 'NuGet.Registry.Config'),
  ], { cwd: root, stdio: 'inherit' })
  // Do not propagate child_process errors: their command text contains the key.
  assert.equal(pushed.status, 0, `NuGet upload failed: ${artifact.path}`)
}
const npm = manifest.artifacts.find(item => item.path.endsWith('.tgz'))
const metadata = await fetch(`https://registry.npmjs.org/@ngbplatform%2fui/${manifest.version}`, { signal: AbortSignal.timeout(30_000) })
if (metadata.ok) {
  assert.equal((await metadata.json()).dist.integrity, npm.integrity, 'Existing npm version differs; publish a corrected new version.')
} else {
  assert.equal(metadata.status, 404, 'Unable to determine whether the npm version already exists.')
  execFileSync('npm', ['publish', join(root, npm.path), '--access', 'public'], { cwd: root, stdio: 'inherit' })
}
await writeFile(join(root, 'upload-receipt.json'), `${JSON.stringify({
  schemaVersion: 1, artifactManifestSha256: digest(bytes), status: 'uploaded-awaiting-registry-verification',
  artifacts: manifest.artifacts.map(({ path, sha256 }) => ({ path, sha256 })),
}, null, 2)}\n`)
