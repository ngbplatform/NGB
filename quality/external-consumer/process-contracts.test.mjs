import assert from 'node:assert/strict'
import { execFileSync, spawnSync } from 'node:child_process'
import { chmod, cp, mkdir, mkdtemp, readFile, readdir, realpath, rm, symlink, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'
import { strToU8, zipSync } from 'fflate'
import { digest } from './release-contracts.mjs'
import { certificationChecks } from '../ai-devkit/certification-data.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')

async function apiCompatibilityFixture(t) {
  const root = await mkdtemp(join(tmpdir(), 'ngb-api-compatibility-process-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  for (const path of ['packaging/nuget', 'quality/external-consumer', 'bin', 'feed']) {
    await mkdir(join(root, path), { recursive: true })
  }
  for (const path of ['packaging/nuget/verify-api-compatibility.mjs', 'quality/external-consumer/dependency-cache.mjs']) {
    await cp(join(repository, path), join(root, path))
  }
  await writeFile(join(root, 'packaging/nuget/baselines.json'), JSON.stringify({
    target: '3.4.0', packages: [{ id: 'NGB.Platform.Test', kind: 'runtime', previous: '3.3.0' }],
  }))
  await writeFile(join(root, 'feed/NGB.Platform.Test.3.4.0.nupkg'), 'candidate package')
  const calls = join(root, 'calls.jsonl')
  const stub = `#!${process.execPath}
const assert = require('node:assert/strict')
const { appendFileSync, mkdirSync, writeFileSync } = require('node:fs')
const { basename, dirname, join } = require('node:path')
const args = process.argv.slice(2)
const mode = process.env.NGB_TEST_MODE
appendFileSync(process.env.NGB_TEST_CALLS, JSON.stringify(args) + '\\n')
if (args[0] === 'restore') {
  const version = basename(process.cwd())
  const archive = join(process.cwd(), 'packages/ngb.platform.test', version, 'ngb.platform.test.' + version + '.nupkg')
  mkdirSync(dirname(archive), { recursive: true })
  writeFileSync(archive, 'baseline package')
} else if (args[0] === 'msbuild') {
  assert.ok(args.includes('-getProperty:TargetFrameworkMoniker'))
  const graph = {
    Properties: { TargetFrameworkMoniker: mode === 'missing-moniker' ? '' : '.NETCoreApp,Version=v10.0' },
    Items: { ReferencePath: mode === 'missing-references' ? [] : [{ FullPath: '/reference/System.Runtime.dll' }] },
  }
  console.log(JSON.stringify(graph))
} else {
  assert.equal(args[0], 'package')
  for (const flag of ['--package-assembly-references', '--baseline-package-assembly-references']) {
    assert.equal(args[args.indexOf(flag) + 1], '.NETCoreApp,Version=v10.0|/reference/System.Runtime.dll')
  }
  if (mode === 'stdout-diagnostic') console.log('CP1003: Could not find a reference directory.')
  if (mode === 'stderr-diagnostic') console.error('CP1003: Could not find a reference directory.')
  if (mode === 'unresolved-reference') console.error('Referenced assembly could not be resolved.')
  if (mode === 'package-diagnostic') console.log('PKV0001: Package validation diagnostic.')
  if (mode === 'nonzero') process.exit(23)
  console.log('APICompat ran successfully without finding any breaking changes.')
}
`
  for (const name of ['dotnet', 'apicompat']) {
    await writeFile(join(root, 'bin', name), stub, { mode: 0o700 })
  }
  const run = mode => spawnSync(process.execPath, ['packaging/nuget/verify-api-compatibility.mjs', 'feed'], {
    cwd: root, encoding: 'utf8',
    env: { ...process.env, PATH: join(root, 'bin'), TMPDIR: root,
      NGB_APICOMPAT: join(root, 'bin/apicompat'), NGB_TEST_CALLS: calls, NGB_TEST_MODE: mode },
  })
  return { root, run, calls }
}

test('API compatibility passes resolved framework monikers and records actual package identities', async t => {
  const { root, run } = await apiCompatibilityFixture(t)
  const result = run('passed')
  assert.equal(result.status, 0, result.stderr)
  const report = JSON.parse(await readFile(join(root, 'feed/api-compatibility.json'), 'utf8'))
  assert.deepEqual(report.results, [{ package: 'NGB.Platform.Test', baseline: '3.3.0',
    baselineSha256: digest('baseline package'), candidateSha256: digest('candidate package'), status: 'passed' }])
})

test('API compatibility rejects missing references, failed processes and zero-exit diagnostics on either stream', async t => {
  for (const mode of ['missing-moniker', 'missing-references', 'stdout-diagnostic', 'stderr-diagnostic',
    'unresolved-reference', 'package-diagnostic', 'nonzero']) {
    const { root, run, calls } = await apiCompatibilityFixture(t)
    const result = run(mode)
    assert.notEqual(result.status, 0, mode)
    assert.match(result.stderr, /Missing resolved|Unresolved compatibility diagnostic|API compatibility failed/, mode)
    assert.ok(!result.stdout.includes('Compatible:'), mode)
    await assert.rejects(readFile(join(root, 'feed/api-compatibility.json')), { code: 'ENOENT' })
    if (mode.startsWith('missing-')) {
      const commands = (await readFile(calls, 'utf8')).trim().split('\n').map(line => JSON.parse(line))
      assert.ok(!commands.some(args => args[0] === 'package'))
    }
  }
})

async function releaseFixture(t, { kit = false } = {}) {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'ngb-seal-process-')))
  const version = kit ? '3.4.0' : '3.2.0'
  t.after(() => rm(root, { recursive: true, force: true }))
  const save = async (path, bytes) => {
    const destination = join(root, path)
    await mkdir(dirname(destination), { recursive: true })
    await writeFile(destination, bytes)
  }
  await mkdir(join(root, 'quality/external-consumer'), { recursive: true })
  for (const name of [
    'release.mjs', 'release-contracts.mjs', 'contracts.mjs', 'checkpoints.mjs',
    'execution.mjs', 'environment.mjs', 'toolchain.json', 'matrix.json', 'source-files.mjs',
  ]) {
    const path = `quality/external-consumer/${name}`
    const source = name === 'matrix.json' && !kit ? 'quality/external-consumer/history/matrix-3.2.0.json' : path
    await cp(join(repository, source), join(root, path))
  }
  await save('quality/ai-devkit/certification-data.mjs',
    await readFile(join(repository, 'quality/ai-devkit/certification-data.mjs')))
  await symlink(join(repository, 'quality/node_modules'), join(root, 'quality/node_modules'))
  await save('.gitignore', 'artifacts/\nquality/node_modules\n')
  await save(`quality/upgrade-certification/source-${kit ? '3.3.0' : '3.1.0'}.json`, '{}')
  await save('packaging/nuget/baselines.json', JSON.stringify({
    schemaVersion: 1, target: version, packages: [
      { id: 'NGB.Platform.Templates', kind: 'template' },
      { id: 'NGB.Platform.Test', kind: 'runtime' },
    ],
  }))
  const entries = {
    'content/README.md': strToU8('Current instructions\n'),
    'content/.template.config/template.json': strToU8('{"identity":"NGB"}'),
  }
  for (const name of ['contracts.mjs', 'kit-contracts.mjs', 'kit-files.mjs', 'file-data.mjs', 'transaction.mjs']) {
    const path = `content/infrastructure/ngb/${name}`
    entries[path] = await readFile(join(repository, 'packaging/templates', path))
  }
  for (const host of ['Api', 'Migrator', 'BackgroundJobs', ...kit ? ['Tests', 'IntegrationTests'] : []]) {
    entries[`content/NgbApplication.${host}/packages.lock.json`] = strToU8('{"version":1}')
  }
  if (kit) entries['content/infrastructure/validation/packages.lock.json'] = strToU8('{"version":1}')
  entries['content/web/package-lock.json'] = strToU8('{"lockfileVersion":3}')
  for (const [path, bytes] of Object.entries(entries)) {
    await save(`packaging/templates/${path}`, bytes)
  }
  await save('ui/ngb-crm-web/package-lock.json', '{"lockfileVersion":3}')
  entries['NGB.Platform.Templates.nuspec'] = strToU8('<package />')
  const template = `artifacts/nuget-release/NGB.Platform.Templates.${version}.nupkg`
  await save(template, zipSync({ ...entries, 'content/README.md': strToU8('Old instructions\n') }))
  const runtime = zipSync({ 'NGB.Platform.Test.nuspec': strToU8('<package />') })
  await save(`artifacts/nuget-release/NGB.Platform.Test.${version}.nupkg`, runtime)
  await save(`artifacts/nuget-release/NGB.Platform.Test.${version}.snupkg`, runtime)
  await save(`artifacts/npm/ngbplatform-ui-${version}.tgz`, 'npm fixture')
  if (kit) await save(`artifacts/npm/ngbplatform-cli-${version}.tgz`, 'CLI fixture')
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

