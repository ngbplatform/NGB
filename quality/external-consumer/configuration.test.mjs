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
  const settings = Object.fromEntries(values.map(line => line.split('=')))
  assert.equal(settings.FEATURE_ATTACHMENTS, 'true')
  assert.equal(settings.FEATURE_NOTES, 'true')
  const names = ['BOOTSTRAP_PASSWORD', 'POSTGRES_PASSWORD', 'KEYCLOAK_ADMIN_CLIENT_SECRET',
    'KEYCLOAK_MASTER_PASSWORD', 'PGADMIN_PASSWORD', 'SEQ_PASSWORD', 'MINIO_ROOT_PASSWORD', 'MINIO_APP_SECRET_KEY']
  const secrets = names.map(name => settings[name])
  assert.equal(new Set(secrets).size, names.length)
  for (const secret of secrets) assert.match(secret, /^[A-Za-z0-9_-]{43}$/)
  assert.match(settings.MINIO_ROOT_USER, /^[A-Za-z0-9_-]{20}$/)
  assert.match(settings.MINIO_APP_ACCESS_KEY, /^[A-Za-z0-9_-]{20}$/)
  assert.notEqual(settings.MINIO_ROOT_USER, settings.MINIO_APP_ACCESS_KEY)
  await assert.rejects(initializeConfiguration(path, 'replacement@example.test'), /EEXIST/)
  assert.equal(await readFile(path, 'utf8'), original)
})
