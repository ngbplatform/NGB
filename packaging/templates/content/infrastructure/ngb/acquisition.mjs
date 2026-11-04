import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { lstat, mkdir, mkdtemp, realpath, rename, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { basename, dirname, isAbsolute, join, relative, resolve } from 'node:path'
import { exactVersion } from './contracts.mjs'
import { verifyCliArchive } from './archive.mjs'
import { digest } from './kit-contracts.mjs'
import { readAsset, readBytes, safePath } from './kit-files.mjs'

const registry = 'https://registry.npmjs.org'

async function cacheLocation(path, applicationRoot) {
  let parent = resolve(path)
  const missing = []
  while (true) {
    try {
      parent = await realpath(parent)
      break
    } catch (error) {
      if (error.code !== 'ENOENT') throw error
      missing.unshift(basename(parent))
      parent = dirname(parent)
    }
  }
  const location = join(parent, ...missing)
  if (applicationRoot) {
    const application = await realpath(applicationRoot)
    const inside = (root, child) => {
      const path = relative(root, child)
      return path === '' || path !== '..' && !path.startsWith(`..${process.platform === 'win32' ? '\\' : '/'}`) && !isAbsolute(path)
    }
    assert.ok(!inside(application, location) && !inside(location, application),
      'CLI acquisition cache must be outside the application, with no overlapping directories.')
  }
}

export async function registryBytes(url, limit, signal) {
  const target = new URL(url)
  assert.equal(target.origin, registry, 'CLI acquisition only uses the official npm registry.')
  assert.ok(!target.username && !target.password && !target.hash && !target.search, 'Invalid registry URL.')
  const response = await fetch(target, { redirect: 'error',
    signal: signal ? AbortSignal.any([signal, AbortSignal.timeout(120000)]) : AbortSignal.timeout(120000) })
  if (!response.ok) {
    await response.body?.cancel()
    throw new Error(`The exact CLI artifact is unavailable from the official registry (HTTP ${response.status}).`)
  }
  const chunks = []
  let length = 0
  for await (const chunk of response.body) {
    length += chunk.length
    assert.ok(length <= limit, 'Registry response exceeds its size limit.')
    chunks.push(chunk)
  }
  return Buffer.concat(chunks)
}

function integrity(bytes) {
  return `sha512-${createHash('sha512').update(bytes).digest('base64')}`
}

async function verifiedCache(root, expected, version) {
  const cached = await readBytes(root, 'cli.tgz', 16 * 1024 * 1024)
  assert.ok(cached && integrity(cached) === expected, 'Cached CLI archive is missing or modified.')
  const { files } = verifyCliArchive(cached, version)
  for (const [path, bytes] of files) {
    const actual = await readBytes(root, path, 20 * 1024 * 1024)
    assert.ok(actual && actual.equals(bytes), `Cached CLI payload was modified: ${path}`)
  }
  return join(root, 'package/infrastructure/ngb.mjs')
}

export async function acquireCli(version, { candidate = null, cacheRoot = join(tmpdir(), 'ngb-cli-cache'), applicationRoot, signal,
  download = registryBytes } = {}) {
  exactVersion(version)
  signal?.throwIfAborted()
  await cacheLocation(cacheRoot, applicationRoot)
  await mkdir(cacheRoot, { recursive: true, mode: 0o700 })
  const details = await lstat(cacheRoot)
  assert.ok(details.isDirectory() && !details.isSymbolicLink(), 'CLI cache must be a real directory.')
  if (process.platform !== 'win32') {
    assert.ok(details.uid === process.getuid() && (details.mode & 0o077) === 0,
      'CLI cache must belong to the current user with private directory permissions.')
  }
  const cache = await realpath(cacheRoot)
  let bytes
  let expected
  let source
  if (candidate) {
    assert.equal(candidate.version, version, 'Target CLI and sealed candidate versions differ.')
    bytes = candidate.files[candidate.cli]
    assert.ok(bytes, 'The candidate does not contain its target CLI.')
    assert.match(candidate.manifestSha256, /^[a-f0-9]{64}$/, 'Candidate CLI acquisition requires its sealed manifest identity.')
    verifyCliArchive(bytes, version)
    expected = integrity(bytes)
    source = { kind: 'candidate', manifestSha256: candidate.manifestSha256 }
  } else {
    const index = await readAsset(cache, `${version}.registry/receipt.json`)
    if (index) {
      const value = JSON.parse(index)
      assert.equal(value.schemaVersion, 1, 'Invalid CLI cache index schema.')
      assert.equal(value.version, version, 'CLI cache index version differs from its path.')
      assert.match(value.integrity, /^sha512-[A-Za-z0-9+/]{86}==$/, 'Invalid cached registry integrity.')
      assert.deepEqual(value.source, { kind: 'registry', url: `${registry}/@ngbplatform/cli/-/cli-${version}.tgz`, integrity: value.integrity },
        'Only a previously verified registry acquisition can be reused without a candidate manifest.')
      const cachedRoot = await safePath(cache, `${version}-${digest(value.integrity)}`)
      return { entry: await verifiedCache(cachedRoot, value.integrity, version), version,
        integrity: value.integrity, source: value.source, cached: true }
    }
    const metadata = JSON.parse(await download(`${registry}/@ngbplatform%2fcli/${version}`, 1024 * 1024, signal))
    assert.equal(metadata.name, '@ngbplatform/cli')
    assert.equal(metadata.version, version, 'The registry returned a different CLI version.')
    expected = metadata.dist?.integrity
    assert.match(expected, /^sha512-[A-Za-z0-9+/]{86}==$/, 'The registry did not provide SHA-512 CLI integrity.')
    assert.equal(metadata.dist.tarball, `${registry}/@ngbplatform/cli/-/cli-${version}.tgz`, 'Unexpected CLI artifact location.')
    source = { kind: 'registry', url: metadata.dist.tarball, integrity: expected }
  }
  const key = `${version}-${digest(expected)}`
  const destination = await safePath(cache, key)
  try {
    const receipt = await readAsset(cache, `${key}/receipt.json`)
    if (receipt) {
      const value = JSON.parse(receipt)
      assert.equal(value.schemaVersion, 1)
      assert.equal(value.version, version)
      assert.equal(value.integrity, expected)
      const entry = await verifiedCache(destination, expected, version)
      if (!candidate) await recordRegistryIndex(cache, version, expected, source)
      return { entry, version, integrity: expected, source, cached: true }
    }
    bytes ??= await download(source.url, 16 * 1024 * 1024, signal)
    assert.equal(integrity(bytes), expected, 'Downloaded CLI bytes differ from registry integrity.')
    const { files } = verifyCliArchive(bytes, version)
    const stage = await mkdtemp(join(cache, 'staging-'))
    try {
      for (const [path, content] of files) {
        signal?.throwIfAborted()
        const location = await safePath(stage, path)
        await mkdir(dirname(location), { recursive: true, mode: 0o700 })
        await writeFile(location, content, { flag: 'wx', mode: 0o600 })
      }
      await writeFile(join(stage, 'cli.tgz'), bytes, { flag: 'wx', mode: 0o600 })
      await writeFile(join(stage, 'receipt.json'), `${JSON.stringify({ schemaVersion: 1, version, integrity: expected, source }, null, 2)}\n`,
        { flag: 'wx', mode: 0o600 })
      try {
        await rename(stage, destination)
      } catch (error) {
        if (!['EEXIST', 'ENOTEMPTY'].includes(error.code)) throw error
        // A concurrent acquisition may already have installed the same immutable
        // identity. Verify all bytes before using it; never overwrite that cache.
      }
    } finally {
      await rm(stage, { recursive: true, force: true })
    }
    const entry = await verifiedCache(destination, expected, version)
    if (!candidate) await recordRegistryIndex(cache, version, expected, source)
    return { entry, version, integrity: expected, source, cached: false }
  } catch (error) {
    throw new Error(`Cannot acquire verified NGB CLI ${version}: ${error.message}`, { cause: error })
  }
}

async function recordRegistryIndex(cache, version, expected, source) {
  const content = `${JSON.stringify({ schemaVersion: 1, version, integrity: expected, source }, null, 2)}\n`
  const path = `${version}.registry/receipt.json`
  const existing = await readAsset(cache, path)
  assert.ok(existing === null || existing === content, 'The cached registry identity changed during acquisition.')
  if (existing !== null) return
  const temporary = await mkdtemp(join(cache, 'registry-index-'))
  try {
    await writeFile(join(temporary, 'receipt.json'), content, { flag: 'wx', mode: 0o600 })
    try {
      await rename(temporary, await safePath(cache, `${version}.registry`))
    } catch (error) {
      if (!['EEXIST', 'ENOTEMPTY'].includes(error.code)) throw error
    }
    assert.equal(await readAsset(cache, path), content, 'The cached registry identity changed during acquisition.')
  } finally {
    await rm(temporary, { recursive: true, force: true })
  }
}
