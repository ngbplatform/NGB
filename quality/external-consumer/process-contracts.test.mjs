import assert from 'node:assert/strict'
import { execFileSync, spawnSync } from 'node:child_process'
import { chmod, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')

test('invalid profile and release commands fail before infrastructure or publication', () => {
  for (const script of ['certify.mjs', 'release.mjs']) {
    const result = spawnSync(process.execPath, [`quality/external-consumer/${script}`, 'invalid-command'], {
      cwd: repository, encoding: 'utf8',
    })
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /Usage:/)
    assert.ok(!result.stdout.includes('passed'))
  }
})

test('direct full quality refuses a missing k6 before starting expensive stages', async t => {
  const root = await mkdtemp(join(tmpdir(), 'ngb-quality-prerequisites-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  const result = spawnSync(process.execPath, ['quality/external-consumer/quality-suite.mjs'], {
    cwd: repository, env: { ...process.env, PATH: root }, encoding: 'utf8',
  })
  assert.notEqual(result.status, 0)
  assert.match(result.stderr, /spawn k6 ENOENT/)
  assert.ok(!result.stdout.includes('[start]'))
})

test('the generated migration entrypoint propagates failure and never seeds after failure', async t => {
  const root = await mkdtemp(join(tmpdir(), 'ngb-migration-entrypoint-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  const executable = join(root, 'dotnet')
  const calls = join(root, 'calls')
  await writeFile(executable, '#!/bin/sh\nprintf "%s\\n" "$*" >> "$NGB_TEST_CALLS"\nexit "$NGB_TEST_EXIT"\n')
  await chmod(executable, 0o700)
  const env = { ...process.env, PATH: `${root}:${process.env.PATH}`, NGB_TEST_CALLS: calls, NGB_TEST_EXIT: '17' }
  const script = join(repository, 'packaging/templates/content/infrastructure/migrate.sh')
  const failed = spawnSync('sh', [script], { env, encoding: 'utf8' })
  assert.equal(failed.status, 17)
  assert.equal(await readFile(calls, 'utf8'), 'NgbApplication.Migrator.dll\n')
  await writeFile(calls, '')
  execFileSync('sh', [script], { env: { ...env, NGB_TEST_EXIT: '0' } })
  assert.equal(await readFile(calls, 'utf8'), 'NgbApplication.Migrator.dll\nNgbApplication.Migrator.dll seed-administrator\n')
})

test('missing manifests and incomplete release evidence cannot pass publication validation', async t => {
  const root = await mkdtemp(join(tmpdir(), 'ngb-evidence-rejection-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  for (const action of ['verify', 'verify-candidate', 'verify-publication', 'verify-promotion']) {
    const result = spawnSync(process.execPath, ['quality/external-consumer/release.mjs', action, root], {
      cwd: repository, encoding: 'utf8',
    })
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /release-manifest/)
  }
  await writeFile(join(root, 'release-manifest.json'), JSON.stringify({ schemaVersion: 1, version: '3.2.0', artifacts: [] }))
  const forged = spawnSync(process.execPath, ['quality/external-consumer/release.mjs', 'verify-candidate', root], {
    cwd: repository, encoding: 'utf8',
  })
  assert.notEqual(forged.status, 0)
  assert.match(forged.stderr, /inventory/)
})
