import assert from 'node:assert/strict'
import test from 'node:test'
import { assertReferenceIdentityRefresh } from './contract-references.mjs'

test('package and assembly identities can refresh while public contracts stay identical', () => {
  const before = { schemaVersion: 1, version: '3.4.0', package: 'NGB.Platform.Test',
    packageSha256: 'old-package', assembly: { name: 'NGB.Test', version: '3.0.0.0', sha256: 'old-assembly' },
    contractSha256: 'contract', types: [{ name: 'Record', methods: ['Read'], contractSha256: 'type' }] }
  const after = structuredClone(before)
  after.packageSha256 = 'new-package'
  after.assembly.sha256 = 'new-assembly'
  assertReferenceIdentityRefresh(before, after)
  assert.equal(before.packageSha256, 'old-package')
  assert.equal(before.assembly.sha256, 'old-assembly')

  for (const change of [
    value => { value.version = '3.5.0' },
    value => { value.assembly.version = '4.0.0.0' },
    value => { value.contractSha256 = 'different-contract' },
    value => { value.types[0].methods.push('Write') },
    value => { value.types.push({ name: 'NewType', methods: [] }) },
  ]) {
    const changed = structuredClone(after)
    change(changed)
    assert.throws(() => assertReferenceIdentityRefresh(before, changed), /Public contracts changed/)
  }
})

test('UI identity refresh preserves export names, paths and content hashes', () => {
  const before = { package: '@ngbplatform/ui', version: '3.4.0', packageSha256: 'old-package',
    exports: [{ subpath: '.', files: [{ path: './index.ts', sha256: 'content' }] }] }
  const after = { ...structuredClone(before), packageSha256: 'new-package' }
  assertReferenceIdentityRefresh(before, after)
  after.exports[0].files[0].sha256 = 'different-content'
  assert.throws(() => assertReferenceIdentityRefresh(before, after), /Public contracts changed/)
})

test('reference index refresh preserves its inventory, toolchain and public surface', () => {
  const before = { schemaVersion: 1, version: '3.4.0', sdk: '10.0.401',
    packages: [{ package: 'NGB.Platform.Test', contractSha256: 'contract' }],
    files: { 'test.json': 'old-package', 'ui.json': 'old-ui' } }
  const after = { ...structuredClone(before), files: { 'test.json': 'new-package', 'ui.json': 'new-ui' } }
  assertReferenceIdentityRefresh(before, after)
  assert.equal(before.files['test.json'], 'old-package')
  delete after.files['ui.json']
  assert.throws(() => assertReferenceIdentityRefresh(before, after), /Public reference inventory changed/)
  after.files = { ...before.files }
  after.sdk = 'other-sdk'
  assert.throws(() => assertReferenceIdentityRefresh(before, after), /Public contracts changed/)
})
