import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { posix } from 'node:path'
import { unzipSync } from 'fflate'
import { assertExactVersion, hashDirectory } from './contracts.mjs'

export const templateContentExcludes = ['**/bin/**', '**/obj/**', '**/node_modules/**', '**/dist/**', '.env', '.local/**']

export const requiredProfiles = ['clean-starter', 'generated-extension', 'core-upgrade', 'notes-upgrade', 'attachments-upgrade']
export const requiredGates = [
  'template-artifact', 'external-isolation', 'clean-starter', 'starter-job', 'administrator-contract',
  'generated-extension', 'frontend-artifact', 'nuget-compatibility', 'npm-compatibility', 'source-fixture',
  'core-upgrade', 'upgrade-job', 'migration-sequencing', 'notes-upgrade', 'attachments-upgrade',
  'backend-quality', 'frontend-quality', 'regression-quality', 'tooling-quality', 'candidate-evidence',
  'publish-identity', 'registry-verification',
]

export function digest(bytes, algorithm = 'sha256', encoding = 'hex') {
  return createHash(algorithm).update(bytes).digest(encoding)
}

export function validateMatrix(matrix) {
  assert.equal(matrix.schemaVersion, 1)
  assertExactVersion(matrix.source)
  assertExactVersion(matrix.target)
  assert.notEqual(matrix.source, matrix.target)
  assert.deepEqual(Object.keys(matrix.profiles).sort(), [...requiredProfiles].sort())
  assert.deepEqual(matrix.gates.map(gate => gate.id).sort(), [...requiredGates].sort())
  for (const gate of matrix.gates) {
    assert.ok(['candidate', 'publication', 'promotion'].includes(gate.blocks), `Invalid gate stage: ${gate.id}`)
    const expectedStage = gate.id === 'publish-identity' ? 'publication'
      : gate.id === 'registry-verification' ? 'promotion' : 'candidate'
    assert.equal(gate.blocks, expectedStage, `A required gate cannot be deferred: ${gate.id}`)
    assert.ok(typeof gate.command === 'string' && gate.command.trim().length > 0, `Missing command: ${gate.id}`)
    assert.ok(gate.assertions.length > 0, `Missing assertions: ${gate.id}`)
  }
  for (const [id, profile] of Object.entries(matrix.profiles)) {
    assert.ok(profile.assertions.length > 0)
    assert.equal(profile.target, matrix.target)
    assert.equal(profile.source, id.endsWith('-upgrade') ? matrix.source : null)
    assert.equal(profile.minio, id === 'attachments-upgrade')
  }
  assert.equal(matrix.profiles['notes-upgrade'].minio, false)
  assert.equal(matrix.profiles['attachments-upgrade'].minio, true)
}

export function canonicalNugetPayload(archive) {
  const entries = unzipSync(archive)
  const payload = Object.keys(entries).filter(name => name !== '.signature.p7s' && !name.endsWith('/')).sort()
  assert.ok(payload.length > 0, 'NuGet payload is empty.')
  assert.equal(payload.filter(name => name.endsWith('.nuspec')).length, 1, 'Exactly one nuspec is required.')
  for (const name of payload) {
    assert.ok(!name.startsWith('/') && !name.includes('\\') && !name.split('/').includes('..'), `Unsafe package path: ${name}`)
  }
  return digest(JSON.stringify(payload.map(name => [name, digest(entries[name])])))
}

export async function assertTemplatePackage(archive, directory) {
  const expected = await hashDirectory(directory, templateContentExcludes)
  assert.ok(Object.keys(expected).length > 0, 'Template source directory is empty.')
  const seen = new Set()
  const entries = unzipSync(archive, {
    filter: ({ name }) => {
      if (!name.startsWith('content/') || name.endsWith('/')) return false
      assert.ok(!name.includes('\\') && !name.split('/').includes('..') && posix.normalize(name) === name,
        `Unsafe template package path: ${name}`)
      assert.ok(!seen.has(name), `Duplicate template package path: ${name}`)
      seen.add(name)
      return true
    },
  })
  const actual = Object.fromEntries(Object.entries(entries).map(([name, bytes]) => [name.slice('content/'.length), digest(bytes)]))
  const paths = new Set([...Object.keys(expected), ...Object.keys(actual)])
  const differences = [...paths].filter(path => expected[path] !== actual[path]).sort()
  assert.equal(differences.length, 0,
    `Template package does not match current sources: ${differences.join(', ')}. Run bash packaging/nuget/pack-platform.sh before sealing a new candidate.`)
}

export function artifactIdentity(path, bytes) {
  assert.ok(/^(?:nuget-release\/[^/]+\.(?:nupkg|snupkg)|npm\/[^/]+\.tgz)$/.test(path), `Invalid artifact path: ${path}`)
  const identity = { path, sha256: digest(bytes), size: bytes.length }
  if (path.endsWith('.tgz')) identity.integrity = `sha512-${digest(bytes, 'sha512', 'base64')}`
  else identity.payloadSha256 = canonicalNugetPayload(bytes)
  return identity
}

export function assertArtifactIdentity(expected, bytes, registry = false) {
  const actual = artifactIdentity(expected.path, bytes)
  if (registry && expected.path.endsWith('.nupkg')) {
    assert.equal(actual.payloadSha256, expected.payloadSha256, `Published NuGet payload differs: ${expected.path}`)
  } else {
    assert.equal(actual.sha256, expected.sha256, `Artifact bytes differ: ${expected.path}`)
  }
  if (expected.integrity) assert.equal(actual.integrity, expected.integrity)
}

export function assertArtifactInventory(manifest, inventory) {
  assert.equal(manifest.schemaVersion, 1)
  assertExactVersion(manifest.version)
  assert.equal(manifest.version, inventory.target)
  const paths = inventory.packages.flatMap(item => [
    `nuget-release/${item.id}.${manifest.version}.nupkg`,
    ...(item.kind === 'runtime' ? [`nuget-release/${item.id}.${manifest.version}.snupkg`] : []),
  ])
  paths.push(`npm/ngbplatform-ui-${manifest.version}.tgz`)
  assert.deepEqual(manifest.artifacts.map(item => item.path).sort(), paths.sort(), 'Artifact inventory is incomplete or contains extras.')
}
