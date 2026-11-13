import assert from 'node:assert/strict'
import { execFileSync, spawnSync } from 'node:child_process'
import { cp, mkdir, mkdtemp, readFile, readdir, rm, symlink, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'
import { zipSync } from 'fflate'
import { packagePaths } from './build-release-contracts.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')

async function fixture(t) {
  const root = await mkdtemp(join(tmpdir(), 'ngb-build-release-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  for (const path of ['quality/external-consumer', 'packaging/nuget', 'artifacts/npm', 'artifacts/nuget-release']) {
    await mkdir(join(root, path), { recursive: true })
  }
  await symlink(join(repository, 'quality/node_modules'), join(root, 'quality/node_modules'))
  for (const file of ['build-release.mjs', 'build-release-contracts.mjs', 'release-contracts.mjs', 'contracts.mjs',
    'publish.mjs', 'verify-registry.mjs', 'registry-fetch.mjs']) {
    await cp(join(repository, 'quality/external-consumer', file), join(root, 'quality/external-consumer', file))
  }
  const inventory = {
    target: '3.4.0', packages: [{ id: 'NGB.Platform.Tools', kind: 'runtime' }, { id: 'NGB.Platform.Templates', kind: 'template' }],
  }
  await writeFile(join(root, 'packaging/nuget/baselines.json'), JSON.stringify(inventory))
  for (const path of packagePaths(inventory)) {
    const bytes = path.endsWith('.tgz') ? Buffer.from(path) : zipSync({ 'package.nuspec': Buffer.from(path) })
    await writeFile(join(root, 'artifacts', path), bytes)
  }
  const git = args => execFileSync('git', ['-c', 'core.hooksPath=/dev/null', ...args], { cwd: root, encoding: 'utf8' }).trim()
  git(['init', '-q'])
  git(['-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '--allow-empty', '-qm', 'Fixture'])
  const env = {
    ...process.env, GITHUB_ACTIONS: 'true', GITHUB_REF: 'refs/heads/main', GITHUB_EVENT_NAME: 'push',
    GITHUB_REPOSITORY: 'owner/NGB', GITHUB_SHA: git(['rev-parse', 'HEAD']), GITHUB_RUN_ID: '42', GITHUB_RUN_ATTEMPT: '2',
    GITHUB_WORKFLOW_REF: 'owner/NGB/.github/workflows/platform-packages.yml@refs/heads/main',
    NGB_BUILD_RUN_ID: '42', NGB_BUILD_RUN_ATTEMPT: '2', NGB_RELEASE_COMMIT: git(['rev-parse', 'HEAD']),
  }
  delete env.NODE_OPTIONS
  const run = (file, args = [], extra = {}) => spawnSync(process.execPath, [`quality/external-consumer/${file}`, ...args], {
    cwd: root, encoding: 'utf8', env: { ...env, ...extra }, timeout: 30_000,
  })
  const json = async path => JSON.parse(await readFile(join(root, 'artifacts/release-candidate', path)))
  return { root, env, run, json }
}

test('build bundle seals only runner packages and detects changes before publication', async t => {
  const { root, run, json } = await fixture(t)
  let result = run('build-release.mjs', ['seal'])
  assert.equal(result.status, 0, result.stderr)
  assert.deepEqual((await readdir(join(root, 'artifacts/release-candidate'))).sort(), ['npm', 'nuget-release', 'release-manifest.json'])
  assert.equal((await json('release-manifest.json')).artifacts.length, 5)
  result = run('build-release.mjs', ['verify'])
  assert.equal(result.status, 0, result.stderr)
  assert.notEqual(run('build-release.mjs', ['seal']).status, 0, 'Sealing cannot overwrite a release bundle.')
  assert.notEqual(run('build-release.mjs', ['verify'], { NGB_BUILD_RUN_ATTEMPT: '1' }).status, 0)
  assert.notEqual(run('build-release.mjs', ['verify'], { NGB_RELEASE_COMMIT: 'b'.repeat(40) }).status, 0)
  assert.notEqual(run('build-release.mjs', ['verify-promotion']).status, 0, 'Publication evidence is required.')
  await writeFile(join(root, 'artifacts/release-candidate/npm/ngbplatform-cli-3.4.0.tgz'), 'changed')
  assert.notEqual(run('build-release.mjs', ['verify']).status, 0)
})

test('PR and local inputs cannot produce a GitHub release bundle', async t => {
  const { run } = await fixture(t)
  for (const env of [{ GITHUB_EVENT_NAME: 'pull_request' }, { GITHUB_ACTIONS: 'false' }, { GITHUB_SHA: 'b'.repeat(40) }]) {
    assert.notEqual(run('build-release.mjs', ['seal'], env).status, 0)
  }
  assert.notEqual(run('build-release.mjs', ['unknown']).status, 0)
})

async function registryFixture(t) {
  const value = await fixture(t)
  const result = value.run('build-release.mjs', ['seal'])
  assert.equal(result.status, 0, result.stderr)
  const callsPath = join(value.root, 'calls.jsonl')
  await writeFile(callsPath, '')
  const bootstrap = join(value.root, 'registry-stub.mjs')
  await writeFile(bootstrap, `
import childProcess from 'node:child_process'
import { syncBuiltinESMExports } from 'node:module'
import { appendFileSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
const call = (name, args) => appendFileSync(process.env.NGB_TEST_CALLS, JSON.stringify({ name, args }) + '\\n')
const original = childProcess.execFileSync
childProcess.execFileSync = (name, args, options) => {
  if (name === 'dotnet' || name === 'npm') {
    call(name, args)
    return Buffer.alloc(0)
  }
  return original(name, args, options)
}
childProcess.spawnSync = (name, args) => {
  call(name, args.map(value => value === process.env.NUGET_API_KEY ? '<redacted>' : value))
  return { status: process.env.NGB_TEST_PUSH_FAIL ? 1 : 0 }
}
syncBuiltinESMExports()
globalThis.fetch = async url => {
  call('fetch', [url])
  const root = join(process.cwd(), 'artifacts/release-candidate')
  const manifest = JSON.parse(readFileSync(join(root, 'release-manifest.json')))
  const npm = url.includes('npmjs.org')
  const name = url.includes('cli') ? 'cli' : 'ui'
  const artifact = manifest.artifacts.find(item => npm
    ? item.path === 'npm/ngbplatform-' + name + '-' + manifest.version + '.tgz'
    : url.endsWith(item.path.split('/').at(-1).toLowerCase()))
  if (!artifact) throw new Error('Unexpected fixture request: ' + url)
  if (npm && !url.endsWith('.tgz')) return new Response(JSON.stringify({
    name: '@ngbplatform/' + name, version: manifest.version,
    dist: { integrity: artifact.integrity,
      tarball: 'https://registry.npmjs.org/@ngbplatform/' + name + '/-/' + name + '-' + manifest.version + '.tgz' },
  }))
  return new Response(process.env.NGB_TEST_CORRUPT ? Buffer.from('corrupt') : readFileSync(join(root, artifact.path)))
}
`)
  // Sentinel programs record the local mode's certification calls. CI mode must
  // never invoke either program, even when registry verification succeeds.
  for (const file of ['release.mjs', 'certify.mjs']) {
    await writeFile(join(value.root, 'quality/external-consumer', file), `
import { appendFileSync, mkdirSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
appendFileSync(process.env.NGB_TEST_CALLS, JSON.stringify({ name: '${file}', args: process.argv.slice(2) }) + '\\n')
if ('${file}' === 'certify.mjs') {
  mkdirSync(join(process.argv[3], 'certification'), { recursive: true })
  writeFileSync(join(process.argv[3], 'certification/registry-clean-starter.json'), JSON.stringify({ status: 'passed' }))
}
`)
  }
  const env = { NODE_OPTIONS: `--import=${bootstrap}`, NGB_TEST_CALLS: callsPath, NUGET_API_KEY: 'fixture-secret' }
  return { ...value, registryEnv: env, calls: async () => (await readFile(callsPath, 'utf8')).trim().split('\n').filter(Boolean).map(JSON.parse) }
}

test('CI publishes and verifies archives without certification, then authorizes image promotion', async t => {
  const { run, json, calls, registryEnv } = await registryFixture(t)
  for (const file of ['publish.mjs', 'verify-registry.mjs']) {
    const result = run(file, ['--ci-build'], registryEnv)
    assert.equal(result.status, 0, result.stderr)
    assert.doesNotMatch(result.stdout + result.stderr, /fixture-secret/)
  }
  for (const command of ['verify-publication', 'verify-promotion']) {
    const result = run('build-release.mjs', [command])
    assert.equal(result.status, 0, result.stderr)
  }
  const recorded = await calls()
  assert.equal(recorded.filter(item => item.name === 'dotnet' && item.args[1] === 'push').length, 2)
  assert.equal(recorded.filter(item => item.name === 'dotnet' && item.args[1] === 'verify').length, 2)
  assert.ok(recorded.every(item => !['release.mjs', 'certify.mjs'].includes(item.name)))
  const identities = await json('certification/registry-identities.json')
  assert.equal(identities.scope, 'github-package-build')
  assert.equal(identities.smokeSha256, undefined)
})

test('local registry certification keeps its runtime check and cannot authorize CI promotion', async t => {
  const { run, calls, registryEnv } = await registryFixture(t)
  for (const file of ['publish.mjs', 'verify-registry.mjs']) {
    const result = run(file, [], registryEnv)
    assert.equal(result.status, 0, result.stderr)
  }
  assert.equal((await calls()).filter(item => item.name === 'release.mjs').length, 2)
  assert.equal((await calls()).filter(item => item.name === 'certify.mjs').length, 1)
  assert.notEqual(run('build-release.mjs', ['verify-promotion']).status, 0)
})

test('upload failure and corrupt registry contents cannot produce promotion evidence', async t => {
  const { run, json, registryEnv } = await registryFixture(t)
  const upload = run('publish.mjs', ['--ci-build'], { ...registryEnv, NGB_TEST_PUSH_FAIL: 'true' })
  assert.notEqual(upload.status, 0)
  assert.doesNotMatch(upload.stderr, /fixture-secret/)
  await assert.rejects(json('upload-receipt.json'), { code: 'ENOENT' })
  const registry = run('verify-registry.mjs', ['--ci-build'], { ...registryEnv, NGB_TEST_CORRUPT: 'true' })
  assert.notEqual(registry.status, 0)
  await assert.rejects(json('promotion-evidence.json'), { code: 'ENOENT' })
})

test('GitHub workflow chain builds and publishes without test, coverage or agent execution', async () => {
  const directory = join(repository, '.github/workflows')
  const files = await readdir(directory)
  assert.ok(!files.includes('external-app-certification.yml'))
  assert.ok(!files.includes('performance-tests.yml'))
  for (const file of files) {
    const text = await readFile(join(directory, file), 'utf8')
    assert.doesNotMatch(text, /(?:dotnet test|vitest|playwright (?:test|install)|test:all|test:tooling|test:api-compat|run-\w+-full-coverage|release\.mjs certify|run-agent-evaluations\.mjs|release\.mjs verify-candidate)/, file)
  }
  const build = await readFile(join(directory, 'platform-packages.yml'), 'utf8')
  const publish = await readFile(join(directory, 'publish-platform-release.yml'), 'utf8')
  assert.match(build, /needs: npm/)
  assert.match(build, /build-release\.mjs seal/)
  assert.match(build, /ngb-platform-release-attempt-\$\{\{ github.run_attempt \}\}/)
  assert.match(publish, /workflows: \[platform-packages\]/)
  assert.match(publish, /NGB_BUILD_RUN_ATTEMPT: \$\{\{ needs.select.outputs.run-attempt \}\}/)
  assert.match(publish, /publish\.mjs --ci-build/)
  assert.match(publish, /verify-registry\.mjs --ci-build/)
  assert.doesNotMatch(publish, /release\.mjs verify-candidate/)
})
