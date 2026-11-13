import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { mkdir, mkdtemp, rename, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import { sourceFiles } from './source-files.mjs'

test('source inventory follows working-tree additions, deletions and renames without depending on staging', async t => {
  const root = await mkdtemp(join(tmpdir(), 'ngb-source-files-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  const git = args => execFileSync('git', args, { cwd: root, encoding: 'utf8' })
  git(['init', '--quiet', '--template='])
  await mkdir(join(root, 'nested'))
  for (const [path, contents] of Object.entries({
    '.gitignore': 'ignored.txt\n',
    'AGENTS.md': 'Local instructions',
    'nested/.DS_Store': 'Excluded metadata',
    'tracked.txt': 'Tracked source',
    'removed workflow.yml': 'Deleted workflow',
  })) {
    await writeFile(join(root, path), contents)
  }
  git(['add', '--all'])
  await writeFile(join(root, 'untracked\nsource.txt'), 'Untracked source')
  await writeFile(join(root, 'ignored.txt'), 'Ignored input')
  await rm(join(root, 'removed workflow.yml'))
  const expected = ['.gitignore', 'tracked.txt', 'untracked\nsource.txt']
  assert.deepEqual(sourceFiles(root), expected)
  git(['add', '-u'])
  assert.deepEqual(sourceFiles(root), expected)
  git(['add', '--', 'untracked\nsource.txt'])
  assert.deepEqual(sourceFiles(root), expected)

  await rename(join(root, 'tracked.txt'), join(root, 'renamed source.txt'))
  const renamed = ['.gitignore', 'renamed source.txt', 'untracked\nsource.txt']
  assert.deepEqual(sourceFiles(root), renamed)
  git(['add', '--all'])
  assert.deepEqual(sourceFiles(root), renamed)
  await writeFile(join(root, 'removed workflow.yml'), 'Restored workflow')
  assert.ok(sourceFiles(root).includes('removed workflow.yml'))
})
