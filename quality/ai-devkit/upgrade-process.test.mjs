import assert from 'node:assert/strict'
import { spawn, spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { once } from 'node:events'
import { chmod, copyFile, cp, mkdir, mkdtemp, readFile, readdir, realpath, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'
import { setTimeout as delay } from 'node:timers/promises'
import { applicationFiles } from '../../packaging/templates/content/infrastructure/ngb/application.mjs'
import { archive, cliFiles, entry } from './fixtures/cli.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const cli = join(repository, 'ngb.mjs')
const hash = bytes => createHash('sha256').update(bytes).digest('hex')

async function fixture(t) {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'ngb-upgrade-process-')))
  t.after(() => rm(root, { recursive: true, force: true }))
  const application = join(root, 'application')
  await cp(join(repository, 'quality/upgrade-certification/fixtures/3.3.0'), application, { recursive: true })
  const source = await applicationFiles(join(repository, 'packaging/templates/content'))
  const files = cliFiles()
  for (const [path, bytes] of Object.entries(source)) {
    if (/^infrastructure\/(?:ngb\.mjs|configuration\.mjs|ngb\/|validation\/)/.test(path)) files[`package/${path}`] = bytes
  }
  const cliArchive = archive(Object.entries(files).map(([path, bytes]) => entry(path, bytes)))
  const packages = join(root, 'candidate')
  await mkdir(join(packages, 'nuget-release'), { recursive: true })
  await mkdir(join(packages, 'npm'))
  // The runtime packages are deliberately not usable: this process contract
  // proves planning/rollback without claiming a package build or certification.
  const artifacts = []
  for (const [path, bytes] of Object.entries({
    'nuget-release/NGB.Platform.Templates.3.4.0.nupkg': Buffer.from('preview fixture'),
    'npm/ngbplatform-ui-3.4.0.tgz': Buffer.from('preview fixture'),
    'npm/ngbplatform-cli-3.4.0.tgz': cliArchive,
  })) {
    await writeFile(join(packages, path), bytes)
    artifacts.push({ path, size: bytes.length, sha256: hash(bytes),
      ...(path.endsWith('.tgz') ? { integrity: `sha512-${createHash('sha512').update(bytes).digest('base64')}` } : {}) })
  }
  await writeFile(join(packages, 'release-manifest.json'), JSON.stringify({ schemaVersion: 1, version: '3.4.0', artifacts }))
  const args = ['upgrade', '--app', application, '--to', '3.4.0', '--packages', packages, '--format', 'json']
  const run = (extra = [], env = process.env) => spawnSync(process.execPath, [cli, ...args, ...extra], {
    cwd: root, env, encoding: 'utf8', timeout: 30000,
  })
  return { root, application, args, run }
}

test('a source-free 3.3 upgrade preview includes dependencies, target CLI/helper and Kit without writes', async t => {
  const { application, run } = await fixture(t)
  const before = await applicationFiles(application)
  const result = run()
  assert.equal(result.status, 0, result.stderr || result.stdout)
  const value = JSON.parse(result.stdout)
  assert.equal(value.sourceVersion, '3.3.0')
  assert.equal(value.targetVersion, '3.4.0')
  assert.equal(value.applied, false)
  assert.ok(value.dependencies.includes('Directory.Build.props'))
  for (const path of ['infrastructure/ngb/contracts.mjs', 'infrastructure/validation/AssemblyInspector.cs',
    'ai/kit.json', 'AGENTS.md', '.agents/skills/ngb-documents/SKILL.md', '.claude/skills/ngb-documents/SKILL.md']) {
    assert.ok(value.plan.entries.some(entry => entry.path === path), path)
  }
  assert.deepEqual(await applicationFiles(application), before)
  assert.ok(!(await readdir(application)).includes('.ngb'))
})

