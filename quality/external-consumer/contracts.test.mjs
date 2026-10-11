import assert from 'node:assert/strict'
import { mkdtemp, mkdir, rm, symlink, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import {
  assertConsumerIsolation,
  assertExactVersion,
  assertGateResults,
  assertOutsideRepository,
  hashDirectory,
  verifyFrozenFixture,
} from './contracts.mjs'

test('versions and repository boundaries reject implicit consumption', () => {
  assertExactVersion('3.2.0')
  for (const version of [undefined, 3, '*', '^3.2.0', '3.2.0-preview.1']) {
    assert.throws(() => assertExactVersion(version))
  }
  assertOutsideRepository('/work/repository', '/work/application')
  assertOutsideRepository('/work/repository', '/work')
  assert.throws(() => assertOutsideRepository('/work/repository', '/work/repository'))
  assert.throws(() => assertOutsideRepository('/work/repository', '/work/repository/app'))
})

test('frozen source rejects edited, missing, added and linked inputs', async t => {
  const root = await mkdtemp(join(tmpdir(), 'ngb-freeze-test-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  await mkdir(join(root, 'nested'))
  await writeFile(join(root, 'nested', 'source.txt'), 'original')
  const manifest = { platformVersion: '3.1.0', files: await hashDirectory(root) }
  await verifyFrozenFixture(root, manifest)
  await writeFile(join(root, 'nested', 'source.txt'), 'changed')
  await assert.rejects(verifyFrozenFixture(root, manifest), /changed/)
  await rm(join(root, 'nested', 'source.txt'))
  await assert.rejects(verifyFrozenFixture(root, manifest), /changed/)
  await writeFile(join(root, 'extra.txt'), 'extra')
  await assert.rejects(verifyFrozenFixture(root, manifest), /extra.txt/)
  await symlink(join(root, 'extra.txt'), join(root, 'link'))
  await assert.rejects(hashDirectory(root), /Symbolic links/)
})

test('directory hashing excludes matching files and prunes matching directories', async t => {
  const root = await mkdtemp(join(tmpdir(), 'ngb-hash-exclusions-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  await mkdir(join(root, 'nested', 'bin'), { recursive: true })
  await writeFile(join(root, 'nested', 'source.txt'), 'source')
  await writeFile(join(root, '.env'), 'private')
  await symlink(join(root, 'missing'), join(root, 'nested', 'bin', 'link'))
  const files = await hashDirectory(root, ['**/bin/**', '.env'])
  assert.deepEqual(Object.keys(files), ['nested/source.txt'])
})

test('isolated source rejects every repository, project and package fallback', async t => {
  const root = await mkdtemp(join(tmpdir(), 'ngb-isolation-test-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  const repository = join(root, 'repository')
  const consumer = join(root, 'consumer')
  await mkdir(repository)
  await mkdir(consumer)
  await writeFile(join(consumer, 'README.md'), 'independent')
  await writeFile(join(consumer, 'App.csproj'), '<ProjectReference Include="Module/Module.csproj" />')
  await writeFile(join(consumer, 'package.json'), JSON.stringify({ dependencies: { vue: '3.5.0' } }))
  await assertConsumerIsolation(consumer, repository)
  await writeFile(join(consumer, 'App.csproj'), '<ProjectReference Include="../repository/Platform.csproj" />')
  await assert.rejects(assertConsumerIsolation(consumer, repository), /escapes/)
  await writeFile(join(consumer, 'App.csproj'), `<Import Project="${repository}/Directory.Build.props" />`)
  await assert.rejects(assertConsumerIsolation(consumer, repository), /Repository/)
  await writeFile(join(consumer, 'App.csproj'), '<Project />')
  for (const manifest of [
    { dependencies: { ui: 'workspace:*' } },
    { dependencies: { ui: 'link:../ui' } },
    { workspaces: ['packages/*'] },
    { devDependencies: { ui: 'file:../ui' } },
    { dependencies: { ui: '../ui' } },
    { dependencies: { ui: '/ui' } },
  ]) {
    await writeFile(join(consumer, 'package.json'), JSON.stringify(manifest))
    await assert.rejects(assertConsumerIsolation(consumer, repository))
  }
  await writeFile(join(consumer, 'package.json'), '{}')
  await writeFile(join(consumer, 'tool.mjs'), 'throw new Error("Refusing symbolic link: filename")\n')
  await assertConsumerIsolation(consumer, repository)
  await writeFile(join(consumer, 'tool.mjs'), 'const dependency = "link:../repository"\n')
  await assert.rejects(assertConsumerIsolation(consumer, repository), /Repository/)
})

test('release evidence fails closed for missing commands, assertions or identity', () => {
  const candidate = 'a'.repeat(64)
  const matrix = { gates: [{ id: 'clean-starter', command: 'run clean-starter', blocks: 'publication' }] }
  const evidence = { artifactManifestSha256: candidate, stage: 'publication', results: {} }
  assert.throws(() => assertGateResults(matrix, evidence, 'another'), /manifest/)
  assert.throws(() => assertGateResults(matrix, evidence, candidate), /clean-starter/)
  evidence.results['clean-starter'] = { status: 'failed', evidenceSha256: 'b'.repeat(64) }
  assert.throws(() => assertGateResults(matrix, evidence, candidate), /clean-starter/)
  evidence.results['clean-starter'].status = 'passed'
  delete evidence.results['clean-starter'].evidenceSha256
  assert.throws(() => assertGateResults(matrix, evidence, candidate), /clean-starter/)
  evidence.results['clean-starter'].evidenceSha256 = 'b'.repeat(64)
  assertGateResults(matrix, evidence, candidate)
  matrix.gates[0].command = ''
  assert.throws(() => assertGateResults(matrix, evidence, candidate), /clean-starter/)
  evidence.stage = 'promotion'
  assert.throws(() => assertGateResults(matrix, evidence, candidate), /no required gates/)
  assert.throws(() => assertGateResults(matrix, evidence, 'bad hash'), /manifest/)
  assert.throws(() => assertGateResults(matrix, evidence, 'c'.repeat(64)), /manifest/)
})
