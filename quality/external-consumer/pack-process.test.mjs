import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { cp, mkdir, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { basename, dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')

async function fixture(t) {
  const root = await mkdtemp(join(tmpdir(), 'ngb-pack-process-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  for (const path of ['packaging/nuget', 'quality/external-consumer', 'bin', 'artifacts/nuget']) {
    await mkdir(join(root, path), { recursive: true })
  }
  for (const file of ['pack-platform.sh', 'pack-platform.mjs']) {
    await cp(join(repository, 'packaging/nuget', file), join(root, 'packaging/nuget', file))
  }
  await cp(join(repository, 'quality/external-consumer/execution.mjs'), join(root, 'quality/external-consumer/execution.mjs'))
  await writeFile(join(root, 'quality/external-consumer/environment.mjs'), `
export async function prepareToolchain() {
  return { node: process.execPath, env: { ...process.env, NGB_TEST_PREPARED: 'yes' } }
}
`)
  await writeFile(join(root, 'packaging/nuget/projects.txt'), 'NGB.Tools/NGB.Tools.csproj\npackaging/templates/NGB.Platform.Templates.csproj\n')
  await writeFile(join(root, 'artifacts/nuget/previous.nupkg'), 'previous feed')
  const calls = join(root, 'calls.jsonl')
  const stub = `#!${process.execPath}
const assert = require('node:assert/strict')
const { appendFileSync, mkdirSync, writeFileSync } = require('node:fs')
const { basename, join } = require('node:path')
const name = basename(process.argv[1])
const args = process.argv.slice(2)
assert.equal(process.env.NGB_TEST_PREPARED, 'yes')
appendFileSync(process.env.NGB_TEST_CALLS, JSON.stringify({ name, args }) + '\\n')
if (name === 'git') console.log('1794390000')
if (name === 'node' && args[0] === '-p') console.log('1')
if (args.some(arg => arg.endsWith('prepare-contracts.mjs')) && process.env.NGB_TEST_FAIL) process.exit(23)
if (name === 'dotnet' && args[0] === 'msbuild') {
  const template = args[1].endsWith('NGB.Platform.Templates.csproj')
  if (args.includes('-getProperty:PackageVersion')) console.log('3.4.0')
  if (args.includes('-getProperty:NgbPlatformPackageKind')) console.log(template ? 'template' : 'runtime')
  if (args.includes('-getProperty:PackageId')) console.log(template ? 'NGB.Platform.Templates' : 'NGB.Platform.Tools')
}
if (name === 'dotnet' && args[0] === 'pack') {
  const template = args[1].endsWith('NGB.Platform.Templates.csproj')
  const output = args[args.indexOf('--output') + 1]
  mkdirSync(output, { recursive: true })
  const id = template ? 'NGB.Platform.Templates' : 'NGB.Platform.Tools'
  writeFileSync(join(output, id + '.3.4.0.nupkg'), 'new-package')
  if (!template) writeFileSync(join(output, id + '.3.4.0.snupkg'), 'new-symbols')
}
`
  for (const name of ['node', 'dotnet', 'git']) await writeFile(join(root, 'bin', name), stub, { mode: 0o700 })
  const run = fail => spawnSync('/bin/bash', ['packaging/nuget/pack-platform.sh'], {
    cwd: root, encoding: 'utf8',
    env: { ...process.env, PATH: `${join(root, 'bin')}:${process.env.PATH}`, NGB_TEST_CALLS: calls,
      NGB_TEST_FAIL: fail ? 'yes' : '' },
  })
  // The entrypoint must first delegate to the real Node wrapper, before stubs require prepared tools.
  const launcher = join(root, 'bin/node')
  await writeFile(launcher, `#!/bin/bash
if [[ "$1" == */pack-platform.mjs ]]; then
  exec '${process.execPath}' "$@"
fi
exec '${process.execPath}' '${join(root, 'bin/node-stub')}' "$@"
`, { mode: 0o700 })
  await writeFile(join(root, 'bin/node-stub'), stub.replace("const name = basename(process.argv[1])", "const name = 'node'"), { mode: 0o700 })
  const readCalls = async () => (await readFile(calls, 'utf8')).trim().split('\n').map(JSON.parse)
  return { root, run, readCalls }
}

test('pack prepares tools and binds references before template locks, template and CLI packaging', async t => {
  const { root, run, readCalls } = await fixture(t)
  const result = run(false)
  assert.equal(result.status, 0, result.stderr)
  const calls = await readCalls()
  const ordered = calls.filter(call => call.args[0] === 'pack' || call.name === 'node' && call.args[0] !== '-p')
  assert.deepEqual(ordered.map(call => call.args[0] === 'pack' ? basename(call.args[1]) : basename(call.args[0])), [
    'NGB.Tools.csproj', 'prepare-contracts.mjs', 'prepare.mjs', 'prepare-locks.mjs', 'NGB.Platform.Templates.csproj', 'pack.mjs',
  ])
  for (const call of calls.filter(call => call.args[0] === 'pack')) {
    assert.ok(call.args.includes('-p:DeterministicTimestamp=1794390000'))
  }
  assert.ok(calls.find(call => call.args[0].endsWith('prepare-contracts.mjs')).args.includes('--refresh-identities'))
  assert.deepEqual((await readdir(join(root, 'artifacts/nuget'))).sort(), [
    'NGB.Platform.Templates.3.4.0.nupkg', 'NGB.Platform.Tools.3.4.0.nupkg', 'NGB.Platform.Tools.3.4.0.snupkg',
  ])
  assert.equal(calls.at(-1).args[0], 'restore')
})

test('a contract mismatch stops before publishing the template, CLI or replacement local feed', async t => {
  const { root, run, readCalls } = await fixture(t)
  const result = run(true)
  assert.notEqual(result.status, 0)
  const calls = await readCalls()
  assert.equal(calls.filter(call => call.args[0] === 'pack').length, 1)
  assert.ok(calls.at(-1).args[0].endsWith('prepare-contracts.mjs'))
  assert.deepEqual(await readdir(join(root, 'artifacts/nuget')), ['previous.nupkg'])
})
