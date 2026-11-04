import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { chmod, mkdir, mkdtemp, readFile, readdir, realpath, rm, stat, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { applyTransaction, recoverTransaction } from '../../packaging/templates/content/infrastructure/ngb/transaction.mjs'

const transactionUrl = new URL('../../packaging/templates/content/infrastructure/ngb/transaction.mjs', import.meta.url).href

async function workspace(t) {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'ngb-transaction-')))
  t.after(() => rm(root, { recursive: true, force: true }))
  await writeFile(join(root, 'original.bin'), Buffer.from([0, 255, 42]))
  await chmod(join(root, 'original.bin'), 0o640)
  return root
}

async function interrupted(t) {
  const root = await workspace(t)
  const source = `
import { applyTransaction } from ${JSON.stringify(transactionUrl)}
await applyTransaction(process.argv[1], {
  'original.bin': Buffer.from([0, 255, 42]), 'new.txt': null, 'last.txt': null,
}, {
  'original.bin': Buffer.from([1, 2, 3]), 'new.txt': Buffer.from('new'), 'last.txt': Buffer.from('last'),
}, 'interrupted-test', { beforeWrite: async path => {
  if (path === 'last.txt') process.exit(91)
} })
`
  const child = spawnSync(process.execPath, ['--input-type=module', '--eval', source, root], { encoding: 'utf8' })
  assert.equal(child.status, 91, child.stderr)
  return root
}

test('process termination leaves a read-only recovery preview and an idempotent rollback with original bytes and modes', async t => {
  const root = await interrupted(t)
  const journal = await readFile(join(root, '.ngb/transaction.json'), 'utf8')
  const preview = await recoverTransaction(root)
  assert.equal(preview.state, 'prepared')
  assert.equal(preview.action, 'rollback')
  assert.equal(await readFile(join(root, '.ngb/transaction.json'), 'utf8'), journal)
  assert.equal(await readFile(join(root, 'new.txt'), 'utf8'), 'new')
  const recovered = await recoverTransaction(root, true)
  assert.equal(recovered.state, 'rolled-back')
  assert.deepEqual(await readFile(join(root, 'original.bin')), Buffer.from([0, 255, 42]))
  if (process.platform !== 'win32') assert.equal((await stat(join(root, 'original.bin'))).mode & 0o777, 0o640)
  await assert.rejects(readFile(join(root, 'new.txt')), { code: 'ENOENT' })
  await assert.rejects(readFile(join(root, '.ngb/transaction.json')), { code: 'ENOENT' })
  assert.equal((await recoverTransaction(root, true)).state, 'not-needed')
  const receipt = JSON.parse(await readFile(join(root, '.ngb/transactions', recovered.id, 'receipt.json'), 'utf8'))
  assert.equal(receipt.state, 'rolled-back')
  assert.equal(receipt.files[0].before, digest(Buffer.from([0, 255, 42])))
})

test('recovery refuses later user edits and preserves both the edit and interrupted transaction', async t => {
  const root = await interrupted(t)
  await writeFile(join(root, 'new.txt'), 'user edit after crash')
  const before = await readFile(join(root, '.ngb/transaction.json'))
  await assert.rejects(recoverTransaction(root, true), /subsequent edit/)
  assert.equal(await readFile(join(root, 'new.txt'), 'utf8'), 'user edit after crash')
  assert.deepEqual(await readFile(join(root, 'original.bin')), Buffer.from([1, 2, 3]))
  assert.deepEqual(await readFile(join(root, '.ngb/transaction.json')), before)
})

test('recovery validates every backup before changing application files', async t => {
  const root = await interrupted(t)
  const journal = JSON.parse(await readFile(join(root, '.ngb/transaction.json'), 'utf8'))
  const original = journal.files.find(file => file.path === 'original.bin')
  await writeFile(join(root, '.ngb/transactions', journal.id, 'objects', original.before), 'tampered backup')
  await assert.rejects(recoverTransaction(root, true), /backup is missing or changed/)
  assert.deepEqual(await readFile(join(root, 'original.bin')), Buffer.from([1, 2, 3]))
  assert.equal(await readFile(join(root, 'new.txt'), 'utf8'), 'new')
})