test('seal accepts a deleted tracked workflow independently of staging and detects later source changes', async t => {
  const { root, template, entries } = await releaseFixture(t)
  await writeFile(join(root, template), zipSync(entries))
  const workflow = '.github/workflows/external-app-certification.yml'
  const tracked = 'retained-input.txt'
  await mkdir(dirname(join(root, workflow)), { recursive: true })
  await writeFile(join(root, workflow), 'name: deleted-workflow\n')
  await writeFile(join(root, tracked), 'Tracked source\n')
  execFileSync('git', ['add', '--', workflow, tracked], { cwd: root })
  await rm(join(root, workflow))
  const candidate = join(root, 'artifacts/candidate')
  const run = action => spawnSync(process.execPath, ['quality/external-consumer/release.mjs', action, candidate], {
    cwd: root, encoding: 'utf8',
  })
  const sealed = run('seal')
  assert.equal(sealed.status, 0, sealed.stderr)
  const manifest = await readFile(join(candidate, 'release-manifest.json'))
  assert.equal(run('verify').status, 0)
  execFileSync('git', ['add', '-u', '--', workflow], { cwd: root })
  assert.equal(run('verify').status, 0, 'Staging an existing deletion must not change the source identity.')

  await writeFile(join(root, workflow), 'name: deleted-workflow\n')
  const restored = run('verify')
  assert.notEqual(restored.status, 0)
  assert.match(restored.stderr, /Sources differ from the sealed candidate/)
  await rm(join(root, workflow))
  assert.equal(run('verify').status, 0)

  const added = join(root, 'new source.txt')
  await writeFile(added, 'New untracked input\n')
  assert.notEqual(run('verify').status, 0, 'New untracked source must invalidate the candidate.')
  await rm(added)
  await rm(join(root, tracked))
  const deleted = run('verify')
  assert.notEqual(deleted.status, 0, 'Deleting a sealed source must invalidate the candidate.')
  assert.match(deleted.stderr, /Sources differ from the sealed candidate/)
  await writeFile(join(root, tracked), 'Tracked source\n')
  assert.equal(run('verify').status, 0)
  assert.deepEqual(await readFile(join(candidate, 'release-manifest.json')), manifest)
})

