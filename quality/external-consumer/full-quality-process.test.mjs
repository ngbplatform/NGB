import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { cp, mkdir, mkdtemp, readFile, rm, symlink, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')

async function fixture(t) {
  const root = await mkdtemp(join(tmpdir(), 'ngb-full-quality-process-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  const candidate = join(root, 'candidate')
  for (const path of ['quality/external-consumer', 'bin', 'candidate/certification', 'candidate/nuget-release', 'candidate/npm']) {
    await mkdir(join(root, path), { recursive: true })
  }
  for (const name of ['full-quality.mjs', 'checkpoints.mjs', 'execution.mjs', 'release-contracts.mjs', 'contracts.mjs', 'source-files.mjs']) {
    await cp(join(repository, 'quality/external-consumer', name), join(root, 'quality/external-consumer', name))
  }
  await symlink(join(repository, 'quality/node_modules'), join(root, 'quality/node_modules'))
  await writeFile(join(root, 'quality/external-consumer/environment.mjs'), `
export async function prepareToolchain() {
  return { node: process.execPath, env: process.env }
}
export async function prepareEnvironment() {
  return { dockerEndpoint: 'unix:///test/docker.sock', dockerSocketGroup: '123', images: { quality: 'test-image' } }
}
`)
  await writeFile(join(root, 'README.md'), 'Source fixture\n')
  await writeFile(join(candidate, 'release-manifest.json'), JSON.stringify({ derivedInputs: {} }))
  const calls = join(root, 'calls.jsonl')
  const stub = `#!${process.execPath}
const assert = require('node:assert/strict')
const { appendFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } = require('node:fs')
const { basename, dirname, join } = require('node:path')
const executable = basename(process.argv[1])
const args = process.argv.slice(2)
appendFileSync(process.env.NGB_TEST_CALLS, JSON.stringify({ executable, args }) + '\\n')
if (executable === 'git') {
  const deleted = '.github/workflows/external-app-certification.yml\\0'
  process.stdout.write(args.includes('--deleted') ? deleted : 'README.md\\0' + deleted)
}
if (executable === 'node') {
  assert.equal(args[0], 'quality/external-consumer/release.mjs')
  assert.equal(args[1], 'verify')
}
if (executable === 'docker' && args[0] === 'run') {
  const workspace = args.find(arg => arg.endsWith(':/workspace')).slice(0, -':/workspace'.length)
  const command = args.at(-1)
  const preflight = command.includes('--preflight')
  const certification = command.includes('--certification')
  const identity = JSON.parse(readFileSync(join(workspace, 'quality-identity.json'), 'utf8'))
  assert.equal(readFileSync(join(workspace, 'README.md'), 'utf8'), 'Source fixture\\n')
  assert.equal(existsSync(join(workspace, '.github/workflows/external-app-certification.yml')), false)
  assert.equal(identity.certification, certification)
  assert.equal(identity.excludeNativeOllama, !certification)
  assert.ok(command.includes('node quality/external-consumer/quality-suite.mjs '))
  assert.ok(command.endsWith('/workspace/quality-identity.json'))
  if (process.env.NGB_TEST_FAIL) process.exit(23)
  const report = preflight ? 'quality-preflight.json' : certification ? 'certification-quality.json' : 'full-quality.json'
  const outputs = ['certification/' + report, 'certification/tooling.json', 'certification/performance.json', 'run-state.json']
  if (!preflight) outputs.push('certification/cross-browser.json')
  if (!preflight && !certification) {
    outputs.push('coverage/backend-full/coverage-summary.json', 'coverage/frontend-full/coverage-summary.json')
  }
  for (const output of outputs) {
    const path = join(workspace, 'artifacts', output)
    mkdirSync(dirname(path), { recursive: true })
    writeFileSync(path, JSON.stringify({ status: 'passed', artifactManifestSha256: identity.artifactManifestSha256 }))
  }
}
`
  for (const name of ['node', 'git', 'docker']) await writeFile(join(root, 'bin', name), stub, { mode: 0o700 })
  const run = (args, fail = false) => spawnSync(process.execPath, [
    'quality/external-consumer/full-quality.mjs', candidate, ...args,
  ], {
    cwd: root, encoding: 'utf8',
    env: { ...process.env, PATH: `${join(root, 'bin')}:${process.env.PATH}`, TMPDIR: root,
      NGB_TEST_CALLS: calls, NGB_TEST_FAIL: fail ? 'yes' : '' },
  })
  const readCalls = async () => (await readFile(calls, 'utf8')).trim().split('\n').map(JSON.parse)
  return { candidate, run, readCalls }
}

test('Linux wrapper forwards preflight through tool preparation and retains the same workspace for full execution', async t => {
  for (const mode of [[], ['--certification']]) {
    const { candidate, run, readCalls } = await fixture(t)
    const first = run([...mode, '--preflight'])
    assert.equal(first.status, 0, first.stderr)
    const workspaceName = mode.length ? 'certification-quality' : 'quality'
    const location = join(candidate, `${workspaceName}-workspace.json`)
    const before = await readFile(location, 'utf8')
    for (const name of ['quality-preflight.json', 'tooling.json', 'performance.json', `${workspaceName}-preflight-stages.json`]) {
      assert.equal(JSON.parse(await readFile(join(candidate, 'certification', name), 'utf8')).status, 'passed')
    }
    for (const name of ['full-quality.json', 'certification-quality.json', 'cross-browser.json', 'coverage/backend-full/coverage-summary.json']) {
      await assert.rejects(readFile(join(candidate, 'certification', name)), { code: 'ENOENT' })
    }

    const full = run(mode)
    assert.equal(full.status, 0, full.stderr)
    assert.equal(await readFile(location, 'utf8'), before)
    const report = mode.length ? 'certification-quality.json' : 'full-quality.json'
    assert.equal(JSON.parse(await readFile(join(candidate, 'certification', report), 'utf8')).status, 'passed')
    await readFile(join(candidate, 'certification', `${workspaceName}-stages.json`))
    const runs = (await readCalls()).filter(call => call.executable === 'docker' && call.args[0] === 'run')
    assert.equal(runs.length, 2)
    assert.ok(runs[0].args.at(-1).includes('--preflight'))
    assert.ok(!runs[1].args.at(-1).includes('--preflight'))
  }
})

test('failed Linux preflight cannot publish a successful preflight or full report', async t => {
  const { candidate, run } = await fixture(t)
  const result = run(['--preflight'], true)
  assert.notEqual(result.status, 0)
  assert.match(result.stderr, /failed \(23\)/)
  for (const name of ['quality-preflight.json', 'full-quality.json', 'quality-preflight-stages.json']) {
    await assert.rejects(readFile(join(candidate, 'certification', name)), { code: 'ENOENT' })
  }
})
