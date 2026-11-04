import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { chmod, mkdtemp, readFile, readdir, realpath, rm, symlink, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import test from 'node:test'
import { acquireCli, registryBytes } from '../../packaging/templates/content/infrastructure/ngb/acquisition.mjs'
import { archive, cliFiles, entry } from './fixtures/cli.mjs'

const bytes = archive(Object.entries(cliFiles()).map(([path, value]) => entry(path, value)))
const integrity = `sha512-${createHash('sha512').update(bytes).digest('base64')}`
const metadataUrl = 'https://registry.npmjs.org/@ngbplatform%2fcli/3.4.0'
const tarball = 'https://registry.npmjs.org/@ngbplatform/cli/-/cli-3.4.0.tgz'
const metadata = { name: '@ngbplatform/cli', version: '3.4.0', dist: { integrity, tarball } }

async function cache(t) {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'ngb-cli-acquisition-')))
  t.after(() => rm(root, { recursive: true, force: true }))
  return root
}

test('exact registry acquisition verifies bytes and reuses the protected cache without network access', async t => {
  const root = await cache(t)
  const urls = []
  const download = async url => {
    urls.push(url)
    return url === metadataUrl ? Buffer.from(JSON.stringify(metadata)) : bytes
  }
  const first = await acquireCli('3.4.0', { cacheRoot: root, download })
  assert.deepEqual(urls, [metadataUrl, tarball])
  assert.equal(first.cached, false)
  assert.equal(await readFile(first.entry, 'utf8'), 'entry')
  const second = await acquireCli('3.4.0', { cacheRoot: root, download: async () => { throw new Error('offline') } })
  assert.equal(second.cached, true)
  assert.equal(second.entry, first.entry)
  await writeFile(first.entry, 'tampered CLI')
  await assert.rejects(acquireCli('3.4.0', { cacheRoot: root, download }), /modified/)
  assert.equal(await readFile(first.entry, 'utf8'), 'tampered CLI')
})

test('candidate acquisition stays offline and cannot pretend to be a registry installation', async t => {
  const root = await cache(t)
  const candidate = { version: '3.4.0', cli: 'npm/cli.tgz', files: { 'npm/cli.tgz': bytes }, manifestSha256: 'a'.repeat(64) }
  const download = async () => { throw new Error('registry unavailable') }
  const result = await acquireCli('3.4.0', { candidate, cacheRoot: root, download })
  assert.equal(result.source.kind, 'candidate')
  assert.equal((await acquireCli('3.4.0', { candidate, cacheRoot: root, download })).cached, true)
  await assert.rejects(acquireCli('3.4.0', { cacheRoot: root, download }), /registry unavailable/)
  await assert.rejects(acquireCli('3.5.0', { candidate, cacheRoot: root, download }), /versions differ/)
  await assert.rejects(acquireCli('3.4.0', { candidate: { ...candidate, manifestSha256: 'unverified' }, cacheRoot: root }), /manifest identity/)
})

test('concurrent exact-version acquisition converges on one verified immutable cache entry', async t => {
  const root = await cache(t)
  const download = async url => url === metadataUrl ? Buffer.from(JSON.stringify(metadata)) : bytes
  const [first, second] = await Promise.all([
    acquireCli('3.4.0', { cacheRoot: root, download }),
    acquireCli('3.4.0', { cacheRoot: root, download }),
  ])
  assert.equal(first.entry, second.entry)
  assert.equal(first.integrity, second.integrity)
  assert.equal((await readdir(root)).length, 2)
  const offline = await acquireCli('3.4.0', { cacheRoot: root, download: async () => { throw new Error('offline') } })
  assert.equal(offline.entry, first.entry)
})

test('foreign registry locations, wrong versions, corrupt downloads and unsafe caches fail closed', async t => {
  for (const change of [
    value => { value.name = '@other/cli' }, value => { value.version = '3.3.0' },
    value => { value.dist.integrity = 'sha1-untrusted' }, value => { value.dist.tarball = 'https://unapproved.test/cli.tgz' },
  ]) {
    const root = await cache(t)
    const value = structuredClone(metadata)
    change(value)
    await assert.rejects(acquireCli('3.4.0', { cacheRoot: root, download: async () => Buffer.from(JSON.stringify(value)) }))
    assert.deepEqual(await readdir(root), [])
  }
  const root = await cache(t)
  await assert.rejects(acquireCli('3.4.0', { cacheRoot: join(root, 'new-cache'), applicationRoot: root }), /outside the application/)
  assert.deepEqual(await readdir(root), [])
  await assert.rejects(acquireCli('3.4.0', { cacheRoot: root, signal: AbortSignal.abort() }), { name: 'AbortError' })
  await assert.rejects(acquireCli('3.4.0', { cacheRoot: root,
    download: async url => url === metadataUrl ? Buffer.from(JSON.stringify(metadata)) : Buffer.from('corrupt') }), /integrity/)
  await assert.rejects(registryBytes('https://unapproved.test/cli.tgz', 1024), /official npm registry/)
  const alias = join(dirname(root), `${root.split('/').at(-1)}-link`)
  await symlink(root, alias)
  t.after(() => rm(alias, { force: true }))
  await assert.rejects(acquireCli('3.4.0', { cacheRoot: alias }), /real directory/)
  if (process.platform !== 'win32') {
    await chmod(root, 0o755)
    await assert.rejects(acquireCli('3.4.0', { cacheRoot: root }), /private directory/)
  }
})
