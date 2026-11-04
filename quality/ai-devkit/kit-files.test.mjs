import assert from 'node:assert/strict'
import { link, mkdir, mkdtemp, readFile, readdir, realpath, rm, symlink, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import { applyAssets, assertSnapshot, assetPaths, readAsset, safePath } from '../../packaging/templates/content/infrastructure/ngb/kit-files.mjs'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'

async function workspace(t) {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'ngb-kit-files-')))
  t.after(() => rm(root, { recursive: true, force: true }))
  return root
}

test('asset reads are bounded, UTF-8 and contained without following symlinks or hard links', async t => {
  const root = await workspace(t)
  assert.equal(await readAsset(root, 'missing.txt'), null)
  assert.equal(await safePath(root, 'new/deep/file.txt'), join(root, 'new/deep/file.txt'))
  await writeFile(join(root, 'text.md'), 'valid\r\n')
  assert.equal(await readAsset(root, 'text.md'), 'valid\r\n')
  await assert.rejects(readAsset(root, 'text.md', 2), /size limit/)
  await writeFile(join(root, 'invalid.md'), Buffer.from([0xff, 0xfe]))
  await assert.rejects(readAsset(root, 'invalid.md'), /UTF-8/)
  await assert.rejects(readAsset(root, '../outside'), /Unsafe/)
  await assert.rejects(readAsset(root, 'text.md/child'), /not a directory/)
  await mkdir(join(root, 'directory'))
  await assert.rejects(readAsset(root, 'directory'), /regular file/)
  await symlink(join(root, 'text.md'), join(root, 'symlink.md'))
  await assert.rejects(readAsset(root, 'symlink.md'), /Symbolic/)
  await link(join(root, 'text.md'), join(root, 'hardlink.md'))
  await assert.rejects(readAsset(root, 'hardlink.md'), /one link/)
  await symlink(root, join(root, 'alias'))
  await assert.rejects(safePath(join(root, 'alias'), 'text.md'), /root must be a real path/)
})

test('asset discovery detects nesting, case aliases and unsupported entries', async t => {
  const root = await workspace(t)
  assert.deepEqual(await assetPaths(root, 'missing'), [])
  await mkdir(join(root, 'assets/sub'), { recursive: true })
  await writeFile(join(root, 'assets/b.md'), 'b')
  await writeFile(join(root, 'assets/sub/a.md'), 'a')
  assert.deepEqual(await assetPaths(root, 'assets'), ['assets/b.md', 'assets/sub/a.md'])
  await symlink(join(root, 'assets/b.md'), join(root, 'assets/link'))
  await assert.rejects(assetPaths(root, 'assets'), /Symbolic/)
  await mkdir(join(root, 'deep', ...Array(14).fill('a')), { recursive: true })
  await assert.rejects(assetPaths(root, 'deep'), /nesting/)
})

test('application mutation needs a full snapshot and refuses concurrent writes', async t => {
  const root = await workspace(t)
  await writeFile(join(root, 'owned.md'), 'original')
  await assertSnapshot(root, { 'owned.md': 'original', 'new.md': null })
  await assert.rejects(assertSnapshot(root, { 'owned.md': 'old' }), /Concurrent/)
  await assert.rejects(applyAssets(root, {}, { 'owned.md': 'replacement' }, 'plan'), /snapshot/)
  const result = await applyAssets(root, { 'owned.md': 'original', 'new.md': null },
    { 'owned.md': 'updated', 'new.md': 'new' }, 'plan')
  assert.equal(result.state, 'committed')
  assert.equal(await readAsset(root, 'owned.md'), 'updated')
  assert.equal(await readAsset(root, 'new.md'), 'new')
  const receipt = JSON.parse(await readFile(join(root, '.ngb/transactions', result.id, 'receipt.json'), 'utf8'))
  assert.equal(receipt.files.find(entry => entry.path === 'owned.md').before, digest('original'))
  assert.equal(await readAsset(root, `.ngb/transactions/${result.id}/objects/${digest('original')}`), 'original')
  assert.equal(await readAsset(root, '.ngb/kit-transaction.json'), null)
  assert.ok(!(await readdir(join(root, '.ngb'))).includes('operation.lock'))
})

test('injected write failures roll back changed and newly created files', async t => {
  const root = await workspace(t)
  await writeFile(join(root, 'b.md'), 'original')
  await writeFile(join(root, 'c.md'), 'keep')
  await assert.rejects(applyAssets(root,
    { 'a.md': null, 'b.md': 'original', 'c.md': 'keep' },
    { 'a.md': 'new', 'b.md': null, 'c.md': 'new' }, 'plan', async path => {
      if (path === 'c.md') throw new Error('injected failure')
    }), /injected failure/)
  assert.equal(await readAsset(root, 'a.md'), null)
  assert.equal(await readAsset(root, 'b.md'), 'original')
  assert.equal(await readAsset(root, 'c.md'), 'keep')
  const [history] = await readdir(join(root, '.ngb/transactions'))
  const receipt = JSON.parse(await readFile(join(root, '.ngb/transactions', history, 'receipt.json'), 'utf8'))
  assert.equal(receipt.state, 'rolled-back')
})

test('a pre-existing operation lock or interrupted journal prevents another writer', async t => {
  const root = await workspace(t)
  await mkdir(join(root, '.ngb/operation.lock'), { recursive: true })
  await assert.rejects(applyAssets(root, { 'a.md': null }, { 'a.md': 'new' }, 'plan'), /EEXIST/)
  await rm(join(root, '.ngb/operation.lock'), { recursive: true })
  await writeFile(join(root, '.ngb/kit-transaction.json'), '{"interrupted":true}\n')
  await assert.rejects(applyAssets(root, { 'a.md': null }, { 'a.md': 'new' }, 'plan'), /interrupted/)
  assert.equal(await readAsset(root, 'a.md'), null)
  assert.equal(await readAsset(root, '.ngb/kit-transaction.json'), '{"interrupted":true}\n')
})

test('a concurrent change during apply is preserved while earlier writes roll back', async t => {
  const root = await workspace(t)
  await assert.rejects(applyAssets(root, { 'a.md': null, 'b.md': null },
    { 'a.md': 'generated a', 'b.md': 'generated b' }, 'plan', async path => {
      if (path === 'b.md') await writeFile(join(root, path), 'concurrent user work')
    }), /Concurrent/)
  assert.equal(await readAsset(root, 'a.md'), null)
  assert.equal(await readAsset(root, 'b.md'), 'concurrent user work')
})
