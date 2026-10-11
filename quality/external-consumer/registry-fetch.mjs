import assert from 'node:assert/strict'

export async function fetchRegistryBytes(url, attempts = 6, delayMs = 10_000) {
  assert.ok(Number.isInteger(attempts) && attempts > 0 && attempts <= 10)
  assert.ok(delayMs >= 0 && delayMs <= 60_000)
  let failure
  for (let attempt = 0; attempt < attempts; attempt += 1) {
    let response
    try {
      response = await fetch(url, { signal: AbortSignal.timeout(30_000) })
      if (response.ok) return Buffer.from(await response.arrayBuffer())
    } catch (error) {
      failure = error
    }
    if (response) {
      failure = new Error(`Registry returned ${response.status}: ${url}`)
      if (![404, 408, 429, 500, 502, 503, 504].includes(response.status)) throw failure
    }
    if (attempt + 1 < attempts) await new Promise(resolve => setTimeout(resolve, delayMs))
  }
  throw failure
}
