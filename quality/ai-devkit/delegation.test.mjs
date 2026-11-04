import assert from 'node:assert/strict'
import { mkdtemp, readFile, readdir, realpath, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import { argumentsFor } from '../../packaging/templates/content/infrastructure/ngb/contracts.mjs'
import { delegateUpgrade, needsDelegation, validateDelegation } from '../../packaging/templates/content/infrastructure/ngb/delegation.mjs'

const integrity = `sha512-${'a'.repeat(86)}==`

test('target delegation preserves historical transitions and never resolves an implicit latest version', () => {
  assert.equal(needsDelegation(argumentsFor(['upgrade', '--to', '3.3.0'])), false)
  assert.equal(needsDelegation(argumentsFor(['upgrade', '--to', '3.4.0'])), false)
  assert.equal(needsDelegation(argumentsFor(['upgrade', '--to', '3.5.0'])), true)
  assert.equal(needsDelegation(argumentsFor(['upgrade', '--to', '4.0.0'])), true)
  assert.equal(needsDelegation(argumentsFor(['ai', 'sync'])), false)
  for (const version of ['latest', '03.4.0', '3.04.0', '3.4.00', '1234567890.0.0']) {
    assert.throws(() => argumentsFor(['upgrade', '--to', version]), /exact stable/)
  }
})

test('delegation identity rejects shadowed, stale, recursive and malformed invocations', async t => {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'ngb-delegation-identity-')))
  t.after(() => rm(root, { recursive: true, force: true }))
  const entry = join(root, 'ngb.mjs')
  await writeFile(entry, '')
  const value = { schemaVersion: 1, version: '3.4.0', entry, integrity }
  const options = argumentsFor(['upgrade', '--to', '3.4.0'])
  const environment = marker => ({ NGB_CLI_DELEGATION: JSON.stringify(marker) })
  await validateDelegation(options, {}, entry)
  await validateDelegation(options, environment(value), entry)
  for (const change of [
    { schemaVersion: 2 }, { version: '3.5.0' }, { entry: join(root, 'other.mjs') }, { integrity: 'unverified' },
  ]) await assert.rejects(validateDelegation(options, environment({ ...value, ...change }), entry))
  await assert.rejects(validateDelegation({ ...options, to: '3.5.0' }, environment(value), entry), /Recursive/)
  await assert.rejects(validateDelegation({ command: 'start' }, environment(value), entry), /coordinated upgrade/)
  await assert.rejects(validateDelegation(options, { NGB_CLI_DELEGATION: 'x'.repeat(16385) }, entry), /marker/)
  await assert.rejects(validateDelegation(options, { NGB_CLI_DELEGATION: '{' }, entry))
})

test('target CLI runs as a separate process with literal arguments, isolated credentials and the exact exit code', async t => {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'ngb-delegation-process-')))
  t.after(() => rm(root, { recursive: true, force: true }))
  const entry = join(root, 'target cli.mjs')
  await writeFile(entry, `
import { writeFileSync } from 'node:fs'
writeFileSync(process.env.HOME + '/process-receipt.json', 'temporary')
console.log(JSON.stringify({ args: process.argv.slice(2), cwd: process.cwd(),
  marker: JSON.parse(process.env.NGB_CLI_DELEGATION), home: process.env.HOME,
  secret: process.env.DEPLOYMENT_SECRET, nodeOptions: process.env.NODE_OPTIONS }))
process.exitCode = 4
`)
  const args = ['upgrade', '--app', root, '--to', '3.5.0', '--apply', '--resolutions', 'review;$(literal) file.json']
  const options = argumentsFor(args)
  let request
  const result = await delegateUpgrade(args, options, {}, {
    environment: { ...process.env, NODE_OPTIONS: '--invalid-option', DEPLOYMENT_SECRET: 'not-forwarded' },
    acquire: async (version, settings) => {
      request = { version, settings }
      return { entry, version, integrity, source: { kind: 'test' } }
    },
  })
  assert.equal(result.exitCode, 4)
  assert.equal(result.state, 'failed')
  const output = JSON.parse(result.output)
  assert.deepEqual(output.args, args)
  assert.equal(output.cwd, process.cwd())
  assert.equal(output.marker.entry, entry)
  assert.equal(output.marker.integrity, integrity)
  assert.equal(output.secret, undefined)
  assert.equal(output.nodeOptions, undefined)
  assert.equal(request.settings.applicationRoot, root)
  assert.equal(request.version, '3.5.0')
  await assert.rejects(readFile(join(output.home, 'process-receipt.json')), { code: 'ENOENT' })
  assert.deepEqual(await readdir(root), ['target cli.mjs'])
  await assert.rejects(delegateUpgrade(args, options, {}, {
    environment: { NGB_CLI_DELEGATION: '{}' },
  }), /Recursive/)
})

test('delegated subprocess cancellation terminates the process and removes its temporary environment', async t => {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'ngb-delegation-cancel-')))
  t.after(() => rm(root, { recursive: true, force: true }))
  const entry = join(root, 'waiting.mjs')
  await writeFile(entry, 'setInterval(() => {}, 1000)')
  const args = ['upgrade', '--app', root, '--to', '3.5.0']
  const cancellation = new AbortController()
  const pending = delegateUpgrade(args, argumentsFor(args), {}, {
    signal: cancellation.signal,
    acquire: async version => ({ entry, version, integrity }),
  })
  const timer = setTimeout(() => cancellation.abort(), 100)
  t.after(() => clearTimeout(timer))
  const result = await pending
  assert.equal(result.reason, 'cancelled')
  assert.equal(result.state, 'failed')
})
