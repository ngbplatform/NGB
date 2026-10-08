import assert from 'node:assert/strict'
import test from 'node:test'
import { fetchRegistryBytes } from './registry-fetch.mjs'

test('registry retries only transient transport and propagation failures within a fixed budget', async t => {
  const fetch = t.mock.method(globalThis, 'fetch', async () => new Response('archive'))
  assert.equal((await fetchRegistryBytes('https://registry.test/package')).toString(), 'archive')
  let attempts = 0
  fetch.mock.mockImplementation(async () => {
    attempts += 1
    if (attempts === 1) throw new TypeError('connection closed')
    if (attempts === 2) return new Response(null, { status: 404 })
    return new Response('propagated')
  })
  assert.equal((await fetchRegistryBytes('https://registry.test/package', 3, 0)).toString(), 'propagated')
  assert.equal(attempts, 3)
  fetch.mock.mockImplementation(async () => new Response(null, { status: 503 }))
  await assert.rejects(fetchRegistryBytes('https://registry.test/package', 2, 0), /503/)
  fetch.mock.mockImplementation(async () => new Response(null, { status: 401 }))
  const before = fetch.mock.callCount()
  await assert.rejects(fetchRegistryBytes('https://registry.test/package', 3, 0), /401/)
  assert.equal(fetch.mock.callCount(), before + 1)
  for (const count of [0, 11, 1.5]) await assert.rejects(fetchRegistryBytes('https://registry.test/package', count, 0))
  await assert.rejects(fetchRegistryBytes('https://registry.test/package', 1, -1))
  await assert.rejects(fetchRegistryBytes('https://registry.test/package', 1, 60_001))
})
