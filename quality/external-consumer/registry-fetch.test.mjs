import assert from 'node:assert/strict'
import test from 'node:test'
import { fetchRegistryBytes } from './registry-fetch.mjs'

const url = 'https://registry.test/package'
const timeoutMs = 20 * 60_000

function fixture(t) {
  const state = { now: 0, delays: [], timeouts: [], messages: [] }
  t.mock.method(performance, 'now', () => state.now)
  t.mock.method(Date, 'now', () => Date.parse('2026-10-11T20:00:00Z') + state.now)
  t.mock.method(globalThis, 'setTimeout', (callback, delay) => {
    state.delays.push(delay)
    state.now += delay
    callback()
  })
  t.mock.method(AbortSignal, 'timeout', timeout => {
    state.timeouts.push(timeout)
    return new AbortController().signal
  })
  t.mock.method(console, 'warn', message => state.messages.push(message))
  const fetch = t.mock.method(globalThis, 'fetch', async () => new Response('archive'))
  return { state, fetch }
}

test('available packages download immediately without propagation waits', async t => {
  const { state, fetch } = fixture(t)
  assert.equal((await fetchRegistryBytes(url)).toString(), 'archive')
  assert.equal(fetch.mock.callCount(), 1)
  assert.deepEqual(state.delays, [])
  assert.deepEqual(state.messages, [])
  assert.deepEqual(state.timeouts, [30_000])
})

test('NuGet propagation can take fifteen minutes without failing after six attempts', async t => {
  const { state, fetch } = fixture(t)
  fetch.mock.mockImplementation(async () => state.now < 15 * 60_000
    ? new Response(null, { status: 404 })
    : new Response('published'))
  assert.equal((await fetchRegistryBytes(url)).toString(), 'published')
  assert.equal(state.now, 15 * 60_000)
  assert.equal(fetch.mock.callCount(), 61)
  assert.ok(state.delays.every(delay => delay === 15_000))
  assert.match(state.messages[0], /404.*registry\.test\/package/)
  assert.match(state.messages[0], /retry in 15s.*1200s remaining/)
})

test('missing packages exhaust one twenty-minute budget with an actionable error', async t => {
  const { state, fetch } = fixture(t)
  fetch.mock.mockImplementation(async () => new Response(null, { status: 404 }))
  await assert.rejects(fetchRegistryBytes(url), error => {
    assert.match(error.message, /Timed out waiting for registry package/)
    assert.match(error.message, /same certified artifacts/)
    assert.match(error.cause.message, /404/)
    return true
  })
  assert.equal(state.now, timeoutMs)
  assert.equal(fetch.mock.callCount(), 80)
  assert.equal(state.timeouts.at(-1), 15_000)
})

test('all registry downloads share the deadline instead of receiving twenty minutes each', async t => {
  const { state, fetch } = fixture(t)
  const options = { deadline: state.now + timeoutMs }
  fetch.mock.mockImplementation(async request => {
    if (request.endsWith('/first') && state.now >= 16 * 60_000) return new Response('first')
    return new Response(null, { status: 404 })
  })
  assert.equal((await fetchRegistryBytes(`${url}/first`, options)).toString(), 'first')
  await assert.rejects(fetchRegistryBytes(`${url}/second`, options), /Timed out/)
  const before = fetch.mock.callCount()
  await assert.rejects(fetchRegistryBytes(`${url}/third`, options), /Timed out/)
  assert.equal(fetch.mock.callCount(), before)
  assert.equal(state.now, timeoutMs)
})

test('transient HTTP failures and broken connections are retried', async t => {
  const { state, fetch } = fixture(t)
  const failures = [404, 408, 429, 500, 502, 503, 504, new TypeError('connection closed')]
  fetch.mock.mockImplementation(async () => {
    const failure = failures.shift()
    if (failure instanceof Error) throw failure
    return failure ? new Response(null, { status: failure }) : new Response('recovered')
  })
  assert.equal((await fetchRegistryBytes(url)).toString(), 'recovered')
  assert.equal(fetch.mock.callCount(), 9)
  assert.equal(state.delays.length, 8)
})

