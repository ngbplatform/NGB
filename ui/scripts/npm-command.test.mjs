import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { mkdtemp, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import { resolveNpmCommand } from './npm-command.mjs'

test('Unix keeps the npm executable and arguments', () => {
  const args = ['pack', '/tmp/package with spaces', '--pack-destination', '/tmp/output']
  assert.deepEqual(resolveNpmCommand(args, { platform: 'linux' }), {
    command: 'npm',
    args,
  })

  if (process.platform !== 'win32') {
    assert.deepEqual(resolveNpmCommand(args), { command: 'npm', args })
  }
})

test('Windows locates the bundled npm CLI when invoked directly with Node', () => {
  const execPath = 'C:\\Program Files\\nodejs\\node.exe'
  const npmCli = 'C:\\Program Files\\nodejs\\node_modules\\npm\\bin\\npm-cli.js'
  const args = ['pack', 'C:\\Projects with spaces\\package']

  const environments = [
    {},
    { npm_execpath: 'C:\\tools\\pnpm.cjs' },
    { npm_execpath: 'C:\\old\\npm-cli.js' },
  ]

  for (const env of environments) {
    assert.deepEqual(resolveNpmCommand(args, {
      platform: 'win32',
      execPath,
      env,
      exists: path => path === npmCli,
    }), {
      command: execPath,
      args: [npmCli, ...args],
    })
  }
})

test('Windows CLI invocation preserves literal arguments and propagates failures without a shell', async t => {
  const root = await mkdtemp(join(tmpdir(), 'ngb npm command & '))
  t.after(() => rm(root, { recursive: true, force: true }))
  const npmCli = join(root, 'npm-cli.js')
  await writeFile(npmCli, 'process.stdout.write(JSON.stringify(process.argv.slice(2)))\n')

  const args = [
    'pack',
    join(root, 'package & %PATH% ! ^'),
    '--pack-destination',
    join(root, 'output $(literal)'),
  ]
  const npm = resolveNpmCommand(args, {
    platform: 'win32',
    env: { npm_execpath: npmCli },
  })
  assert.equal(npm.command, process.execPath)
  assert.equal(npm.args[0], npmCli)
  assert.deepEqual(JSON.parse(execFileSync(npm.command, npm.args, { encoding: 'utf8' })), args)

  await writeFile(npmCli, 'process.exit(17)\n')
  assert.throws(() => execFileSync(npm.command, npm.args), { status: 17 })
})

test('missing Windows npm CLI gives an actionable error before packing', () => {
  assert.throws(() => resolveNpmCommand(['pack'], {
    platform: 'win32',
    execPath: 'C:\\nodejs\\node.exe',
    env: {},
    exists: () => false,
  }), /Cannot locate npm-cli\.js.*npm --prefix ui run pack:platform-ui/)
})
