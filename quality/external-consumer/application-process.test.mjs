import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { cp, mkdir, mkdtemp, readFile, readdir, rm, symlink, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'
import {
  applicationFiles, commitUpgrade, loadCandidate, managedPackageFiles,
} from '../../packaging/templates/content/infrastructure/ngb/application.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const cli = join(repository, 'ngb.mjs')

async function temporary(t) {
  const path = await mkdtemp(join(tmpdir(), 'ngb-application-process-'))
  t.after(() => rm(path, { recursive: true, force: true }))
  return path
}

test('public command reports usage and refuses invalid requests without creating an application', async t => {
  const root = await temporary(t)
  const help = spawnSync(process.execPath, [cli, '--help'], { cwd: root, encoding: 'utf8' })
  assert.equal(help.status, 0)
  assert.match(help.stdout, /create MyApp --local/)
  for (const args of [['invalid'], ['deploy'], ['upgrade', '--to', 'latest'], ['create', '../escape']]) {
    const result = spawnSync(process.execPath, [cli, ...args], { cwd: root, encoding: 'utf8' })
    assert.equal(result.status, 1)
    assert.match(result.stderr, /NGB:/)
  }
  assert.deepEqual(await readdir(root), [])
  await writeFile(join(root, 'owned.txt'), 'keep')
  const existing = spawnSync(process.execPath, [cli, 'create', 'ExistingApp', '--output', root], { cwd: root, encoding: 'utf8' })
  assert.equal(existing.status, 1)
  assert.equal(await readFile(join(root, 'owned.txt'), 'utf8'), 'keep')
})

test('upgrade preview is read-only and failed validation preserves all source and dependency files', async t => {
  const root = await temporary(t)
  const app = join(root, 'app')
  await cp(join(repository, 'quality/upgrade-certification/fixtures/3.1.0'), app, { recursive: true })
  await writeFile(join(app, 'user-customization.txt'), 'uncommitted user work')
  const before = await applicationFiles(app)
  const preview = spawnSync(process.execPath, [cli, 'upgrade', '--app', app, '--to', '3.2.0'], { encoding: 'utf8' })
  assert.equal(preview.status, 0, preview.stderr)
  assert.match(preview.stdout, /Plan only/)
  assert.deepEqual(await applicationFiles(app), before)
  assert.ok(!(await readdir(app)).includes('.ngb'))

  const bin = join(root, 'bin')
  await mkdir(bin)
  await writeFile(join(bin, 'dotnet'), '#!/bin/sh\nexit 19\n', { mode: 0o700 })
  const failed = spawnSync(process.execPath, [cli, 'upgrade', '--app', app, '--to', '3.2.0', '--apply'], {
    env: { ...process.env, PATH: `${bin}:${process.env.PATH}` }, encoding: 'utf8',
  })
  assert.equal(failed.status, 1)
  assert.match(failed.stderr, /dotnet restore/)
  assert.deepEqual(await applicationFiles(app), before)
  assert.ok(!(await readdir(join(app, '.ngb'))).includes('operation.lock'))
})

test('candidate identities reject corruption, path traversal, duplicates and mismatched integrity', async t => {
  const root = await temporary(t)
  await mkdir(join(root, 'nuget-release'))
  await mkdir(join(root, 'npm'))
  const artifacts = []
  for (const path of ['nuget-release/NGB.Platform.Templates.3.2.0.nupkg', 'npm/ngbplatform-ui-3.2.0.tgz']) {
    const bytes = Buffer.from(path)
    await writeFile(join(root, path), bytes)
    const artifact = { path, size: bytes.length, sha256: createHash('sha256').update(bytes).digest('hex') }
    if (path.endsWith('.tgz')) artifact.integrity = `sha512-${createHash('sha512').update(bytes).digest('base64')}`
    artifacts.push(artifact)
  }
  const manifest = { schemaVersion: 1, version: '3.2.0', artifacts }
  const save = value => writeFile(join(root, 'release-manifest.json'), JSON.stringify(value))
  await save(manifest)
  assert.equal((await loadCandidate(root)).version, '3.2.0')
  for (const change of [
    value => { value.artifacts[0].sha256 = '0'.repeat(64) },
    value => { value.artifacts[0].size = 0 },
    value => { value.artifacts[0].path = '../escape' },
    value => { value.artifacts[1].integrity = 'sha512-invalid' },
    value => { value.artifacts.push(value.artifacts[0]) },
    value => { value.artifacts.pop() },
    value => { value.version = 'latest' },
    value => { value.schemaVersion = 2 },
  ]) {
    const invalid = structuredClone(manifest)
    change(invalid)
    await save(invalid)
    await assert.rejects(loadCandidate(root))
  }
})

test('applying dependency changes preserves user code and records recoverable originals', async t => {
  const root = await temporary(t)
  await writeFile(join(root, 'dependency.json'), 'old')
  await writeFile(join(root, 'user.cs'), 'user code')
  const before = await applicationFiles(root)
  await commitUpgrade(root, before, { 'dependency.json': Buffer.from('new'), 'new.lock': Buffer.from('lock') }, {
    source: '3.1.0', target: '3.2.0', guide: 'guide',
  })
  assert.equal(await readFile(join(root, 'dependency.json'), 'utf8'), 'new')
  assert.equal(await readFile(join(root, 'user.cs'), 'utf8'), 'user code')
  const [entry] = await readdir(join(root, '.ngb/upgrades'))
  assert.equal(await readFile(join(root, '.ngb/upgrades', entry, 'before/dependency.json'), 'utf8'), 'old')
  const receipt = JSON.parse(await readFile(join(root, '.ngb/upgrades', entry, 'plan.json'), 'utf8'))
  assert.equal(receipt.files['new.lock'], null)
})

test('user-owned local configuration and edited managed files are never overwritten', async t => {
  const root = await temporary(t)
  assert.deepEqual(await managedPackageFiles(root), {})
  const path = join(root, 'NuGet.Local.Config')
  await writeFile(path, 'user config')
  await assert.rejects(managedPackageFiles(root), /user-owned/)
  await mkdir(join(root, '.ngb'))
  const files = { 'NuGet.Local.Config': createHash('sha256').update('user config').digest('hex') }
  await writeFile(join(root, '.ngb/packages.json'), JSON.stringify({ schemaVersion: 1, files }))
  assert.deepEqual(await managedPackageFiles(root), files)
  await writeFile(path, 'new user edit')
  await assert.rejects(managedPackageFiles(root), /changed or removed/)
  assert.equal(await readFile(path, 'utf8'), 'new user edit')
})

test('concurrent edits and symbolic-link destinations are rejected before writing', async t => {
  const root = await temporary(t)
  await writeFile(join(root, 'dependency.json'), 'old')
  const before = await applicationFiles(root)
  await writeFile(join(root, 'dependency.json'), 'user edit')
  await assert.rejects(commitUpgrade(root, before, { 'dependency.json': Buffer.from('new') }, {}), /changed during validation/)
  await writeFile(join(root, 'added.txt'), 'added')
  await assert.rejects(commitUpgrade(root, before, {}, {}), /files changed/)
  const current = await applicationFiles(root)
  await assert.rejects(commitUpgrade(root, current, { '../escape': Buffer.from('new') }, {}), /Unsafe/)
  await assert.rejects(commitUpgrade(root, current, { '/escape': Buffer.from('new') }, {}), /Unsafe/)
  const outside = await temporary(t)
  await symlink(outside, join(root, '.ngb'))
  await assert.rejects(commitUpgrade(root, current, {}, {}), /symbolic link/)
  await symlink(outside, join(root, 'linked-source'))
  await assert.rejects(applicationFiles(root), /Symbolic link/)
})

test('a failed file replacement restores changes already applied', async t => {
  const root = await temporary(t)
  await writeFile(join(root, 'first.json'), 'first original')
  await writeFile(join(root, 'second.json'), 'second original')
  const before = await applicationFiles(root)
  const failingWrite = async (path, bytes) => {
    if (path.endsWith('second.json')) throw new Error('EIO: injected disk failure')
    await writeFile(path, bytes)
  }
  await assert.rejects(commitUpgrade(root, before, {
    'first.json': Buffer.from('first new'),
    'added.lock': Buffer.from('new lock'),
    'second.json': Buffer.from('second new'),
  }, { source: '3.1.0', target: '3.2.0' }, failingWrite), /EIO/)
  assert.equal(await readFile(join(root, 'first.json'), 'utf8'), 'first original')
  assert.equal(await readFile(join(root, 'second.json'), 'utf8'), 'second original')
  assert.ok(!(await readdir(root)).includes('added.lock'))
})

test('deployment failure keeps writers stopped and never starts target hosts', async t => {
  const root = await temporary(t)
  const bin = join(root, 'bin')
  const calls = join(root, 'calls')
  await mkdir(bin)
  const services = Object.fromEntries(['api', 'jobs', 'web', 'migrator'].map(name => [name, {
    build: { context: '.' }, depends_on: { migrator: { condition: 'service_completed_successfully' } },
  }]))
  const docker = `#!/bin/sh
printf '%s\\n' "$*" >> "$NGB_TEST_CALLS"
case "$*" in
  'compose config --format json') printf '%s\\n' '${JSON.stringify({ services })}' ;;
  'compose run --rm --no-deps migrator') exit 27 ;;
esac
`
  await writeFile(join(bin, 'docker'), docker, { mode: 0o700 })
  const result = spawnSync(process.execPath, [cli, 'deploy', '--app', root, '--backup-confirmed'], {
    env: { ...process.env, PATH: `${bin}:${process.env.PATH}`, NGB_TEST_CALLS: calls }, encoding: 'utf8',
  })
  assert.equal(result.status, 1)
  assert.deepEqual((await readFile(calls, 'utf8')).trim().split('\n'), [
    'compose config --format json', 'compose build', 'compose stop api jobs web', 'compose run --rm --no-deps migrator',
  ])
})
