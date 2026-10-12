import assert from 'node:assert/strict'

export const registryTimeoutMs = 20 * 60_000

function retryAfterMs(response) {
  const value = response?.headers.get('retry-after')
  if (!value) return 0
  const delay = /^\d+$/.test(value) ? Number(value) * 1_000 : Date.parse(value) - Date.now()
  return Number.isFinite(delay) ? Math.max(0, delay) : 0
}

export async function fetchRegistryBytes(url, { deadline = performance.now() + registryTimeoutMs } = {}) {
  assert.ok(Number.isFinite(deadline), 'The registry deadline must be finite.')
  let failure
  for (let attempt = 1; performance.now() < deadline; attempt += 1) {
    const timeoutMs = Math.max(1, Math.ceil(Math.min(30_000, deadline - performance.now())))
    let response
    try {
      response = await fetch(url, { signal: AbortSignal.timeout(timeoutMs) })
      if (response.ok) return Buffer.from(await response.arrayBuffer())
      await response.body?.cancel()
    } catch (error) {
      failure = error
    }
    if (response && !response.ok) {
      failure = new Error(`Registry returned ${response.status}: ${url}`)
      if (![404, 408, 429, 500, 502, 503, 504].includes(response.status)) throw failure
    }
    const remainingMs = deadline - performance.now()
    if (remainingMs <= 0) break
    const delayMs = Math.min(remainingMs, Math.max(15_000, retryAfterMs(response)))
    console.warn(`[registry] ${failure.message} (attempt ${attempt}); retry in ${Math.ceil(delayMs / 1_000)}s, `
      + `${Math.ceil(remainingMs / 1_000)}s remaining.`)
    await new Promise(resolve => setTimeout(resolve, delayMs))
  }
  throw new Error(`Timed out waiting for registry package: ${url}. `
    + 'Check registry processing/status and rerun publication with the same certified artifacts.', { cause: failure })
}
