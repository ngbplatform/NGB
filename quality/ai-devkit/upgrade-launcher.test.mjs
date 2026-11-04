import assert from 'node:assert/strict'
import { execFile } from 'node:child_process'
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import { promisify } from 'node:util'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { upgradeLauncher } from './upgrade-launcher.mjs'

const exec = promisify(execFile)
const configuration = {
  node: process.execPath,
  sandboxCli: '/fixed/sandbox.mjs',
  settings: '/fixed/settings.json',
  cli: '/fixed/ngb.mjs',
  application: '/fixed/application',
  candidate: '/fixed/candidate',
  resolution: '/scratch/resolutions.json',
  environment: { PATH: '/usr/bin:/bin', HOME: '/scratch/home', TMPDIR: '/scratch/tmp' },
}

test('fixed upgrade launcher quotes paths and removes inherited execution settings before Node starts', async () => {
  const root = await mkdtemp(join(tmpdir(), 'ngb-launcher-'))
  try {
    const sandboxCli = join(root, "sandbox's probe.mjs")
    const launch = join(root, 'upgrade')
    const marker = join(root, 'injected')
    const injection = join(root, 'injection.cjs')
    await writeFile(injection, `require('node:fs').writeFileSync(${JSON.stringify(marker)}, 'unexpected')`)
    await writeFile(sandboxCli, 'console.log(JSON.stringify({ args: process.argv.slice(2), env: process.env }))\n')
    const selected = { ...configuration, sandboxCli, application: "/fixed/app's $(touch injected); name",
      environment: { ...configuration.environment, LANG: "en_US.UTF-8' $(touch injected)" } }
    await writeFile(launch, upgradeLauncher(selected), { mode: 0o500 })
    const result = await exec(launch, [], { cwd: root, env: {
      PATH: '/untrusted', HOME: '/private/untrusted', NODE_OPTIONS: `--require=${injection}`,
      BASH_ENV: injection, npm_config_proxy: 'http://untrusted.invalid', ANTHROPIC_API_KEY: 'synthetic-secret',
    } })
    const probe = JSON.parse(result.stdout)
    assert.deepEqual(probe.args, ['--settings', selected.settings, '--', selected.node, selected.cli,
      'upgrade', '--app', selected.application, '--to', '3.4.0', '--packages', selected.candidate,
      '--resolutions', selected.resolution, '--apply', '--format', 'json'])
    // CoreFoundation adds this locale hint when Node starts on macOS.
    delete probe.env.__CF_USER_TEXT_ENCODING
    assert.deepEqual(probe.env, selected.environment)
    await assert.rejects(readFile(marker), { code: 'ENOENT' })
    await assert.rejects(exec(launch, ['--settings', '/untrusted.json']), error => error.code === 64)
  } finally {
    await rm(root, { recursive: true, force: true })
  }
})

test('fixed upgrade launcher rejects mutable command inputs and inherited secret/proxy settings', async () => {
  const profile = JSON.parse(await readFile(new URL('./claude-upgrade-profile.json', import.meta.url)))
  assert.equal(profile.installer.sourceSha256, digest(await readFile(new URL('./upgrade-launcher.mjs', import.meta.url))))
  assert.equal(profile.settings.sandbox.enabled, true)
  assert.equal(profile.settings.sandbox.allowUnsandboxedCommands, false)
  assert.deepEqual(profile.settings.sandbox.excludedCommands, [])
  for (const value of [null, 'relative', '/fixed\ncommand', '/fixed\0command']) {
    assert.throws(() => upgradeLauncher({ ...configuration, cli: value }), /fixed absolute paths/)
  }
  for (const environment of [
    { ...configuration.environment, NODE_OPTIONS: '--require=untrusted' },
    { ...configuration.environment, HTTP_PROXY: 'http://untrusted.invalid' },
    { ...configuration.environment, CI: null },
    { ...configuration.environment, CI: 'true\nfalse' },
    { ...configuration.environment, PATH: '' },
    { ...configuration.environment, HOME: '' },
    { ...configuration.environment, TMPDIR: '' },
  ]) {
    assert.throws(() => upgradeLauncher({ ...configuration, environment }))
  }
})
