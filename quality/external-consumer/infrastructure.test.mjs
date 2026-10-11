import assert from 'node:assert/strict'
import { mkdtemp, readFile, rm, stat } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import { saveInfrastructureDiagnostics, writeInfrastructureConfiguration } from './infrastructure.mjs'

const secrets = {
  postgres: 'private-postgres', administrator: 'private-administrator', client: 'private-client',
  harness: 'private-harness', minioRoot: 'private-minio-root', minioApp: 'private-minio-app',
}

async function temporary(t) {
  const directory = await mkdtemp(join(tmpdir(), 'ngb-infrastructure-test-'))
  t.after(() => rm(directory, { recursive: true, force: true }))
  return directory
}

test('starter and upgrade imports are readable across container UIDs without disclosing secrets', async t => {
  for (const source of [
    '../../packaging/templates/content/infrastructure/realm.json',
    '../upgrade-certification/fixtures/3.1.0/infrastructure/realm.json',
  ]) {
    const directory = await temporary(t)
    const original = await readFile(new URL(source, import.meta.url), 'utf8')
    const realm = JSON.parse(original)
    const previousMask = process.umask(0o077)
    try {
      await writeInfrastructureConfiguration(directory, realm, 'administrator@certification.test', secrets)
    } finally {
      process.umask(previousMask)
    }
    const content = await readFile(join(directory, 'realm.json'), 'utf8')
    const configured = JSON.parse(content)
    assert.deepEqual(realm, JSON.parse(original))
    assert.equal(configured.realm, realm.realm)
    assert.equal(configured.users[0].username, 'administrator@certification.test')
    assert.equal(configured.users[0].email, 'administrator@certification.test')
    assert.equal(configured.users[0].credentials[0].value, '${BOOTSTRAP_PASSWORD}')
    assert.equal(configured.clients[2].secret, '${KEYCLOAK_ADMIN_CLIENT_SECRET}')
    assert.equal(configured.clients.at(-1).clientId, 'certification-harness')
    assert.equal(configured.clients.at(-1).secret, '${CERTIFICATION_HARNESS_SECRET}')
    assert.equal(configured.clients.at(-1).directAccessGrantsEnabled, true)
    for (const secret of Object.values(secrets)) assert.ok(!content.includes(secret))
    assert.equal((await stat(join(directory, 'realm.json'))).mode & 0o777, 0o644)
    assert.equal((await stat(join(directory, 'init.sql'))).mode & 0o777, 0o644)
    assert.equal((await stat(join(directory, '.env'))).mode & 0o777, 0o600)
    assert.equal(await readFile(join(directory, 'init.sql'), 'utf8'), 'CREATE DATABASE keycloak;\n')
    const environment = await readFile(join(directory, '.env'), 'utf8')
    assert.equal(environment, [
      `POSTGRES_PASSWORD=${secrets.postgres}`, `BOOTSTRAP_PASSWORD=${secrets.administrator}`,
      `KEYCLOAK_ADMIN_CLIENT_SECRET=${secrets.client}`, `CERTIFICATION_HARNESS_SECRET=${secrets.harness}`,
      `MINIO_ROOT_PASSWORD=${secrets.minioRoot}`, `MINIO_APP_SECRET_KEY=${secrets.minioApp}`, '',
    ].join('\n'))
    await assert.rejects(writeInfrastructureConfiguration(directory, realm, 'replacement@example.test', secrets), { code: 'EEXIST' })
    assert.equal(await readFile(join(directory, '.env'), 'utf8'), environment)
  }
})

test('failure diagnostics collect bounded container logs and status with every generated secret redacted', async t => {
  const directory = join(await temporary(t), 'diagnostics')
  const compose = ['compose', '--project-name', 'test', '--file', '/private/compose.yaml']
  const calls = []
  const execute = (command, args, options) => {
    calls.push(args.slice(compose.length))
    assert.equal(command, 'docker')
    assert.deepEqual(args.slice(0, compose.length), compose)
    assert.equal(options.stdio, 'pipe')
    assert.equal(options.timeout, 15_000)
    assert.equal(options.maxBuffer, 2 * 1024 * 1024)
    return `Permission denied\n${Object.values(secrets).join('\n')}\n`
  }
  const path = await saveInfrastructureDiagnostics(directory, compose, secrets, execute)
  assert.deepEqual(calls, [['ps', '--all'], ['logs', '--no-color', '--tail', '200']])
  const content = await readFile(path, 'utf8')
  assert.match(content, /Permission denied/)
  assert.match(content, /\[REDACTED\]/)
  for (const secret of Object.values(secrets)) assert.ok(!content.includes(secret))
  assert.equal((await stat(path)).mode & 0o777, 0o600)
})

test('failed diagnostic commands preserve redacted output and continue collecting remaining diagnostics', async t => {
  let calls = 0
  const execute = () => {
    calls += 1
    if (calls === 1) {
      throw Object.assign(new Error(`failure ${secrets.postgres}`), {
        stdout: secrets.administrator, stderr: secrets.client,
      })
    }
    throw new Error('Docker is unavailable')
  }
  const path = await saveInfrastructureDiagnostics(await temporary(t), ['compose'], secrets, execute)
  const content = await readFile(path, 'utf8')
  assert.equal(calls, 2)
  assert.match(content, /Diagnostic collection failed/)
  assert.match(content, /Docker is unavailable/)
  for (const secret of Object.values(secrets)) assert.ok(!content.includes(secret))
})
