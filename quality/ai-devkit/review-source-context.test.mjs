import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import test from 'node:test'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { reviewSourceContext } from './review-source-context.mjs'

test('large test updates retain every changed byte in replayable diffs without duplicating trusted source', async t => {
  const root = await mkdtemp(join(tmpdir(), 'ngb-review-patch-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  const original = Array.from({ length: 2400 }, (_, index) => `// unchanged context ${index}`).join('\n')
    + '\nAssert.Equal("старое", actual);'
  const modified = original.replace('// unchanged context 10', '// changed near the start')
    .replace('// unchanged context 1200', '  // whitespace and new content')
    .replace('Assert.Equal("старое", actual);', 'Assert.Equal("новое", actual);\n')
  const paths = ['EquipmentInspection.IntegrationTests/InspectionTests.cs', 'EquipmentInspection.Tests/ContractTests.cs',
    'web/tests/e2e/inspection.spec.ts']
  const files = Object.fromEntries(paths.map(path => [path, Buffer.from(modified)]))
  const before = Object.fromEntries(paths.map(path => [path, original]))
  const context = await reviewSourceContext(paths, files, before)
  assert.deepEqual(context.sources, {})
  assert.deepEqual(context.originalSources, {})
  assert.deepEqual(Object.keys(context.testChanges), paths)
  assert.ok(Buffer.byteLength(JSON.stringify(context)) < 20000)
  assert.ok(Buffer.byteLength(JSON.stringify({ files: before, originals: before })) > 262144)
  for (const path of paths) {
    const change = context.testChanges[path]
    assert.equal(change.beforeSha256, digest(original))
    assert.equal(change.afterSha256, digest(files[path]))
    assert.ok(change.patch.includes('-Assert.Equal("старое", actual);'))
    assert.ok(change.patch.includes('+Assert.Equal("новое", actual);'))
    assert.ok(change.patch.includes('+  // whitespace and new content'))
    assert.ok(change.patch.includes('No newline at end of file'))
    const target = join(root, path)
    await mkdir(dirname(target), { recursive: true })
    await writeFile(target, original)
    execFileSync('git', ['apply', '-p2', '--whitespace=nowarn', '-'], { cwd: root, input: change.patch })
    assert.deepEqual(await readFile(target), files[path])
  }
})

test('new tests, application code, SQL and documentation remain complete and hash inputs stay untouched', async () => {
  const files = {
    'EquipmentInspection.Runtime/Validator.cs': Buffer.from('new application code\n'),
    'EquipmentInspection.IntegrationTests/NewTests.cs': Buffer.from('new integration test\n'),
    'EquipmentInspection.PostgreSql/db/migrations/V1__change.sql': Buffer.from('alter table example;\n'),
    'README.md': Buffer.from('new documentation\n'),
  }
  const before = { 'EquipmentInspection.Runtime/Validator.cs': 'old application code\n', 'README.md': 'old documentation\n' }
  const originalFiles = structuredClone(files)
  const originalBefore = structuredClone(before)
  const context = await reviewSourceContext(Object.keys(files), files, before)
  assert.deepEqual(context.testChanges, {})
  for (const [path, source] of Object.entries(files)) {
    assert.equal(context.sources[path], source.toString('utf8'))
    assert.equal(context.originalSources[path], before[path] ?? null)
    assert.deepEqual([...source], [...originalFiles[path]])
  }
  assert.deepEqual(before, originalBefore)
})

test('diff preparation rejects unchanged tests and unsafe paths instead of returning partial context', async () => {
  const path = 'EquipmentInspection.Tests/Tests.cs'
  await assert.rejects(reviewSourceContext([path], { [path]: Buffer.from('unchanged') }, { [path]: 'unchanged' }), /no source diff/)
  await assert.rejects(reviewSourceContext(['../outside.cs'], { '../outside.cs': Buffer.from('invalid') }, {}))
})
