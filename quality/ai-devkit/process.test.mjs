import assert from 'node:assert/strict'
import { chmod, copyFile, mkdtemp, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import { executionEnvironment, redactOutput, runTool, toolInvocation } from '../../packaging/templates/content/infrastructure/ngb/process.mjs'

test('execution inherits tool locations but excludes credentials and deployment configuration', () => {
  const env = executionEnvironment('/temporary/home', {}, {
    PATH: '/tools', DOTNET_ROOT: '/sdk', HOME: '/personal', AWS_SECRET_ACCESS_KEY: 'private',
    DOCKER_HOST: 'tcp://production:2376', NGB_CONNECTION_STRING: 'production', npm_config_registry: 'https://untrusted.test',
  })
  assert.equal(env.PATH, '/tools')
  assert.equal(env.DOTNET_ROOT, '/sdk')
  assert.equal(env.HOME, '/temporary/home')
  assert.equal(env.CI, 'true')
  for (const name of ['AWS_SECRET_ACCESS_KEY', 'DOCKER_HOST', 'NGB_CONNECTION_STRING', 'npm_config_registry']) {
    assert.equal(env[name], undefined)
  }
  assert.equal(executionEnvironment('/test', { DOCKER_HOST: 'unix:///test.sock' }).DOCKER_HOST, 'unix:///test.sock')
})

test('diagnostics remove structured, HTTP and explicit secret values', () => {
  const text = 'Bearer abc123\n{"password":"secret value"}\napi_key=my-key\nhttps://name:pwd@example.test/path\nknown-secret'
  const output = redactOutput(text, ['', 'known-secret'])
  for (const secret of ['abc123', 'secret value', 'my-key', 'name:pwd', 'known-secret']) {
    assert.ok(!output.includes(secret), secret)
  }
  assert.ok(output.includes('example.test/path'))
  assert.equal(redactOutput('ordinary diagnostic'), 'ordinary diagnostic')
})

test('isolated tools retain credential-free proxy transport without inheriting registry overrides', async () => {
  const transport = {
    HTTP_PROXY: 'http://127.0.0.1:3128', HTTPS_PROXY: 'http://127.0.0.1:3128', ALL_PROXY: 'socks5h://127.0.0.1:1080',
    http_proxy: 'https://proxy.example.test/', https_proxy: '', all_proxy: 'socks5://127.0.0.1:1080',
    NO_PROXY: 'localhost,127.0.0.1,::1', no_proxy: '.example.test,10.0.0.0/8', NODE_USE_ENV_PROXY: '1',
  }
  const env = executionEnvironment('/isolated', {}, { ...transport,
    npm_config_registry: 'https://untrusted.example.test', NPM_CONFIG_PROXY: 'https://user:secret@proxy.example.test',
    AWS_SECRET_ACCESS_KEY: 'secret', NUGET_AUTH_TOKEN: 'secret' })
  for (const [name, value] of Object.entries(transport)) assert.equal(env[name], value)
  for (const name of ['npm_config_registry', 'NPM_CONFIG_PROXY', 'AWS_SECRET_ACCESS_KEY', 'NUGET_AUTH_TOKEN']) {
    assert.equal(env[name], undefined)
  }
  const child = await runTool('node', ['--input-type=module', '--eval', 'console.log(JSON.stringify(process.env))'], { env })
  assert.equal(child.state, 'passed')
  const received = JSON.parse(child.output)
  for (const [name, value] of Object.entries(transport)) assert.equal(received[name], value)
  assert.equal(received.AWS_SECRET_ACCESS_KEY, undefined)
  assert.equal(executionEnvironment('/isolated', {}, { NODE_USE_ENV_PROXY: '1' }).NODE_USE_ENV_PROXY, undefined)
  assert.equal(executionEnvironment('/isolated', {}, { HTTP_PROXY: '', NODE_USE_ENV_PROXY: '1' }).HTTP_PROXY, '')
  for (const name of ['HTTP_PROXY', 'HTTPS_PROXY', 'ALL_PROXY', 'http_proxy', 'https_proxy', 'all_proxy']) {
    for (const value of ['not-a-url', 'http://user:synthetic-secret@proxy.test', 'https://proxy.test/path',
      'https://proxy.test?token=synthetic-secret', 'https://proxy.test#synthetic-secret', 'file:///tmp/proxy',
      'http://proxy.test\n', 'x'.repeat(2049)]) {
      assert.throws(() => executionEnvironment('/isolated', {}, { [name]: value }), error =>
        error.message.includes(name) && !error.message.includes('synthetic-secret'))
    }
  }
  assert.throws(() => executionEnvironment('/isolated', {}, { HTTPS_PROXY: 'socks5://proxy.test' }), /HTTPS_PROXY/)
  for (const name of ['NO_PROXY', 'no_proxy']) {
    for (const value of ['localhost\nsecret', 'localhost\0secret', 'x'.repeat(4097), 123]) {
      assert.throws(() => executionEnvironment('/isolated', {}, { [name]: value }), /proxy bypass/)
    }
  }
})

test('process arguments stay separate and Windows npm runs through Node without a command shell', () => {
  assert.deepEqual(toolInvocation('dotnet', ['build', 'folder with spaces/app.csproj']), {
    command: 'dotnet', args: ['build', 'folder with spaces/app.csproj'],
  })
  const windows = toolInvocation('npm', ['run', 'build'], 'win32', { npm_execpath: 'C:\\npm\\npm-cli.js' }, () => true)
  assert.equal(windows.command, process.execPath)
  assert.deepEqual(windows.args, ['C:\\npm\\npm-cli.js', 'run', 'build'])
  assert.throws(() => toolInvocation('npm', [], 'win32', {}, () => false), /locate/)
  assert.throws(() => toolInvocation('sh', ['-c', 'anything']), /Unsupported/)
  assert.throws(() => toolInvocation('dotnet', ['\0']), /arguments/)
  assert.throws(() => toolInvocation('dotnet', ['x'.repeat(8193)]), /arguments/)
})

test('subprocesses report exit, missing tooling, timeout, cancellation and bounded output', async t => {
  const root = await mkdtemp(join(tmpdir(), 'ngb-process-contract-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  const executable = join(root, process.platform === 'win32' ? 'dotnet.exe' : 'dotnet')
  await copyFile(process.execPath, executable)
  await chmod(executable, 0o700)
  const env = executionEnvironment(root, { PATH: root, TEST_TOKEN: 'sensitive-value' })
  const invoke = (body, options = {}, args = []) => runTool('dotnet', ['--eval', body, '--', ...args], { cwd: root, env, ...options })
  const result = await invoke('process.stdout.write(JSON.stringify(process.argv)); process.stdout.write(process.env.TEST_TOKEN)',
    {}, ['a; echo injected', '$(secret)', 'one two'])
  assert.equal(result.state, 'passed')
  assert.ok(result.output.includes('a; echo injected'))
  assert.ok(result.output.includes('[REDACTED]'))
  assert.ok(!result.output.includes('sensitive-value'))
  assert.equal((await invoke('process.stderr.write("intentional failure"); process.exitCode = 9')).exitCode, 9)
  let observedPid
  assert.equal((await invoke('process.stdout.write("owned")', { onSpawn: async pid => { observedPid = pid } })).state, 'passed')
  assert.ok(Number.isInteger(observedPid) && observedPid > 0)
  assert.equal((await invoke('setInterval(() => {}, 1000)', { onSpawn: async () => { throw new Error('Receipt unavailable') } })).reason,
    'ownership-record')
  assert.equal((await invoke('setInterval(() => {}, 1000)', { timeoutMs: 80 })).reason, 'timeout')
  const signal = AbortSignal.abort()
  assert.equal((await invoke('setInterval(() => {}, 1000)', { signal })).reason, 'cancelled')
  const limited = await invoke('process.stdout.write("x".repeat(10000))', { maxBytes: 100 })
  assert.equal(limited.reason, 'output-limit')
  assert.ok(limited.output.length <= 100)
  await rm(executable)
  const missing = await runTool('dotnet', [], { cwd: root, env })
  assert.equal(missing.state, 'not-run')
  assert.equal(missing.reason, 'ENOENT')
  await assert.rejects(runTool('dotnet', [], { timeoutMs: 0 }), /resource limits/)
})

test('loopback sandbox proxy authentication reaches children but is redacted from diagnostics', async () => {
  for (const origin of ['http://localhost:3128', 'https://127.0.0.1:3128', 'socks5h://[::1]:1080']) {
    const url = new URL(origin)
    url.username = 'sandbox-command'
    url.password = 'synthetic%2Fsecret-proxy'
    const name = url.protocol.startsWith('socks') ? 'ALL_PROXY' : 'HTTPS_PROXY'
    const env = executionEnvironment('/isolated', {}, { [name]: url.href })
    const script = `
const value = process.env[${JSON.stringify(name)}]
if (!value?.includes('sandbox-command')) process.exit(2)
console.log(value)
for (const part of ['username', 'password']) {
  console.log(new URL(value)[part])
  console.log(decodeURIComponent(new URL(value)[part]))
}
`
    const child = await runTool('node', ['--input-type=module', '--eval', script], { env })
    assert.equal(child.state, 'passed')
    assert.ok(child.output.includes('[REDACTED]'))
    assert.ok(!child.output.includes('synthetic'))
    assert.ok(!child.output.includes('sandbox-command'))
  }
  for (const host of ['localhost.example.test', '127.0.0.2', '10.0.0.1', 'proxy.test']) {
    assert.throws(() => executionEnvironment('/isolated', {}, { HTTP_PROXY: `http://user:synthetic-secret@${host}:3128` }),
      error => !error.message.includes('synthetic-secret'))
  }
  assert.equal(redactOutput('socks5h://username:password@localhost:1080'), 'socks5h://[REDACTED]@localhost:1080')
})
