import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { existsSync } from 'node:fs'
import { join, win32 } from 'node:path'

const allowedTools = new Set(['dotnet', 'npm', 'docker', 'node', 'k6'])
const inheritedNames = new Set(['PATH', 'SystemRoot', 'WINDIR', 'COMSPEC', 'PATHEXT',
  'TMP', 'TEMP', 'TMPDIR', 'LANG', 'LC_ALL', 'DOTNET_ROOT', 'DOTNET_ROOT_X64', 'DOTNET_ROOT_ARM64'])
const proxyNames = ['HTTP_PROXY', 'HTTPS_PROXY', 'ALL_PROXY', 'http_proxy', 'https_proxy', 'all_proxy']

export function executionEnvironment(home, overrides = {}, source = process.env) {
  const environment = Object.fromEntries(Object.entries(source).filter(([name]) => inheritedNames.has(name)))
  for (const name of proxyNames) {
    const value = source[name]
    if (value === undefined) continue
    assert.ok(typeof value === 'string' && value.length <= 2048 && !/[\s\0]/.test(value),
      `${name} must be a bounded proxy URL.`)
    if (value) {
      let proxy
      try {
        proxy = new URL(value)
      } catch {
        throw new Error(`${name} must be a valid proxy URL.`)
      }
      const protocols = name.toUpperCase() === 'ALL_PROXY' ? ['http:', 'https:', 'socks5:', 'socks5h:'] : ['http:', 'https:']
      const local = ['localhost', '127.0.0.1', '[::1]'].includes(proxy.hostname)
      assert.ok(protocols.includes(proxy.protocol) && proxy.hostname && (local || !proxy.username && !proxy.password)
        && !proxy.search && !proxy.hash && ['', '/'].includes(proxy.pathname),
      `${name} must be a proxy origin without path, query or fragment; credentials require an exact loopback host.`)
    }
    environment[name] = value
  }
  for (const name of ['NO_PROXY', 'no_proxy']) {
    const value = source[name]
    if (value === undefined) continue
    assert.ok(typeof value === 'string' && value.length <= 4096 && !/[\r\n\0]/.test(value),
      `${name} must be a bounded proxy bypass list.`)
    environment[name] = value
  }
  if (source.NODE_USE_ENV_PROXY === '1' && proxyNames.some(name => environment[name])) {
    environment.NODE_USE_ENV_PROXY = '1'
  }
  return {
    ...environment,
    HOME: home,
    USERPROFILE: home,
    DOTNET_CLI_HOME: home,
    DOTNET_CLI_TELEMETRY_OPTOUT: '1',
    DOTNET_NOLOGO: '1',
    NUGET_PACKAGES: join(home, 'nuget'),
    NUGET_HTTP_CACHE_PATH: join(home, 'nuget-http'),
    npm_config_cache: join(home, 'npm'),
    npm_config_update_notifier: 'false',
    CI: 'true',
    ...overrides,
  }
}

export function redactOutput(text, secrets = []) {
  let output = text.replace(/\b(Bearer\s+)[^\s"']+/gi, '$1[REDACTED]')
    .replace(/((?:password|token|secret|api[_-]?key|connectionstring)["']?\s*[=:]\s*["']?)[^\r\n,"']+/gi, '$1[REDACTED]')
    .replace(/((?:https?|socks5h?):\/\/)[^\s/@]+@/gi, '$1[REDACTED]@')
  for (const secret of secrets) {
    if (secret) output = output.replaceAll(secret, '[REDACTED]')
  }
  return output
}

export function toolInvocation(tool, args, platform = process.platform, env = process.env, exists = existsSync) {
  assert.ok(allowedTools.has(tool), `Unsupported tool: ${tool}`)
  assert.ok(Array.isArray(args) && args.length <= 128 && args.every(value =>
    typeof value === 'string' && value.length <= 8192 && !value.includes('\0')), 'Invalid process arguments.')
  if (tool === 'node') return { command: process.execPath, args }
  if (tool !== 'npm' || platform !== 'win32') return { command: tool, args }
  const candidates = [env.npm_execpath,
    win32.join(win32.dirname(process.execPath), 'node_modules/npm/bin/npm-cli.js')].filter(Boolean)
  const script = candidates.find(path => path.endsWith('npm-cli.js') && exists(path))
  assert.ok(script, 'Cannot locate npm-cli.js. Install npm with the supported Node distribution.')
  return { command: process.execPath, args: [script, ...args] }
}

export async function runTool(tool, args, { cwd, env, signal, onSpawn, timeoutMs = 600000, maxBytes = 1024 * 1024 } = {}) {
  assert.ok(timeoutMs > 0 && timeoutMs <= 3600000 && maxBytes > 0 && maxBytes <= 16 * 1024 * 1024,
    'Invalid subprocess resource limits.')
  const invocation = toolInvocation(tool, args, process.platform, env)
  const started = performance.now()
  const secrets = Object.entries(env ?? {}).filter(([key]) => /password|secret|token|key/i.test(key)).map(([, value]) => value)
  for (const name of proxyNames) {
    if (!env?.[name]) continue
    try {
      const proxy = new URL(env[name])
      for (const value of [proxy.username, proxy.password]) {
        if (value) secrets.push(value, decodeURIComponent(value))
      }
      if (proxy.username || proxy.password) secrets.push(env[name])
    } catch {
      // Execution environments validate inherited proxies before launching tools.
    }
  }
  return await new Promise(resolve => {
    const child = spawn(invocation.command, invocation.args, {
      cwd,
      env,
      shell: false,
      windowsHide: true,
      detached: process.platform !== 'win32',
      stdio: ['ignore', 'pipe', 'pipe'],
    })
    const chunks = []
    let size = 0
    let reason = null
    let processError = null
    let ownership = Promise.resolve()
    const stop = cause => {
      reason ??= cause
      if (!child.pid) return
      if (process.platform === 'win32') {
        const executable = join(env?.SystemRoot ?? 'C:\\Windows', 'System32', 'taskkill.exe')
        const cleanup = spawn(executable, ['/pid', String(child.pid), '/t', '/f'], { stdio: 'ignore', shell: false })
        cleanup.on('error', () => child.kill())
      } else {
        try {
          process.kill(-child.pid, 'SIGKILL')
        } catch (error) {
          if (error.code !== 'ESRCH') child.kill('SIGKILL')
        }
      }
    }
    const collect = bytes => {
      size += bytes.length
      if (size > maxBytes) stop('output-limit')
      else chunks.push(bytes)
    }
    const cancel = () => stop('cancelled')
    const timer = setTimeout(() => stop('timeout'), timeoutMs)
    signal?.addEventListener('abort', cancel, { once: true })
    child.stdout.on('data', collect)
    child.stderr.on('data', collect)
    child.once('spawn', () => {
      ownership = Promise.resolve().then(() => onSpawn?.(child.pid)).catch(() => stop('ownership-record'))
    })
    child.on('error', error => { processError = error.code ?? 'SPAWN_ERROR' })
    child.on('close', async (code, terminationSignal) => {
      await ownership
      clearTimeout(timer)
      signal?.removeEventListener('abort', cancel)
      resolve({
        tool,
        exitCode: code,
        state: processError ? 'not-run' : code === 0 && !reason ? 'passed' : 'failed',
        reason: processError ?? reason ?? terminationSignal,
        elapsedMs: Math.round(performance.now() - started),
        output: redactOutput(Buffer.concat(chunks).toString('utf8'), secrets),
      })
    })
    if (signal?.aborted) cancel()
  })
}
