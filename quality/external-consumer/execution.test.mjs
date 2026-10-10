import assert from 'node:assert/strict'
import test from 'node:test'
import { mkdtemp, readFile, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { command, retryDownload, transientDownloadFailure } from './execution.mjs'
import { dependencyCache } from './dependency-cache.mjs'

const quiet = { log() {}, wait: async () => {} }

test('download retries recover transient failures with bounded backoff', async () => {
  let attempts = 0
  const waits = []
  const result = await retryDownload(async () => {
    attempts += 1
    if (attempts < 3) throw new Error('HTTP 504 Gateway Timeout')
    return 'downloaded'
  }, { ...quiet, wait: async milliseconds => { waits.push(milliseconds) } })
  assert.equal(result, 'downloaded')
  assert.equal(attempts, 3)
  assert.deepEqual(waits, [1000, 2000])
})

test('permanent download failures are not retried and transient failures exhaust the budget', async () => {
  for (const message of ['HTTP 401 unauthorized', 'manifest unknown', 'checksum mismatch', 'integrity failure', 'HTTP 404 not found', 'compilation failed']) {
    let calls = 0
    await assert.rejects(retryDownload(async () => {
      calls += 1
      throw new Error(message)
    }, quiet))
    assert.equal(calls, 1)
  }
  let calls = 0
  await assert.rejects(retryDownload(async () => {
    calls += 1
    throw new Error('ECONNRESET')
  }, quiet), /ECONNRESET/)
  assert.equal(calls, 3)
  await assert.rejects(retryDownload(async () => {}, { ...quiet, attempts: 0 }))
  assert.equal(transientDownloadFailure('TLS handshake timeout'), true)
  assert.equal(transientDownloadFailure('HTTP 503'), true)
  assert.equal(transientDownloadFailure('TimeoutError: The operation was aborted due to timeout'), true)
  assert.equal(transientDownloadFailure('fetch failed UND_ERR_SOCKET'), true)
})

test('ordinary commands execute once, preserve failures and enforce timeouts', async () => {
  assert.equal(await command(process.execPath, ['-e', 'console.log("ready")'], { capture: true }), 'ready')
  await assert.rejects(command(process.execPath, ['-e', 'console.error("504 is a test assertion"); process.exit(7)'], { capture: true }), /failed \(7\)/)
  await assert.rejects(command(process.execPath, ['-e', 'setInterval(() => {}, 1000)'], { capture: true, timeout: 100 }), /interrupted/)
  await assert.rejects(command('/nonexistent-ngb-test-command', [], { capture: true }), /ENOENT/)
})

test('an explicit cancellation is never retried even after a transient HTTP error', async () => {
  for (const properties of [{ code: 'ABORT_ERR' }, { name: 'AbortError' }]) {
    let calls = 0
    await assert.rejects(retryDownload(async () => {
      calls += 1
      throw Object.assign(new Error('HTTP 504 before cancellation'), properties)
    }, quiet), /cancellation/)
    assert.equal(calls, 1)
  }
})

test('only download caches are shared; clean starter and registry smoke stay isolated', t => {
  const previous = process.env.NGB_CERTIFICATION_CACHE
  t.after(() => {
    if (previous === undefined) delete process.env.NGB_CERTIFICATION_CACHE
    else process.env.NGB_CERTIFICATION_CACHE = previous
  })
  delete process.env.NGB_CERTIFICATION_CACHE
  assert.equal(dependencyCache('/consumer', '3.2.0').npm, '/consumer/npm-3.2.0')
  process.env.NGB_CERTIFICATION_CACHE = '/candidate-cache'
  assert.deepEqual(dependencyCache('/consumer', '3.2.0'), {
    npm: '/candidate-cache/npm-3.2.0', nugetHttp: '/candidate-cache/nuget-http-3.2.0',
  })
  assert.equal(dependencyCache('/consumer', '3.2.0', true).npm, '/consumer/npm-3.2.0')
  assert.notEqual(dependencyCache('/consumer', '3.1.0').npm, dependencyCache('/consumer', '3.2.0').npm)
})

test('command cancellation reaches descendant processes, not just the launcher', async t => {
  const root = await mkdtemp(join(tmpdir(), 'ngb-cancellation-test-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  const marker = join(root, 'terminated')
  const descendant = `process.on('SIGTERM', () => { require('node:fs').writeFileSync(${JSON.stringify(marker)}, 'stopped'); process.exit(0) }); setInterval(() => {}, 1000)`
  const launcher = `require('node:child_process').spawn(process.execPath, ['-e', ${JSON.stringify(descendant)}], {stdio:'inherit'}); setInterval(() => {}, 1000)`
  await assert.rejects(command(process.execPath, ['-e', launcher], { capture: true, timeout: 1000 }), /interrupted/)
  assert.equal(await readFile(marker, 'utf8'), 'stopped')
})
