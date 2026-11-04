import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { createWriteStream } from 'node:fs'
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { createServer } from 'node:net'
import { join } from 'node:path'
import { createInterface } from 'node:readline'
import { fileURLToPath } from 'node:url'

export default async function setup() {
  const root = fileURLToPath(new URL('../../../', import.meta.url))
  const web = join(root, 'web')
  await mkdir(join(root, '.ngb'), { recursive: true })
  const privateRoot = await mkdtemp(join(root, '.ngb/browser-'))
  const settingsFile = join(privateRoot, 'settings.json')
  const listener = createServer()
  await new Promise(resolve => listener.listen(0, '127.0.0.1', resolve))
  const port = listener.address().port
  await new Promise(resolve => listener.close(resolve))
  const origin = `http://127.0.0.1:${port}`
  const children = []
  const launch = (command, args, env, name) => {
    const output = createWriteStream(join(privateRoot, `${name}.log`), { mode: 0o600 })
    const child = spawn(command, args, { cwd: web, env: { ...process.env, ...env }, stdio: ['pipe', 'pipe', 'pipe'] })
    child.stdout.pipe(output)
    child.stderr.pipe(output)
    const exited = new Promise(resolve => child.once('close', code => {
      output.end()
      resolve(code)
    }))
    child.stdin.on('error', () => { /* A failed host may close stdin before readiness. */ })
    const state = { child, exited, name }
    children.push(state)
    return state
  }
  const stop = async () => {
    const failures = []
    for (const { child, exited, name } of [...children].reverse()) {
      if (child.pid && child.exitCode === null && child.signalCode === null) {
        if (name === 'api') child.stdin.end('\n')
        else child.kill('SIGTERM')
        const deadline = setTimeout(() => child.kill('SIGKILL'), 30000)
        await exited
        clearTimeout(deadline)
      }
      if (name === 'api' && child.exitCode !== 0) failures.push(`API cleanup failed; see ${privateRoot}/api.log`)
    }
    delete process.env.NGB_EXAMPLE_ORIGIN
    delete process.env.NGB_EXAMPLE_PASSWORD
    await rm(settingsFile, { force: true })
    assert.equal(failures.length, 0, failures.join('\n'))
  }
  try {
    const host = launch('dotnet', [join(root,
      'EquipmentInspection.IntegrationTests/bin/Release/net10.0/EquipmentInspection.IntegrationTests.dll')], {}, 'api')
    const lines = createInterface({ input: host.child.stdout })
    const ready = new Promise((resolve, reject) => {
      const deadline = setTimeout(() => reject(new Error(`API startup timed out; see ${privateRoot}/api.log`)), 180000)
      lines.on('line', line => {
        if (line === 'EQUIPMENT_INSPECTION_READY') {
          clearTimeout(deadline)
          resolve()
        }
      })
      host.child.once('error', error => {
        clearTimeout(deadline)
        reject(error)
      })
      host.exited.then(code => {
        clearTimeout(deadline)
        reject(new Error(`API test host exited with ${code}; see ${privateRoot}/api.log`))
      })
    })
    host.child.stdin.write(`${JSON.stringify({ browserOrigin: origin, settingsFile })}\n`)
    await ready
    lines.close()
    const settings = JSON.parse(await readFile(settingsFile, 'utf8'))
    const issuer = new URL(settings.issuer)
    const configuration = {
      VITE_API_BASE_URL: '',
      VITE_KEYCLOAK_URL: issuer.origin,
      VITE_KEYCLOAK_REALM: issuer.pathname.split('/realms/')[1],
      VITE_KEYCLOAK_CLIENT_ID: settings.clientId,
    }
    await writeFile(join(web, 'dist/runtime-config.js'), `window.__NGB_RUNTIME_CONFIG__ = ${JSON.stringify(configuration)}\n`)
    const preview = launch(process.execPath, ['node_modules/vite/bin/vite.js', 'preview', '--host', '127.0.0.1',
      '--port', String(port), '--strictPort'], { NGB_EXAMPLE_API: settings.api }, 'web')
    let available = false
    for (let attempt = 0; attempt < 100; attempt += 1) {
      assert.equal(preview.child.exitCode, null, `Web preview exited; see ${privateRoot}/web.log`)
      try {
        available = (await fetch(origin, { signal: AbortSignal.timeout(1000) })).ok
        if (available) break
      } catch { /* The preview process is still starting. */ }
      await new Promise(resolve => setTimeout(resolve, 200))
    }
    assert.ok(available, 'Web preview startup timed out.')
    process.env.NGB_EXAMPLE_ORIGIN = origin
    process.env.NGB_EXAMPLE_PASSWORD = settings.password
    return stop
  } catch (error) {
    try {
      await stop()
    } catch (cleanupError) {
      throw new AggregateError([error, cleanupError], 'Browser environment startup and cleanup failed.')
    }
    throw error
  }
}
