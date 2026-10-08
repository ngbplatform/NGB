import assert from 'node:assert/strict'
import { mkdtemp, readFile, rm, stat } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import { initializeConfiguration } from '../../packaging/templates/content/infrastructure/configuration.mjs'

test('configuration creates private unique secrets without overwriting an existing deployment', async t => {
  const root = await mkdtemp(join(tmpdir(), 'ngb-configuration-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  const path = join(root, '.env')
  await assert.rejects(initializeConfiguration(path), /Usage/)
  await assert.rejects(initializeConfiguration(path, 'bad\nINJECTED=true'), /Usage/)
  await initializeConfiguration(path, 'administrator@example.test')
  assert.equal((await stat(path)).mode & 0o777, 0o600)
  const original = await readFile(path, 'utf8')
  const values = original.trim().split('\n')
  assert.equal(values[0], 'BOOTSTRAP_EMAIL=administrator@example.test')
  assert.equal(values.length, 4)
  const secrets = values.slice(1).map(line => line.split('=')[1])
  assert.equal(new Set(secrets).size, 3)
  for (const secret of secrets) assert.match(secret, /^[A-Za-z0-9_-]{43}$/)
  await assert.rejects(initializeConfiguration(path, 'replacement@example.test'), /EEXIST/)
  assert.equal(await readFile(path, 'utf8'), original)
})
