import { execFile, spawn, type ChildProcess } from 'node:child_process'
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { createInterface } from 'node:readline'
import { fileURLToPath } from 'node:url'
import { promisify } from 'node:util'

import { createServer, type ViteDevServer } from 'vite'

export type ContentStackSettings = {
  api: string
  storage: string
  token: string
  keycloakUrl: string
  realm: string
  clientId: string
  username: string
  password: string
  webOrigin: string
}

const repositoryRoot = fileURLToPath(new URL('../../../../', import.meta.url))
const hostProject = path.join(repositoryRoot, 'quality/integration/NGB.PropertyManagement.TestHost/NGB.PropertyManagement.TestHost.csproj')
const run = promisify(execFile)

export async function readContentStackSettings(): Promise<ContentStackSettings> {
  const file = process.env.NGB_PM_CONTENT_SETTINGS_FILE
  if (!file) throw new Error('PM content E2E stack has not been initialized.')
  return JSON.parse(await readFile(file, 'utf8')) as ContentStackSettings
}

export default async function setupContentStack(): Promise<() => Promise<void>> {
  await run('dotnet', ['build', hostProject, '--verbosity', 'quiet', '-m:1'], {
    cwd: repositoryRoot,
    timeout: 180_000,
  })

  const temporaryDirectory = await mkdtemp(path.join(tmpdir(), 'ngb-pm-content-'))
  const settingsFile = path.join(temporaryDirectory, 'settings.json')
  let settings: ContentStackSettings | undefined
  let host: ChildProcess | undefined
  let server: ViteDevServer | undefined

  async function cleanup(): Promise<void> {
    try {
      if (host?.pid && host.exitCode === null && host.signalCode === null) {
        const stopped = new Promise<void>((resolve) => host!.once('exit', () => resolve()))
        host.stdin?.end('\n')
        const timeout = setTimeout(() => host?.kill('SIGKILL'), 30_000)
        try {
          await stopped
        } finally {
          clearTimeout(timeout)
        }
      }
    } finally {
      try {
        await server?.close()
      } finally {
        await rm(temporaryDirectory, { recursive: true, force: true })
        delete process.env.NGB_PM_CONTENT_SETTINGS_FILE
      }
    }
  }

  try {
    server = await createServer({
      root: path.join(repositoryRoot, 'ui/ngb-property-management-web'),
      mode: 'e2e-real',
      logLevel: 'error',
      server: { host: '127.0.0.1', port: 0 },
      plugins: [{
        name: 'pm-content-runtime-config',
        configureServer(vite) {
          vite.middlewares.use((request, response, next) => {
            if (request.url?.split('?')[0] !== '/runtime-config.js') return next()
            if (!settings) {
              response.statusCode = 503
              response.end('Test stack is starting.')
              return
            }

            response.setHeader('Content-Type', 'application/javascript')
            response.setHeader('Cache-Control', 'no-store')
            response.end(`window.__NGB_RUNTIME_CONFIG__ = ${JSON.stringify({
              VITE_API_BASE_URL: settings.api,
              VITE_KEYCLOAK_URL: settings.keycloakUrl,
              VITE_KEYCLOAK_REALM: settings.realm,
              VITE_KEYCLOAK_CLIENT_ID: settings.clientId,
              VITE_NGB_E2E_AUTH_BYPASS: false,
            })}`)
          })
        },
      }],
    })

    await server.listen()
    const webOrigin = server.resolvedUrls!.local[0]!.replace(/\/$/, '')
    host = spawn('dotnet', ['run', '--no-build', '--project', hostProject], {
      cwd: repositoryRoot,
      stdio: ['pipe', 'pipe', 'pipe'],
    })

    const lines = createInterface({ input: host.stdout! })
    let diagnostics = ''
    host.stderr!.on('data', (data) => {
      diagnostics = (diagnostics + data.toString()).slice(-8000)
    })

    const ready = new Promise<void>((resolve, reject) => {
      const timeout = setTimeout(() => {
        finish(new Error(`PM content stack startup timed out. ${diagnostics}`))
      }, 180_000)

      const onExit = (code: number | null) => {
        finish(new Error(`PM content stack exited (${code}). ${diagnostics}`))
      }

      const finish = (error?: Error) => {
        clearTimeout(timeout)
        lines.close()
        host!.off('error', finish)
        host!.off('exit', onExit)
        if (error) reject(error)
        else resolve()
      }

      host!.once('error', finish)
      host!.once('exit', onExit)
      lines.on('line', (line) => {
        if (line === 'NGB_CONTENT_STACK_READY') finish()
        else diagnostics = `${diagnostics}\n${line}`.slice(-8000)
      })
    })

    host.stdin!.write(`${JSON.stringify({ browserOrigin: webOrigin, settingsFile })}\n`)
    await ready
    host.stdout!.resume()
    settings = { ...JSON.parse(await readFile(settingsFile, 'utf8')), webOrigin }
    await writeFile(settingsFile, JSON.stringify(settings), { mode: 0o600 })
    process.env.NGB_PM_CONTENT_SETTINGS_FILE = settingsFile
    return cleanup
  } catch (error) {
    await cleanup()
    throw error
  }
}