test('an active owner, unknown lock or journal traversal cannot be recovered automatically', async t => {
  const root = await interrupted(t)
  const ownerPath = join(root, '.ngb/operation.lock/owner.json')
  const owner = JSON.parse(await readFile(ownerPath, 'utf8'))
  await writeFile(ownerPath, JSON.stringify({ ...owner, pid: process.pid }))
  await assert.rejects(recoverTransaction(root, true), /still running/)
  await rm(ownerPath)
  await assert.rejects(recoverTransaction(root, true), /no owner/)
  await writeFile(ownerPath, JSON.stringify(owner))
  const journalPath = join(root, '.ngb/transaction.json')
  const journal = JSON.parse(await readFile(journalPath, 'utf8'))
  for (const path of ['../outside', '.git/config', '.ngb/operation.lock/owner.json']) {
    const modified = structuredClone(journal)
    modified.files[0].path = path
    await writeFile(journalPath, JSON.stringify(modified))
    await assert.rejects(recoverTransaction(root, true))
  }
  await rm(journalPath)
  await assert.rejects(recoverTransaction(root, true), /no journal/)
})

test('transactions reject incomplete snapshots and unsafe targets before creating a lock', async t => {
  const root = await workspace(t)
  for (const path of ['../outside', '.env', '.git/config', '.ngb/transaction.json']) {
    await assert.rejects(applyTransaction(root, { [path]: null }, { [path]: Buffer.from('x') }, 'test'))
  }
  await assert.rejects(applyTransaction(root, {}, { 'new': Buffer.from('x') }, 'test'), /snapshot/)
  await assert.rejects(applyTransaction(root, { 'new': null }, { 'new': 'text' }, 'test'), /binary/)
  await assert.rejects(applyTransaction(root, {}, {}, 'test'), /Empty/)
  assert.deepEqual(await readdir(root), ['original.bin'])
})

test('a handled failure restores deleted files, removes additions and retains a verified receipt', async t => {
  const root = await workspace(t)
  await assert.rejects(applyTransaction(root, {
    'original.bin': Buffer.from([0, 255, 42]), 'new': null, 'last': null,
  }, { 'original.bin': null, 'new': Buffer.from('added'), 'last': Buffer.from('last') }, 'failure', {
    beforeWrite: async path => {
      if (path === 'last') throw new Error('injected failure')
    },
  }), /injected failure/)
  assert.deepEqual(await readFile(join(root, 'original.bin')), Buffer.from([0, 255, 42]))
  await assert.rejects(readFile(join(root, 'new')), { code: 'ENOENT' })
  assert.equal((await recoverTransaction(root)).state, 'not-needed')
  await mkdir(join(root, '.ngb/operation.lock'))
  await assert.rejects(applyTransaction(root, { 'new': null }, { 'new': Buffer.from('x') }, 'test'), /EEXIST/)
})

test('a write that succeeds before reporting an error is still included in rollback', async t => {
  const root = await workspace(t)
  await assert.rejects(applyTransaction(root, { 'original.bin': Buffer.from([0, 255, 42]), 'new': null },
    { 'original.bin': Buffer.from([1, 2, 3]), 'new': Buffer.from('added') }, 'late-write-failure', {
      replaceFile: async (path, bytes) => {
        await writeFile(path, bytes)
        if (path.endsWith('/new')) throw new Error('Failure after destination replacement')
      },
    }), /Failure after/)
  assert.deepEqual(await readFile(join(root, 'original.bin')), Buffer.from([0, 255, 42]))
  await assert.rejects(readFile(join(root, 'new')), { code: 'ENOENT' })
  assert.equal((await recoverTransaction(root)).state, 'not-needed')
})

test('write failures preserve subsequent edits and retain an unresolved recovery journal', async t => {
  const root = await workspace(t)
  await assert.rejects(applyTransaction(root, { 'original.bin': Buffer.from([0, 255, 42]) },
    { 'original.bin': Buffer.from([1, 2, 3]) }, 'concurrent-write-failure', {
      replaceFile: async path => {
        await writeFile(path, 'concurrent edit')
        throw new Error('Failure after concurrent modification')
      },
    }), /subsequent edit/)
  assert.equal(await readFile(join(root, 'original.bin'), 'utf8'), 'concurrent edit')
  assert.equal(JSON.parse(await readFile(join(root, '.ngb/transaction.json'))).state, 'prepared')
  await assert.rejects(recoverTransaction(root, true), /subsequent edit/)
})
