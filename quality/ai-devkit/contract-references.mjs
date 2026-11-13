import assert from 'node:assert/strict'

export function assertReferenceIdentityRefresh(previous, incoming) {
  const expected = structuredClone(previous)
  if (incoming.files) {
    assert.deepEqual(Object.keys(previous.files).sort(), Object.keys(incoming.files).sort(),
      'Public reference inventory changed. Review the public contracts before refreshing references.')
    expected.files = incoming.files
  } else {
    expected.packageSha256 = incoming.packageSha256
    if (incoming.assembly) expected.assembly.sha256 = incoming.assembly.sha256
  }
  assert.deepEqual(expected, incoming,
    'Public contracts changed. Review affected capabilities before explicitly regenerating references with --write.')
}