test('permanent HTTP failures stop immediately and release their response bodies', async t => {
  const { state, fetch } = fixture(t)
  for (const status of [400, 401, 403, 410, 422]) {
    const response = new Response('registry error', { status })
    const cancel = t.mock.method(response.body, 'cancel', async () => {})
    fetch.mock.mockImplementation(async () => response)
    const before = fetch.mock.callCount()
    await assert.rejects(fetchRegistryBytes(url), new RegExp(`Registry returned ${status}`))
    assert.equal(fetch.mock.callCount(), before + 1)
    assert.equal(cancel.mock.callCount(), 1)
  }
  assert.deepEqual(state.delays, [])
})

test('a body read failure remains retryable even after successful response headers', async t => {
  const { state, fetch } = fixture(t)
  const response = new Response('incomplete')
  t.mock.method(response, 'arrayBuffer', async () => {
    throw new TypeError('body connection closed')
  })
  fetch.mock.mockImplementationOnce(async () => response)
  assert.equal((await fetchRegistryBytes(url)).toString(), 'archive')
  assert.equal(fetch.mock.callCount(), 2)
  assert.match(state.messages[0], /body connection closed/)
})

test('failed body cancellation preserves the HTTP failure classification', async t => {
  const { fetch } = fixture(t)
  for (const status of [403, 503]) {
    const response = new Response('registry error', { status })
    t.mock.method(response.body, 'cancel', async () => {
      throw new TypeError('connection closed')
    })
    fetch.mock.mockImplementationOnce(async () => response)
    if (status === 403) {
      await assert.rejects(fetchRegistryBytes(url), /403/)
    } else {
      assert.equal((await fetchRegistryBytes(url)).toString(), 'archive')
    }
  }
})

test('Retry-After seconds and HTTP dates are respected', async t => {
  const { state, fetch } = fixture(t)
  for (const retryAfter of ['45', 'Sun, 11 Oct 2026 20:01:30 GMT']) {
    fetch.mock.mockImplementationOnce(async () => new Response(null, {
      status: 429, headers: { 'Retry-After': retryAfter },
    }))
    assert.equal((await fetchRegistryBytes(url)).toString(), 'archive')
  }
  assert.deepEqual(state.delays, [45_000, 45_000])
})

test('invalid, past or short Retry-After values retain the normal retry interval', async t => {
  const { state, fetch } = fixture(t)
  for (const retryAfter of ['invalid', '', 'Sun, 11 Oct 2026 19:00:00 GMT', '0', '3', '9'.repeat(400)]) {
    fetch.mock.mockImplementationOnce(async () => new Response(null, {
      status: 503, headers: { 'Retry-After': retryAfter },
    }))
    assert.equal((await fetchRegistryBytes(url)).toString(), 'archive')
  }
  assert.ok(state.delays.every(delay => delay === 15_000))
})

test('Retry-After cannot extend the shared deadline', async t => {
  const { state, fetch } = fixture(t)
  fetch.mock.mockImplementation(async () => new Response(null, {
    status: 503, headers: { 'Retry-After': '3600' },
  }))
  await assert.rejects(fetchRegistryBytes(url), /Timed out/)
  assert.equal(fetch.mock.callCount(), 1)
  assert.deepEqual(state.delays, [timeoutMs])
})

test('request duration consumes the budget and no extra wait follows an exhausted request', async t => {
  const { state, fetch } = fixture(t)
  fetch.mock.mockImplementation(async () => {
    state.now += 5_000
    throw new DOMException('Request timed out', 'TimeoutError')
  })
  await assert.rejects(fetchRegistryBytes(url, { deadline: 25_000 }), error => {
    assert.equal(error.cause.name, 'TimeoutError')
    return true
  })
  assert.deepEqual(state.timeouts, [25_000, 5_000])
  assert.deepEqual(state.delays, [15_000])
  assert.equal(state.now, 25_000)
})

test('invalid deadlines fail before making requests', async t => {
  const { fetch } = fixture(t)
  for (const deadline of [NaN, Infinity, 'later', null]) {
    await assert.rejects(fetchRegistryBytes(url, { deadline }), /finite/)
  }
  assert.equal(fetch.mock.callCount(), 0)
})
