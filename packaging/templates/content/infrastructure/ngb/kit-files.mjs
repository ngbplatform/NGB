import assert from 'node:assert/strict'
import { digest, maximumAssetBytes } from './kit-contracts.mjs'
import { readAsset } from './file-data.mjs'
import { applyTransaction } from './transaction.mjs'

export { assetPaths, readAsset, readBytes, safePath } from './file-data.mjs'

export async function assertSnapshot(root, snapshot) {
  for (const [path, expected] of Object.entries(snapshot)) {
    assert.ok(expected === null || typeof expected === 'string', 'Invalid text snapshot.')
    const size = Buffer.byteLength(expected ?? '')
    assert.ok(size <= 32 * 1024 * 1024, 'Snapshot file exceeds its size limit.')
    assert.equal(digest(await readAsset(root, path, Math.max(maximumAssetBytes, size))), digest(expected), `Concurrent change detected: ${path}`)
  }
}

export async function applyAssets(root, snapshot, changes, identity, beforeWrite = async () => {}) {
  await assertSnapshot(root, snapshot)
  const binary = files => Object.fromEntries(Object.entries(files).map(([path, text]) => [path,
    text === null ? null : Buffer.from(text)]))
  return await applyTransaction(root, binary(snapshot), binary(changes), identity, { beforeWrite })
}
