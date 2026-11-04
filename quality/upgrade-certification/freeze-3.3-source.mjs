import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { cp, mkdir, mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { hashDirectory, verifyFrozenFixture } from '../external-consumer/contracts.mjs'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const directory = join(repository, 'quality/upgrade-certification')
const prior = JSON.parse(await readFile(join(directory, 'source-3.2.0.json'), 'utf8'))
const baseline = JSON.parse(await readFile(join(repository, 'ai-devkit/baselines/cli-3.3.0.json'), 'utf8'))
await verifyFrozenFixture(join(directory, 'fixtures/3.2.0'), prior)
const work = await mkdtemp(join(tmpdir(), 'ngb-source-3.3-'))
const application = join(work, 'application')
await cp(join(directory, 'fixtures/3.2.0'), application, { recursive: true })
for (const filename of ['Directory.Build.props', 'web/package.json']) {
  const path = join(application, filename)
  const text = await readFile(path, 'utf8')
  assert.ok(text.includes('3.2.0'))
  await writeFile(path, text.replaceAll('3.2.0', '3.3.0'))
}
for (const file of baseline.files) {
  assert.equal(digest(file.content), file.sha256)
  const path = join(application, file.path)
  await mkdir(dirname(path), { recursive: true })
  await writeFile(path, file.content)
}
await writeFile(join(application, 'AGENTS.md'), '# Application instructions\n\nPreserve the checkpoint audit history.\n')
await writeFile(join(application, 'CLAUDE.md'), '# Application context\n\nRead AGENTS.md before editing.\n')
await mkdir(join(application, '.agents/skills/checkpoint-review'), { recursive: true })
await writeFile(join(application, '.agents/skills/checkpoint-review/SKILL.md'),
  '---\nname: checkpoint-review\ndescription: Review the application checkpoint workflow.\n---\n\nKeep checkpoint history append-only.\n')
await mkdir(join(application, 'ai/custom'), { recursive: true })
await writeFile(join(application, 'ai/custom/instructions.md'), '# Application guidance\n\nCheckpoint changes require audit evidence.\n')
execFileSync('dotnet', ['restore', 'CertificationApp.slnx', '--force-evaluate', '--no-http-cache',
  '--configfile', 'NuGet.Config', '--packages', join(work, 'packages')], { cwd: application, stdio: 'inherit' })
execFileSync('dotnet', ['build', 'CertificationApp.slnx', '--no-restore', '-c', 'Release', '-m:1',
  '-nr:false', '-p:UseSharedCompilation=false', '-v:minimal'], { cwd: application, stdio: 'inherit' })
execFileSync('npm', ['install', '--package-lock-only', '--ignore-scripts', '--cache', join(work, 'npm-cache')], {
  cwd: join(application, 'web'), stdio: 'inherit',
})
execFileSync('npm', ['ci', '--cache', join(work, 'npm-cache')], { cwd: join(application, 'web'), stdio: 'inherit' })
execFileSync('npm', ['run', 'build'], { cwd: join(application, 'web'), stdio: 'inherit' })
const fixture = join(directory, 'fixtures/3.3.0')
await mkdir(fixture)
await cp(application, fixture, {
  recursive: true,
  filter: path => !path.split('/').some(part => ['bin', 'obj', 'node_modules', 'dist'].includes(part)),
})
const files = await hashDirectory(fixture)
await writeFile(join(directory, 'source-3.3.0.json'), `${JSON.stringify({
  schemaVersion: 1,
  fixture: '3.3.0',
  platformVersion: '3.3.0',
  authoredFromCommit: 'be691b30a86ae6f636134d5ff15f75a678939e28',
  frozenAtUtc: new Date().toISOString(),
  provenance: 'Frozen 3.2.0 application upgraded to published NuGet/npm 3.3.0 packages; isolated registry restore and backend/frontend builds. Bundled CLI bytes are from the signed published 3.3.0 template.',
  cliBaseline: baseline.source,
  files,
  scope: prior.scope,
}, null, 2)}\n`, { flag: 'wx' })
console.log(`Frozen independent 3.3.0 application: ${Object.keys(files).length} files; registry builds at ${work}.`)