test('seal does not suppress read errors for source paths still present in Git', async t => {
  const { root, template, entries } = await releaseFixture(t)
  await writeFile(join(root, template), zipSync(entries))
  await symlink('missing-target', join(root, 'broken-source'))
  execFileSync('git', ['add', '--', 'broken-source'], { cwd: root })
  const candidate = join(root, 'artifacts/candidate')
  const result = spawnSync(process.execPath, ['quality/external-consumer/release.mjs', 'seal', candidate], {
    cwd: root, encoding: 'utf8',
  })
  assert.notEqual(result.status, 0)
  assert.match(result.stderr, /ENOENT.*broken-source/)
  await assert.rejects(readdir(candidate), { code: 'ENOENT' })
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

test('seal detects stale Kit contracts before creating a release candidate', async t => {
  const { root } = await releaseFixture(t)
  await cp(join(repository, 'quality/external-consumer/matrix.json'), join(root, 'quality/external-consumer/matrix.json'))
  await cp(join(repository, 'packaging/nuget/baselines.json'), join(root, 'packaging/nuget/baselines.json'))
  await writeFile(join(root, 'quality/external-consumer/environment.mjs'), `
export async function prepareToolchain() {
  return { node: process.execPath, env: process.env }
}
export async function prepareEnvironment() {
  throw new Error('Certification infrastructure must not be started while sealing.')
}
export async function verifyHostEnvironment() {}
`)
  await writeFile(join(root, 'quality/ai-devkit/prepare-contracts.mjs'), `
import assert from 'node:assert/strict'
assert.equal(process.argv[2], '--feed')
assert.ok(process.argv[3].endsWith('/artifacts/nuget-release'))
console.error('Public contract drift: package identity changed.')
process.exit(23)
`)
  const candidate = join(root, 'artifacts/candidate')
  const result = spawnSync(process.execPath, ['quality/external-consumer/release.mjs', 'seal', candidate], {
    cwd: root, encoding: 'utf8',
  })
  assert.notEqual(result.status, 0)
  assert.match(result.stderr, /Public contract drift/)
  await assert.rejects(readdir(candidate), { code: 'ENOENT' })
})

async function certificationOrderFixture(t, kit = true) {
  const { root, template, entries } = await releaseFixture(t, { kit })
  const candidate = join(root, 'artifacts/candidate')
  const calls = join(root, 'artifacts/calls.jsonl')
  await writeFile(join(root, template), zipSync(entries))
  await writeFile(join(root, 'quality/ai-devkit/prepare-contracts.mjs'), '')
  await writeFile(join(root, 'quality/external-consumer/environment.mjs'), `
import { appendFileSync } from 'node:fs'
function record(name) {
  appendFileSync(process.env.NGB_TEST_CALLS, name + '\\n')
  if (process.env.NGB_TEST_FAIL === name) throw new Error('Test failure: ' + name)
}
export async function prepareToolchain() {
  return { node: process.execPath, env: process.env }
}
export async function verifyHostEnvironment() {
  record('host')
}
export async function prepareEnvironment() {
  record('environment')
  return { toolchain: 'test-environment' }
}
`)
  await mkdir(join(root, 'bin'))
  await writeFile(join(root, 'bin/node'), `#!${process.execPath}
const assert = require('node:assert/strict')
const { appendFileSync, mkdirSync, readFileSync, writeFileSync } = require('node:fs')
const { createHash } = require('node:crypto')
const { dirname, join } = require('node:path')
const [script, ...args] = process.argv.slice(2)
const kit = ${kit}
const workspace = 'certification-quality'
const candidate = process.env.NGB_TEST_CANDIDATE
const hash = bytes => createHash('sha256').update(bytes).digest('hex')
const manifestBytes = readFileSync(join(candidate, 'release-manifest.json'))
const manifest = JSON.parse(manifestBytes)
const matrix = JSON.parse(readFileSync('quality/external-consumer/matrix.json'))
const artifactManifestSha256 = hash(manifestBytes)
const checks = ${JSON.stringify(certificationChecks)}
let name
let outputs = []
let report = { status: 'passed' }
if (script === 'quality/upgrade-certification/verify-source.mjs') name = 'source-fixture'
if (script === 'quality/ai-devkit/run-agent-evaluations.mjs') {
  assert.equal(args[1], '--check')
  name = 'ai-kit-agent-access'
}
if (script === 'quality/ai-devkit/certify.mjs') {
  name = 'ai-kit-' + args[0]
  outputs = ['certification/ai-kit/' + args[0] + '.json']
  report = { ...report, schemaVersion: 1, profile: args[0], artifactManifestSha256,
    checks: ['candidate.before', ...checks[args[0]], 'candidate.after'].map(id => ({ id, state: 'passed', exitCode: 0 })),
    files: { 'release-manifest.json': artifactManifestSha256 } }
}
if (script === 'quality/external-consumer/full-quality.mjs') {
  assert.ok(args.includes('--certification'))
  if (kit) assert.ok(args.includes('--preflight'), 'Platform regression must never run during certification.')
  name = args.includes('--preflight') ? 'quality-preflight' : 'certification-quality'
  outputs = ['certification/quality-preflight.json', 'certification/tooling.json',
    'certification/performance.json', 'certification/' + workspace + '-preflight-stages.json']
  if (!args.includes('--preflight')) outputs.push('certification/' + name + '.json',
    'certification/cross-browser.json', 'certification/' + workspace + '-stages.json')
}
const reports = {
  'quality/external-consumer/verify-template.mjs': ['template', 'certification/template.json'],
  'packaging/nuget/verify-api-compatibility.mjs': ['nuget-compatibility', 'nuget-release/api-compatibility.json'],
  'ui/scripts/verify-platform-ui-contracts.mjs': ['npm-compatibility', 'npm/npm-compatibility.json'],
  'quality/external-consumer/verify-compose.mjs': ['compose', 'certification/compose.json'],
  'quality/mcp/verify-contract.mjs': ['mcp-contract', 'certification/mcp-contract/contracts.json'],
  'quality/mcp/verify-external.mjs': ['mcp-external', 'certification/mcp-external/inputs.json'],
  'quality/mcp/verify-verticals.mjs': ['mcp-verticals', 'certification/mcp-verticals/verticals.json'],
}
if (reports[script]) [name, ...outputs] = reports[script]
if (kit) assert.ok(!['mcp-contract', 'mcp-verticals'].includes(name), 'MCP platform tests belong to manual regression.')
if (name === 'compose') report.templateSha256 = manifest.artifacts.find(item => item.path.includes('NGB.Platform.Templates.')).sha256
if (script === 'quality/external-consumer/certify.mjs') {
  name = args[0]
  outputs = ['certification/' + name + '.json']
  report = { ...report, artifactManifestSha256,
    assertions: Object.fromEntries(matrix.profiles[name].assertions.map(id => [id, true])) }
}
if (!name) throw new Error('Unexpected command: ' + script)
appendFileSync(process.env.NGB_TEST_CALLS, name + '\\n')
if (process.env.NGB_TEST_FAIL === name) process.exit(23)
for (const output of outputs) {
  const path = join(candidate, output)
  mkdirSync(dirname(path), { recursive: true })
  writeFileSync(path, JSON.stringify(report))
}
if (name === 'quality-preflight') {
  const reports = Object.fromEntries(['tooling', 'performance'].map(id => [id,
    { sha256: hash(readFileSync(join(candidate, 'certification', id + '.json'))) }]))
  writeFileSync(join(candidate, 'certification/quality-preflight.json'),
    JSON.stringify({ status: 'passed', scope: 'quality-preflight', artifactManifestSha256, reports }))
}
`, { mode: 0o700 })
  const sealed = spawnSync(process.execPath, ['quality/external-consumer/release.mjs', 'seal', candidate], {
    cwd: root, encoding: 'utf8',
  })
  assert.equal(sealed.status, 0, sealed.stderr)
  const run = fail => spawnSync(process.execPath, [
    'quality/external-consumer/release.mjs', 'certify', candidate, '--prepared-toolchain',
  ], {
    cwd: root, encoding: 'utf8',
    env: { ...process.env, PATH: `${join(root, 'bin')}:${process.env.PATH}`,
      NGB_TEST_CALLS: calls, NGB_TEST_CANDIDATE: candidate, NGB_TEST_FAIL: fail },
  })
  const readCalls = async () => (await readFile(calls, 'utf8')).trim().split('\n')
  return { root, candidate, calls, run, readCalls }
}

test('release rejects failed host, fixture and Kit checks before preparing containers or running suites', async t => {
  const preflight = ['host', 'source-fixture', 'ai-kit-artifacts', 'ai-kit-contracts', 'ai-kit-agent-access']
  for (const [index, failed] of preflight.entries()) {
    const { candidate, run, readCalls } = await certificationOrderFixture(t)
    const result = run(failed)
    assert.notEqual(result.status, 0, failed)
    assert.deepEqual(await readCalls(), preflight.slice(0, index + 1))
    assert.match(result.stdout, new RegExp(`\\[failed\\] preflight/${failed}`))
    await assert.rejects(readFile(join(candidate, 'candidate-evidence.json')), { code: 'ENOENT' })
  }
})

test('release runs quality and compatibility checks before integrations, then resumes expensive stages', async t => {
  for (const kit of [true, false]) {
    const { candidate, calls, run, readCalls } = await certificationOrderFixture(t, kit)
    const quick = ['host', 'source-fixture', ...kit ? ['ai-kit-artifacts', 'ai-kit-contracts', 'ai-kit-agent-access'] : []]
    const slow = kit ? 'ai-kit-agents' : 'certification-quality'
    const expected = [...quick, 'environment', 'quality-preflight', 'template', 'nuget-compatibility', 'npm-compatibility',
      'compose', ...kit ? ['mcp-external'] : [],
      'clean-starter', 'generated-extension', 'core-upgrade', 'notes-upgrade', 'attachments-upgrade',
      ...kit ? ['ai-kit-application', 'ai-kit-upgrade'] : [], slow]
    // Stop at the final expensive suite and prove that incomplete evidence cannot pass.
    const first = run(slow)
    assert.notEqual(first.status, 0)
    assert.deepEqual(await readCalls(), expected)
    await assert.rejects(readFile(join(candidate, 'candidate-evidence.json')), { code: 'ENOENT' })

    await writeFile(calls, '')
    const resumed = run(slow)
    assert.notEqual(resumed.status, 0)
    assert.deepEqual(await readCalls(), [...quick, 'environment', slow])
    assert.match(resumed.stdout, /\[resume\] quality-preflight/)
    assert.match(resumed.stdout, /\[resume\] attachments-upgrade/)
  }
})

test('failed Linux quality preflight blocks compatibility, integrations and agent evaluations', async t => {
  const { candidate, run, readCalls } = await certificationOrderFixture(t)
  const result = run('quality-preflight')
  assert.notEqual(result.status, 0)
  assert.deepEqual(await readCalls(), [
    'host', 'source-fixture', 'ai-kit-artifacts', 'ai-kit-contracts', 'ai-kit-agent-access', 'environment', 'quality-preflight',
  ])
  const state = JSON.parse(await readFile(join(candidate, 'run-state.json'), 'utf8'))
  assert.equal(state.stages['quality-preflight'].status, 'failed')
  assert.deepEqual(Object.keys(state.stages), ['quality-preflight'])
})

test('current certification completes and verifies without platform tests or manual coverage evidence', async t => {
  const { root, candidate, run, readCalls } = await certificationOrderFixture(t)
  const certified = run('none')
  assert.equal(certified.status, 0, certified.stderr)
  const verify = () => spawnSync(process.execPath, ['quality/external-consumer/release.mjs', 'verify-candidate', candidate], {
    cwd: root, encoding: 'utf8',
  })
  const verified = verify()
  assert.equal(verified.status, 0, verified.stderr)
  const calls = await readCalls()
  for (const stage of ['mcp-contract', 'mcp-verticals', 'full-quality', 'certification-quality',
    'cross-browser', 'backend', 'frontend']) assert.ok(!calls.includes(stage), stage)
  for (const path of ['coverage', 'cross-browser.json', 'full-quality.json', 'mcp-contract', 'mcp-verticals']) {
    await assert.rejects(readdir(join(candidate, 'certification', path)), { code: 'ENOENT' })
  }
  const evidence = JSON.parse(await readFile(join(candidate, 'candidate-evidence.json'), 'utf8'))
  assert.equal(Object.keys(evidence.results).length, 24)
  for (const gate of ['mcp-external', 'tooling-quality', 'performance-tooling', 'ai-kit-agents']) {
    assert.equal(evidence.results[gate].status, 'passed')
  }

  const path = join(candidate, 'certification/quality-preflight.json')
  const original = await readFile(path, 'utf8')
  for (const mutate of [
    value => { value.artifactManifestSha256 = '0'.repeat(64) },
    value => { value.status = 'failed' },
    value => { value.scope = 'full-platform-quality' },
    value => { value.reports.tooling.sha256 = '0'.repeat(64) },
    value => { value.reports.backend = { sha256: '0'.repeat(64) } },
  ]) {
    const invalid = JSON.parse(original)
    mutate(invalid)
    await writeFile(path, JSON.stringify(invalid))
    assert.notEqual(verify().status, 0, JSON.stringify(invalid))
  }
  await writeFile(path, original)
  assert.equal(verify().status, 0)
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

test('unknown quality modes fail before preparing tools or running tests', () => {
  for (const script of ['quality-suite.mjs', 'full-quality.mjs']) {
    const result = spawnSync(process.execPath, [`quality/external-consumer/${script}`, '--certfication'], {
      cwd: repository, encoding: 'utf8',
    })
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /Usage:/)
    assert.equal(result.stdout, '')
  }
})

async function qualitySuiteFixture(t) {
  const root = await mkdtemp(join(tmpdir(), 'ngb-quality-scope-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  await mkdir(join(root, 'quality/external-consumer'), { recursive: true })
  for (const name of ['quality-suite.mjs', 'checkpoints.mjs', 'execution.mjs', 'release-contracts.mjs', 'contracts.mjs']) {
    await cp(join(repository, 'quality/external-consumer', name), join(root, 'quality/external-consumer', name))
  }
  await symlink(join(repository, 'quality/node_modules'), join(root, 'quality/node_modules'))
  await mkdir(join(root, 'bin'))
  const calls = join(root, 'calls.jsonl')
  const stub = `#!${process.execPath}
const { appendFileSync, mkdirSync, writeFileSync } = require('node:fs')
const { basename, dirname, join } = require('node:path')
const executable = basename(process.argv[1])
const args = process.argv.slice(2)
const testFilter = process.env.VSTestTestCaseFilter
appendFileSync(process.env.NGB_TEST_CALLS, JSON.stringify({ executable, args, testFilter }) + '\\n')
if (args.includes(process.env.NGB_TEST_FAIL)) process.exit(23)
let report
if (executable === 'node') report = 'certification/tooling.json'
if (executable === 'bash') {
  report = args[0].includes('backend')
    ? 'coverage/backend-full/coverage-summary.json'
    : 'coverage/frontend-full/coverage-summary.json'
}
if (report) {
  const path = join('artifacts', report)
  mkdirSync(dirname(path), { recursive: true })
  writeFileSync(path, JSON.stringify({ status: 'passed' }))
}
`
  for (const name of ['node', 'bash', 'npm', 'k6']) {
    const path = join(root, 'bin', name)
    await writeFile(path, stub, { mode: 0o700 })
  }
  await writeFile(join(root, 'identity.json'), JSON.stringify({ candidate: 'test-candidate' }))
  const run = (args = [], fail = '') => spawnSync(process.execPath, ['quality/external-consumer/quality-suite.mjs', ...args], {
    cwd: root, encoding: 'utf8',
    env: { ...process.env, PATH: join(root, 'bin'), NGB_TEST_CALLS: calls, NGB_TEST_FAIL: fail, VSTestTestCaseFilter: undefined },
  })
  const readCalls = async () => (await readFile(calls, 'utf8')).trim().split('\n').map(line => JSON.parse(line))
  return { root, run, readCalls }
}

test('certification omits full coverage commands and evidence and resumes only its own scope', async t => {
  const { root, run, readCalls } = await qualitySuiteFixture(t)
  const result = run(['--certification', 'identity.json'])
  assert.equal(result.status, 0, result.stderr)
  assert.ok(!(await readCalls()).some(call => call.executable === 'bash'))
  await assert.rejects(readdir(join(root, 'artifacts/coverage')), { code: 'ENOENT' })
  const report = JSON.parse(await readFile(join(root, 'artifacts/certification/certification-quality.json'), 'utf8'))
  assert.equal(report.scope, 'external-app-certification')
  assert.deepEqual(Object.keys(report.reports).sort(), ['browsers', 'performance', 'tooling'])
  for (const evidence of Object.values(report.reports)) {
    assert.equal(evidence.sha256, digest(await readFile(join(root, evidence.path))))
  }
  await assert.rejects(readFile(join(root, 'artifacts/certification/full-quality.json')), { code: 'ENOENT' })
  const resumed = run(['--certification', 'identity.json'])
  assert.equal(resumed.status, 0, resumed.stderr)
  for (const stage of ['tooling', 'cross-browser', 'performance']) assert.match(resumed.stdout, new RegExp(`\\[resume\\] ${stage}`))
  const full = run(['identity.json'])
  assert.notEqual(full.status, 0)
  assert.match(full.stderr, /inputs or environment changed/)
})

test('local full quality still runs both complete coverage scripts and includes their reports', async t => {
  const { root, run, readCalls } = await qualitySuiteFixture(t)
  const result = run()
  assert.equal(result.status, 0, result.stderr)
  assert.deepEqual([...result.stdout.matchAll(/\[start\] ([a-z-]+)/g)].map(match => match[1]),
    ['tooling', 'performance', 'cross-browser', 'backend', 'frontend'])
  const coverage = (await readCalls()).filter(call => call.executable === 'bash').map(call => call.args)
  assert.deepEqual(coverage, [['run-backend-full-coverage.sh'], ['run-frontend-full-coverage.sh']])
  assert.ok((await readCalls()).every(call => call.testFilter === undefined))
  const report = JSON.parse(await readFile(join(root, 'artifacts/certification/full-quality.json'), 'utf8'))
  assert.equal(report.scope, 'full-platform-quality')
  assert.equal(report.backendTestFilter, undefined)
  assert.deepEqual(Object.keys(report.reports).sort(), ['backend', 'browsers', 'frontend', 'performance', 'tooling'])
  await assert.rejects(readFile(join(root, 'artifacts/certification/certification-quality.json')), { code: 'ENOENT' })
})

test('release quality applies and reports the live Ollama exclusion only for backend coverage', async t => {
  const { root, run, readCalls } = await qualitySuiteFixture(t)
  await writeFile(join(root, 'identity.json'), JSON.stringify({ artifactManifestSha256: 'candidate', excludeNativeOllama: true }))
  const result = run(['identity.json'])
  assert.equal(result.status, 0, result.stderr)

  const filtered = (await readCalls()).filter(call => call.testFilter)
  assert.deepEqual(filtered, [{
    executable: 'bash', args: ['run-backend-full-coverage.sh'],
    testFilter: 'FullyQualifiedName!~NGB.AiAssistant.AspNetCore.IntegrationTests.AssistantHttpTests.NativeOllama',
  }])
  const report = JSON.parse(await readFile(join(root, 'artifacts/certification/full-quality.json'), 'utf8'))
  assert.equal(report.backendTestFilter, filtered[0].testFilter)
})

test('quality preflight emits no full certificate and full execution reuses its verified checkpoints', async t => {
  for (const mode of [[], ['--certification']]) {
    const { root, run, readCalls } = await qualitySuiteFixture(t)
    const first = run([...mode, '--preflight', 'identity.json'])
    assert.equal(first.status, 0, first.stderr)
    assert.deepEqual([...first.stdout.matchAll(/\[start\] ([a-z-]+)/g)].map(match => match[1]), ['tooling', 'performance'])
    assert.ok(!(await readCalls()).some(call => call.executable === 'bash'
      || call.args.includes('test:browser:framework:cross-browser')))
    const report = JSON.parse(await readFile(join(root, 'artifacts/certification/quality-preflight.json'), 'utf8'))
    assert.equal(report.scope, 'quality-preflight')
    assert.deepEqual(Object.keys(report.reports).sort(), ['performance', 'tooling'])
    for (const name of ['full-quality.json', 'certification-quality.json', 'cross-browser.json']) {
      await assert.rejects(readFile(join(root, 'artifacts/certification', name)), { code: 'ENOENT' })
    }

    const full = run([...mode, 'identity.json'])
    assert.equal(full.status, 0, full.stderr)
    for (const name of ['tooling', 'performance']) assert.match(full.stdout, new RegExp(`\\[resume\\] ${name}`))
    assert.match(full.stdout, /\[start\] cross-browser/)
    assert.equal((await readCalls()).filter(call => call.args.includes('quality/external-consumer/test-tooling.mjs')).length, 1)
    const resumed = run([...mode, 'identity.json'])
    assert.equal(resumed.status, 0, resumed.stderr)
    assert.ok(!resumed.stdout.includes('[start]'))
  }
})

test('changed preflight evidence reruns checks before reusing later coverage results', async t => {
  const { root, run } = await qualitySuiteFixture(t)
  assert.equal(run(['identity.json']).status, 0)
  await writeFile(join(root, 'artifacts/certification/tooling.json'), 'changed')
  const result = run(['identity.json'])
  assert.equal(result.status, 0, result.stderr)
  assert.deepEqual([...result.stdout.matchAll(/\[start\] ([a-z-]+)/g)].map(match => match[1]),
    ['tooling', 'performance', 'cross-browser', 'backend', 'frontend'])
})

test('certification and local quality fail on the first failed required command without a success report', async t => {
  for (const [args, commands] of [
    [['--certification'], ['quality/external-consumer/test-tooling.mjs', 'test:browser:framework:cross-browser', 'test:metric-grouping']],
    [[], ['quality/external-consumer/test-tooling.mjs', 'typecheck', 'test:metric-grouping',
      'test:browser:framework:cross-browser', 'run-backend-full-coverage.sh', 'run-frontend-full-coverage.sh']],
    [['--preflight'], ['quality/external-consumer/test-tooling.mjs', 'typecheck', 'test:metric-grouping']],
  ]) {
    for (const failed of commands) {
      const { root, run, readCalls } = await qualitySuiteFixture(t)
      const result = run(args, failed)
      assert.notEqual(result.status, 0)
      assert.match(result.stderr, /failed \(23\)/)
      assert.ok((await readCalls()).at(-1).args.includes(failed))
      const reports = await readdir(join(root, 'artifacts/certification')).catch(error => {
        if (error.code !== 'ENOENT') throw error
        return []
      })
      assert.ok(!reports.includes('full-quality.json'))
      assert.ok(!reports.includes('certification-quality.json'))
      assert.ok(!reports.includes('quality-preflight.json'))
    }
  }
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