test('reviewed collisions reach isolated validation, whose failure leaves source CLI/SDK/Kit and user work intact', async t => {
  const { root, application, run } = await fixture(t)
  const path = join(application, 'ai/vendor/instructions.md')
  await mkdir(dirname(path), { recursive: true })
  await writeFile(path, 'Existing user-owned instructions\n')
  const preview = run()
  assert.equal(preview.status, 4, preview.stderr || preview.stdout)
  const value = JSON.parse(preview.stdout)
  const collision = value.plan.entries.find(entry => entry.path === 'ai/vendor/instructions.md')
  assert.equal(collision.content.local, 'Existing user-owned instructions\n')
  const resolutions = join(application, 'reviewed-resolutions.json')
  await writeFile(resolutions, JSON.stringify({ schemaVersion: 1, planSha256: value.plan.planSha256,
    decisions: [{ id: collision.id, choice: 'incoming' }] }))
  const before = await applicationFiles(application)
  const bin = join(root, 'bin')
  await mkdir(bin)
  const executable = join(bin, process.platform === 'win32' ? 'dotnet.exe' : 'dotnet')
  await copyFile(process.execPath, executable)
  await chmod(executable, 0o700)
  const failed = run(['--apply', '--resolutions', resolutions], { ...process.env,
    PATH: bin + (process.platform === 'win32' ? ';' : ':') + process.env.PATH })
  assert.equal(failed.status, 2, failed.stderr || failed.stdout)
  const error = JSON.parse(failed.stdout)
  assert.match(error.checks[0].message, /dotnet restore failed/)
  assert.deepEqual(await applicationFiles(application), before)
  assert.ok(!(await readdir(application)).includes('.ngb'))
  await writeFile(path, 'Changed after review\n')
  const stale = run(['--apply', '--resolutions', resolutions])
  assert.equal(stale.status, 2)
  assert.match(JSON.parse(stale.stdout).checks[0].message, /stale/)
  assert.equal(await readFile(path, 'utf8'), 'Changed after review\n')
})

test('interrupted upgrade validation preserves the application and reports the terminating signal', { timeout: 15000 }, async t => {
  for (const [signal, exitCode] of [['SIGINT', 130], ['SIGTERM', 143]]) {
    const { root, application, args } = await fixture(t)
    const before = await applicationFiles(application)
    const bin = join(root, 'bin')
    const marker = join(root, 'restore-started.json')
    await mkdir(bin)
    const executable = join(bin, 'dotnet')
    await writeFile(executable, `#!${process.execPath}
import { writeFileSync } from 'node:fs'
writeFileSync(${JSON.stringify(marker)}, JSON.stringify({ pid: process.pid, cwd: process.cwd() }))
setInterval(() => {}, 1000)
`)
    await chmod(executable, 0o700)
    const child = spawn(process.execPath, [cli, ...args, '--apply'], {
      cwd: root,
      env: { ...process.env, PATH: bin + ':' + process.env.PATH },
      stdio: ['ignore', 'pipe', 'pipe'],
    })
    let output = ''
    child.stdout.on('data', chunk => { output += chunk })
    child.stderr.on('data', chunk => { output += chunk })
    t.after(() => { if (child.exitCode === null) child.kill('SIGKILL') })
    const exited = once(child, 'exit')
    let receipt
    for (let attempt = 0; attempt < 200; attempt += 1) {
      try {
        receipt = JSON.parse(await readFile(marker, 'utf8'))
        break
      } catch (error) {
        if (error.code !== 'ENOENT') throw error
      }
      assert.equal(child.exitCode, null, output)
      await delay(25)
    }
    assert.ok(receipt, 'The upgrade never reached isolated restore.')
    child.kill(signal)
    const [code, terminatingSignal] = await exited
    assert.equal(code, exitCode, output)
    assert.equal(terminatingSignal, null)
    assert.throws(() => process.kill(receipt.pid, 0), { code: 'ESRCH' })
    await assert.rejects(readdir(receipt.cwd), { code: 'ENOENT' })
    assert.deepEqual(await applicationFiles(application), before)
    assert.ok(!(await readdir(application)).includes('.ngb'))
  }
})
