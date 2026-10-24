import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { cp, mkdir, mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { hashDirectory, verifyFrozenFixture } from '../external-consumer/contracts.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const directory = join(repository, 'quality/upgrade-certification')
const prior = JSON.parse(await readFile(join(directory, 'source-3.1.0.json'), 'utf8'))
await verifyFrozenFixture(join(directory, 'fixtures/3.1.0'), prior)
const work = await mkdtemp(join(tmpdir(), 'ngb-source-3.2-'))
const application = join(work, 'application')
await cp(join(directory, 'fixtures/3.1.0'), application, { recursive: true })
for (const filename of ['Directory.Build.props', 'web/package.json']) {
  const path = join(application, filename)
  const text = await readFile(path, 'utf8')
  assert.ok(text.includes('3.1.0'))
  await writeFile(path, text.replaceAll('3.1.0', '3.2.0'))
}
execFileSync('dotnet', ['restore', 'CertificationApp.slnx', '--force-evaluate', '--no-http-cache', '--configfile', 'NuGet.Config', '--packages', join(work, 'packages')], {
  cwd: application, stdio: 'inherit',
})
execFileSync('dotnet', ['build', 'CertificationApp.slnx', '--no-restore', '-m:1', '-nr:false', '-v:minimal'], {
  cwd: application, stdio: 'inherit',
})
execFileSync('npm', ['install', '--package-lock-only', '--ignore-scripts', '--cache', join(work, 'npm-cache')], {
  cwd: join(application, 'web'), stdio: 'inherit',
})
const fixture = join(directory, 'fixtures/3.2.0')
await mkdir(fixture)
await cp(application, fixture, {
  recursive: true,
  filter: path => !path.split('/').some(part => ['bin', 'obj', 'node_modules'].includes(part)),
})
const files = await hashDirectory(fixture)
await writeFile(join(directory, 'source-3.2.0.json'), `${JSON.stringify({
  schemaVersion: 1,
  fixture: '3.2.0',
  platformVersion: '3.2.0',
  authoredFromCommit: '06c169ba8f409d5b824e153bfd3ea262cfbb9581',
  frozenAtUtc: new Date().toISOString(),
  provenance: 'Frozen 3.1.0 application upgraded to published NuGet/npm 3.2.0 packages; isolated registry restore and build.',
  files,
  scope: prior.scope,
}, null, 2)}\n`, { flag: 'wx' })
console.log(`Frozen independent 3.2.0 application: ${Object.keys(files).length} files; registry build at ${work}.`)
