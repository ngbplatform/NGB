import assert from 'node:assert/strict'
import { execFileSync, spawnSync } from 'node:child_process'
import { chmod, cp, mkdir, mkdtemp, readFile, readdir, rm, symlink, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'
import { strToU8, zipSync } from 'fflate'
import { digest } from './release-contracts.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')

async function releaseFixture(t) {
  const root = await mkdtemp(join(tmpdir(), 'ngb-seal-process-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  const save = async (path, bytes) => {
    const destination = join(root, path)
    await mkdir(dirname(destination), { recursive: true })
    await writeFile(destination, bytes)
  }
  await mkdir(join(root, 'quality/external-consumer'), { recursive: true })
  for (const name of [
    'release.mjs', 'release-contracts.mjs', 'contracts.mjs', 'checkpoints.mjs',
    'execution.mjs', 'environment.mjs', 'toolchain.json', 'matrix.json',
  ]) {
    const path = `quality/external-consumer/${name}`
    await cp(join(repository, path), join(root, path))
  }
  await symlink(join(repository, 'quality/node_modules'), join(root, 'quality/node_modules'))
  await save('.gitignore', 'artifacts/\nquality/node_modules\n')
  await save('quality/upgrade-certification/source-3.1.0.json', '{}')
  await save('packaging/nuget/baselines.json', JSON.stringify({
    schemaVersion: 1, target: '3.2.0', packages: [
      { id: 'NGB.Platform.Templates', kind: 'template' },
      { id: 'NGB.Platform.Test', kind: 'runtime' },
    ],
  }))
  const entries = {
    'content/README.md': strToU8('Current instructions\n'),
    'content/.template.config/template.json': strToU8('{"identity":"NGB"}'),
  }
  for (const host of ['Api', 'Migrator', 'BackgroundJobs']) {
    entries[`content/NgbApplication.${host}/packages.lock.json`] = strToU8('{"version":1}')
  }
  entries['content/web/package-lock.json'] = strToU8('{"lockfileVersion":3}')
  for (const [path, bytes] of Object.entries(entries)) {
    await save(`packaging/templates/${path}`, bytes)
  }
  await save('ui/ngb-crm-web/package-lock.json', '{"lockfileVersion":3}')
  entries['NGB.Platform.Templates.nuspec'] = strToU8('<package />')
  const template = 'artifacts/nuget-release/NGB.Platform.Templates.3.2.0.nupkg'
  await save(template, zipSync({ ...entries, 'content/README.md': strToU8('Old instructions\n') }))
  const runtime = zipSync({ 'NGB.Platform.Test.nuspec': strToU8('<package />') })
  await save('artifacts/nuget-release/NGB.Platform.Test.3.2.0.nupkg', runtime)
  await save('artifacts/nuget-release/NGB.Platform.Test.3.2.0.snupkg', runtime)
  await save('artifacts/npm/ngbplatform-ui-3.2.0.tgz', 'npm fixture')
  execFileSync('git', ['init', '--quiet', '--template='], { cwd: root })
  execFileSync('git', [
    '-c', 'user.name=NGB Test', '-c', 'user.email=ngb@example.invalid',
    '-c', 'commit.gpgsign=false', '-c', 'core.hooksPath=/dev/null',
    'commit', '--quiet', '--allow-empty', '-m', 'Test fixture',
  ], { cwd: root })
  return { root, template, entries }
}

test('seal rejects stale template content before creating a candidate and accepts a rebuilt package', async t => {
  const { root, template, entries } = await releaseFixture(t)
  const candidate = join(root, 'artifacts/candidate')
  const run = action => spawnSync(process.execPath, ['quality/external-consumer/release.mjs', action, candidate], {
    cwd: root, encoding: 'utf8',
  })
  const failed = run('seal')
  assert.notEqual(failed.status, 0)
  assert.match(failed.stderr, /Template package does not match current sources: README\.md/)
  assert.match(failed.stderr, /pack-platform\.sh/)
  assert.ok(!failed.stdout.includes('passed'))
  await assert.rejects(readdir(candidate), { code: 'ENOENT' })

  const rebuilt = zipSync(entries)
  await writeFile(join(root, template), rebuilt)
  const sealed = run('seal')
  assert.equal(sealed.status, 0, sealed.stderr)
  assert.match(sealed.stdout, /Release seal: passed/)
  assert.deepEqual(await readFile(join(candidate, template.slice('artifacts/'.length))), Buffer.from(rebuilt))
  const manifestPath = join(candidate, 'release-manifest.json')
  const manifestBytes = await readFile(manifestPath)
  const manifest = JSON.parse(manifestBytes)
  assert.equal(manifest.artifacts.find(item => item.path.endsWith('Templates.3.2.0.nupkg')).sha256, digest(rebuilt))
  const verified = run('verify')
  assert.equal(verified.status, 0, verified.stderr)
  assert.match(verified.stdout, /Release verify: passed/)
  const repeated = run('seal')
  assert.notEqual(repeated.status, 0)
  assert.match(repeated.stderr, /EEXIST/)
  assert.deepEqual(await readFile(manifestPath), manifestBytes)
})

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
