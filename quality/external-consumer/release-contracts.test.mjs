import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'
import { strToU8, zipSync } from 'fflate'
import {
  artifactIdentity, assertArtifactIdentity, assertArtifactInventory, canonicalNugetPayload, digest, validateMatrix,
} from './release-contracts.mjs'

const zip = entries => zipSync(Object.fromEntries(Object.entries(entries).map(([name, text]) => [name, strToU8(text)])))

test('signed NuGet identity ignores only the signature and archive representation', () => {
  const original = zip({ 'A.nuspec': '<package />', 'lib/net10.0/A.dll': 'assembly', 'empty/': '' })
  const signed = zip({ '.signature.p7s': 'registry signature', 'lib/net10.0/A.dll': 'assembly', 'A.nuspec': '<package />' })
  const expected = artifactIdentity('nuget-release/A.3.2.0.nupkg', original)
  assert.equal(canonicalNugetPayload(original), canonicalNugetPayload(signed))
  assertArtifactIdentity(expected, original)
  assertArtifactIdentity(expected, signed, true)
  assert.throws(() => assertArtifactIdentity(expected, signed), /bytes differ/)
  assert.throws(() => assertArtifactIdentity(expected, zip({ 'A.nuspec': '<package />', 'lib/net10.0/A.dll': 'changed' }), true), /payload differs/)
  assert.throws(() => canonicalNugetPayload(zip({})), /empty/)
  assert.throws(() => canonicalNugetPayload(zip({ a: 'a' })), /nuspec/)
  for (const path of ['/root', '../escape', 'lib\\escape']) {
    assert.throws(() => canonicalNugetPayload(zip({ 'A.nuspec': 'a', [path]: 'b' })), /Unsafe/)
  }
  assert.throws(() => artifactIdentity('../outside.tgz', original), /path/)
})

test('npm requires exact bytes and SHA-512 integrity; symbols require exact bytes', () => {
  const npm = artifactIdentity('npm/ngbplatform-ui-3.2.0.tgz', Buffer.from('npm'))
  assert.equal(npm.integrity, `sha512-${digest('npm', 'sha512', 'base64')}`)
  assertArtifactIdentity(npm, Buffer.from('npm'), true)
  assert.throws(() => assertArtifactIdentity(npm, Buffer.from('other'), true))
  assert.throws(() => assertArtifactIdentity({ ...npm, integrity: 'sha512-wrong' }, Buffer.from('npm')))
  const symbols = zip({ 'A.nuspec': 'symbols' })
  assertArtifactIdentity(artifactIdentity('nuget-release/A.3.2.0.snupkg', symbols), symbols, true)
})

test('artifact inventory classifies templates separately and rejects missing or extra artifacts', () => {
  const inventory = { target: '3.2.0', packages: [{ id: 'A', kind: 'runtime' }, { id: 'T', kind: 'template' }] }
  const manifest = { schemaVersion: 1, version: '3.2.0', artifacts: [
    'nuget-release/A.3.2.0.nupkg', 'nuget-release/A.3.2.0.snupkg', 'nuget-release/T.3.2.0.nupkg', 'npm/ngbplatform-ui-3.2.0.tgz',
  ].map(path => ({ path })) }
  assertArtifactInventory(manifest, inventory)
  assert.throws(() => assertArtifactInventory({ ...manifest, artifacts: manifest.artifacts.slice(1) }, inventory))
  assert.throws(() => assertArtifactInventory({ ...manifest, artifacts: [...manifest.artifacts, { path: 'extra' }] }, inventory))
  assert.throws(() => assertArtifactInventory({ ...manifest, version: '3.3.0' }, inventory))
})

test('matrix binds all mandatory gates, stages, exact versions and profiles', async () => {
  const matrix = JSON.parse(await readFile(new URL('./matrix.json', import.meta.url), 'utf8'))
  validateMatrix(matrix)
  for (const mutate of [
    value => { value.source = '^3.1.0' },
    value => { value.source = value.target },
    value => { delete value.profiles['clean-starter'] },
    value => { value.gates.pop() },
    value => { value.gates[0].blocks = 'anything' },
    value => { value.gates[0].blocks = 'promotion' },
    value => { value.gates[0].command = '' },
    value => { value.gates[0].assertions = [] },
    value => { value.profiles['clean-starter'].assertions = [] },
    value => { value.profiles['clean-starter'].target = '3.3.0' },
    value => { value.profiles['core-upgrade'].source = '3.0.0' },
    value => { value.profiles['notes-upgrade'].minio = true },
    value => { value.profiles['attachments-upgrade'].minio = false },
  ]) {
    const invalid = structuredClone(matrix)
    mutate(invalid)
    assert.throws(() => validateMatrix(invalid))
  }
})
