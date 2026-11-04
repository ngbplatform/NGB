import assert from 'node:assert/strict'
import { randomBytes, randomUUID } from 'node:crypto'
import { lstat, mkdir, mkdtemp, readFile, realpath, rm, stat, writeFile } from 'node:fs/promises'
import { createServer } from 'node:net'
import { homedir, tmpdir } from 'node:os'
import { basename, delimiter, dirname, isAbsolute, join } from 'node:path'
import { executionEnvironment, redactOutput, runTool } from './process.mjs'
import { applicationConfiguration } from './validation.mjs'
import { applicationFiles, managedPackageFiles } from './application.mjs'
import { digest } from './kit-contracts.mjs'
import { safePath } from './kit-files.mjs'
import { recoverTestRuns, registerTestRun, removeTestResources, testResourceLabels } from './resources.mjs'

const ownerLabel = 'com.ngb.devkit.run'

export async function dockerPluginDirectories(environment = process.env) {
  const directories = (environment.PATH ?? '').split(delimiter).filter(path => isAbsolute(path))
  assert.ok(directories.length <= 128, 'Docker tool search exceeds its path limit.')
  for (const directory of directories) {
    let executable
    try {
      executable = await realpath(join(directory, process.platform === 'win32' ? 'docker.exe' : 'docker'))
    } catch (error) {
      if (!['ENOENT', 'ENOTDIR'].includes(error.code)) throw error
      continue
    }
    try {
      const plugins = join(dirname(executable), '..', 'cli-plugins')
      if ((await stat(plugins)).isDirectory()) return [await realpath(plugins)]
    } catch (error) {
      if (!['ENOENT', 'ENOTDIR'].includes(error.code)) throw error
    }
    return []
  }
  return []
}

export async function stageTestSources(root, destination) {
  for (const [path, bytes] of Object.entries(await applicationFiles(root))) {
    const details = await lstat(await safePath(root, path))
    assert.ok(details.isFile() && details.nlink === 1, `Test source changed while staging: ${path}`)
    const target = join(destination, path)
    await mkdir(dirname(target), { recursive: true })
    await writeFile(target, bytes, { mode: details.mode & 0o777 })
  }
}

async function availablePort() {
  const server = createServer()
  await new Promise((resolve, reject) => {
    server.once('error', reject)
    server.listen(0, '127.0.0.1', resolve)
  })
  const port = server.address().port
  await new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve()))
  return port
}

async function localDockerEndpoint() {
  if (process.platform === 'win32') return 'npipe:////./pipe/docker_engine'
  for (const path of ['/var/run/docker.sock', join(homedir(), '.docker/run/docker.sock')]) {
    try {
      if ((await stat(path)).isSocket()) return `unix://${path}`
    } catch (error) {
      if (error.code !== 'ENOENT') throw error
    }
  }
  throw new Error('A local Docker socket is required for disposable tests. Remote deployment contexts are not used.')
}

export function testCompose(config, { runId, applicationSha256, postgresPassword, keycloakPort, webPort, apiPort, candidate = false }) {
  assert.match(runId, /^[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/)
  assert.match(applicationSha256, /^[a-f0-9]{64}$/)
  assert.match(postgresPassword, /^[a-f0-9]{48}$/)
  const ports = [keycloakPort, webPort, apiPort]
  assert.equal(new Set(ports).size, ports.length, 'Test services require distinct ports.')
  for (const port of ports) assert.ok(Number.isSafeInteger(port) && port > 1024 && port <= 65535)
  const project = role => {
    const projects = config.projects.filter(project => project.role === role)
    assert.equal(projects.length, 1, `Disposable starter profile requires one ${role} project.`)
    assert.equal(dirname(projects[0].path), basename(projects[0].path, '.csproj'),
      `Custom ${role} layout requires an explicit test harness.`)
    return basename(projects[0].path, '.csproj')
  }
  const labels = testResourceLabels({ runId, applicationSha256 })
  const connection = `Host=postgres;Database=ngb_test;Username=postgres;Password=${postgresPassword}`
  const build = role => ({ context: '.', dockerfile: 'infrastructure/Dockerfile',
    args: { PROJECT: project(role), NUGET_CONFIG: candidate ? 'NuGet.Local.Config' : 'NuGet.Config' } })
  return {
    name: `ngb-kit-${runId}`,
    services: {
      postgres: {
        image: 'postgres:18.3-alpine', labels,
        environment: { POSTGRES_DB: 'ngb_test', POSTGRES_PASSWORD: postgresPassword },
        volumes: ['test-postgres:/var/lib/postgresql'],
        healthcheck: { test: ['CMD', 'pg_isready', '-U', 'postgres', '-d', 'ngb_test'], interval: '2s', timeout: '3s', retries: 45 },
      },
      keycloak: {
        image: 'quay.io/keycloak/keycloak:26.5.6', labels,
        command: ['start-dev', '--import-realm', '--hostname-strict=false'],
        ports: [`127.0.0.1:${keycloakPort}:8080`],
        volumes: ['./test-realm.json:/opt/keycloak/data/import/ngb-test-realm.json:ro'],
        environment: {
          KC_HOSTNAME: `http://127.0.0.1:${keycloakPort}`,
          KC_HOSTNAME_BACKCHANNEL_DYNAMIC: 'true',
          KC_HEALTH_ENABLED: 'true',
        },
        healthcheck: {
          test: ['CMD-SHELL', "bash -c 'exec 3<>/dev/tcp/localhost/9000; printf \"GET /health/ready HTTP/1.0\\r\\n\\r\\n\" >&3; cat <&3' | grep -q '\"status\": \"UP\"'"],
          interval: '3s', timeout: '3s', retries: 40,
        },
      },
      migrator: {
        labels, build: build('migrator'), restart: 'no',
        entrypoint: ['/bin/sh', '/app/migrate.sh'],
        environment: { NGB_CONNECTION_STRING: connection },
        depends_on: { postgres: { condition: 'service_healthy' } },
      },
      api: {
        labels, build: build('api'),
        ports: [`127.0.0.1:${apiPort}:8080`],
        environment: {
          ConnectionStrings__DefaultConnection: connection,
          KeycloakSettings__Issuer: `http://127.0.0.1:${keycloakPort}/realms/ngb-test`,
          KeycloakSettings__MetadataAddress: 'http://keycloak:8080/realms/ngb-test/.well-known/openid-configuration',
          KeycloakSettings__RequireHttpsMetadata: 'false',
          KeycloakSettings__ClientIds__0: 'ngb-web',
          KeycloakSettings__ClientIds__1: 'ngb-load',
          FeatureManagement__Mcp: 'false',
          FeatureManagement__AiAssistant: 'false',
          FeatureManagement__Attachments: 'false',
          FeatureManagement__Notes: 'false',
        },
        depends_on: { migrator: { condition: 'service_completed_successfully' }, keycloak: { condition: 'service_healthy' } },
      },
      web: { labels, build: `./${config.frontend.path}`, ports: [`127.0.0.1:${webPort}:8080`], depends_on: ['api'] },
    },
    volumes: { 'test-postgres': { labels } },
    networks: { default: { labels } },
  }
}

export async function attachTestEnvironment(environment = process.env) {
  const runId = environment.NGB_TEST_RUN_ID
  const origin = environment.NGB_TEST_ORIGIN
  const password = environment.NGB_TEST_PASSWORD
  assert.match(runId ?? '', /^[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/)
  assert.match(password ?? '', /^[a-f0-9]{48}$/)
  assert.match(origin ?? '', /^http:\/\/127\.0\.0\.1:[1-9][0-9]{3,4}$/)
  const port = Number(new URL(origin).port)
  assert.ok(port > 1024 && port <= 65535, 'Invalid disposable browser port.')
  const home = await mkdtemp(join(tmpdir(), 'ngb-test-attach-'))
  try {
    const env = executionEnvironment(home, { DOCKER_HOST: await localDockerEndpoint() })
    const listed = await runTool('docker', ['ps', '--quiet', '--filter', `label=${ownerLabel}=${runId}`,
      '--filter', 'label=com.docker.compose.service=web'], { cwd: home, env, timeoutMs: 15000 })
    assert.ok(listed.state === 'passed' && /^[a-f0-9]{12,64}$/.test(listed.output.trim()),
      'The browser handoff requires exactly one running web container owned by this test run.')
    const inspected = await runTool('docker', ['inspect', '--format', '{{json .NetworkSettings.Ports}}', listed.output.trim()],
      { cwd: home, env, timeoutMs: 15000 })
    assert.equal(inspected.state, 'passed', 'Cannot verify the disposable web endpoint.')
    assert.deepEqual(JSON.parse(inspected.output)['8080/tcp'], [{ HostIp: '127.0.0.1', HostPort: String(port) }],
      'Browser endpoint does not match the owned container.')
    return { runId, origin, password }
  } finally {
    await rm(home, { recursive: true, force: true })
  }
}

export async function startTestEnvironment(root, { signal } = {}) {
  signal?.throwIfAborted()
  root = await realpath(root)
  const config = await applicationConfiguration(root)
  const packages = await managedPackageFiles(root)
  const candidate = Object.hasOwn(packages, 'NuGet.Local.Config')
  const runId = randomUUID()
  const password = randomBytes(24).toString('hex')
  const postgresPassword = randomBytes(24).toString('hex')
  const keycloakPort = await availablePort()
  const webPort = await availablePort()
  const apiPort = await availablePort()
  const endpoint = await localDockerEndpoint()
  const registration = await registerTestRun(root, runId)
  const stage = registration.stage
  const env = executionEnvironment(join(stage, 'controller'), { DOCKER_HOST: endpoint })
  const application = join(stage, 'application')
  const composeFile = join(application, 'test-compose.json')
  let started = false
  const command = async (args, cancellable = false) => {
    if (cancellable) await registration.launching()
    let result
    try {
      result = await runTool('docker', ['compose', '--project-name', `ngb-kit-${runId}`, '--env-file',
        join(stage, 'empty.env'), '-f', composeFile, ...args],
      { cwd: application, env, timeoutMs: 900000, signal: cancellable ? signal : undefined,
        onSpawn: cancellable ? registration.spawned : undefined })
    } finally {
      if (cancellable) await registration.settled()
    }
    assert.equal(result.state, 'passed', `Disposable test operation failed (${result.reason ?? result.exitCode}):\n${redactOutput(result.output, [password, postgresPassword]).slice(-4096)}`)
    return result.output.trim()
  }
  const stop = async () => {
    if (!started) {
      await registration.finish()
      return
    }
    await removeTestResources(registration.owner, env, root)
    started = false
    await registration.finish()
  }
  try {
    await mkdir(join(env.HOME, '.docker'), { recursive: true, mode: 0o700 })
    await writeFile(join(env.HOME, '.docker/config.json'), JSON.stringify({
      cliPluginsExtraDirs: await dockerPluginDirectories(env),
    }), { mode: 0o600 })
    const compose = await runTool('docker', ['compose', 'version', '--short'], { cwd: stage, env, timeoutMs: 15000 })
    assert.equal(compose.state, 'passed', 'Docker Compose must be installed alongside the trusted Docker CLI before E2E tests can run.')
    await stageTestSources(root, application)
    for (const [path, expected] of Object.entries(packages)) {
      const bytes = await readFile(await safePath(root, path))
      assert.equal(digest(bytes), expected, `Candidate package changed while staging tests: ${path}`)
      const destination = join(application, path)
      await mkdir(dirname(destination), { recursive: true, mode: 0o700 })
      await writeFile(destination, bytes, { mode: 0o600 })
    }
    await writeFile(join(stage, 'empty.env'), '', { mode: 0o600 })
    const origin = `http://127.0.0.1:${webPort}`
    const realm = {
      realm: 'ngb-test', enabled: true, sslRequired: 'none',
      roles: { realm: [{ name: 'ngb-admin' }] },
      clients: [
        { clientId: 'ngb-web', publicClient: true, standardFlowEnabled: true,
          redirectUris: [`${origin}/*`], webOrigins: [origin], attributes: { 'pkce.code.challenge.method': 'S256' } },
        { clientId: 'ngb-load', publicClient: true, standardFlowEnabled: false, directAccessGrantsEnabled: true },
      ],
      users: ['administrator', 'reader'].map(username => ({ username, firstName: 'Test', lastName: username,
        email: `${username}@example.test`, enabled: true, emailVerified: true,
        realmRoles: username === 'administrator' ? ['ngb-admin'] : [],
        credentials: [{ type: 'password', value: password, temporary: false }] })),
    }
    // The container's non-root identity must read this file. Its parent staging
    // directory remains private (0700), and these credentials exist only for this run.
    await writeFile(join(application, 'test-realm.json'), JSON.stringify(realm), { mode: 0o644 })
    await writeFile(join(application, config.frontend.path, 'public/runtime-config.js'),
      `window.__NGB_RUNTIME_CONFIG__ = ${JSON.stringify({ VITE_API_BASE_URL: '',
        VITE_KEYCLOAK_URL: `http://127.0.0.1:${keycloakPort}`, VITE_KEYCLOAK_REALM: 'ngb-test', VITE_KEYCLOAK_CLIENT_ID: 'ngb-web' })}\n`)
    await writeFile(composeFile, JSON.stringify(testCompose(config, { runId,
      applicationSha256: registration.owner.applicationSha256, postgresPassword, keycloakPort, webPort, apiPort, candidate })), { mode: 0o600 })
    started = true
    await command(['up', '--build', '-d'], true)
    const deadline = Date.now() + 120000
    let ready = false
    while (Date.now() < deadline) {
      signal?.throwIfAborted()
      try {
        const checks = await Promise.all([
          fetch(`http://127.0.0.1:${apiPort}/health`, { signal: AbortSignal.timeout(2000) }),
          fetch(`http://127.0.0.1:${keycloakPort}/realms/ngb-test/.well-known/openid-configuration`, { signal: AbortSignal.timeout(2000) }),
        ])
        if (checks.every(response => response.ok)) {
          ready = true
          break
        }
      } catch {
        // Newly started test hosts may not yet be listening.
      }
      await new Promise(resolve => setTimeout(resolve, 1000))
    }
    if (!ready) {
      const logs = await command(['logs', '--no-color', '--tail', '40', 'api', 'keycloak', 'migrator'])
      throw new Error(`Disposable browser environment did not become healthy:\n${redactOutput(logs, [password, postgresPassword]).slice(-8192)}`)
    }
    signal?.throwIfAborted()
    return { runId, origin, password, apiOrigin: `http://127.0.0.1:${apiPort}`,
      issuer: `http://127.0.0.1:${keycloakPort}/realms/ngb-test`, stop }
  } catch (error) {
    if (started) {
      try {
        const logs = await command(['logs', '--no-color', '--tail', '20', 'api', 'keycloak', 'migrator'])
        error.message += `\nTest service diagnostics:\n${redactOutput(logs, [password, postgresPassword]).slice(-8192)}`
      } catch {
        // Preserve the original failure if diagnostics are unavailable.
      }
    }
    try {
      await stop()
    } catch (cleanup) {
      throw new AggregateError([error, cleanup], `Test environment failed: ${error.message}\nCleanup failed: ${cleanup.message}\nInspect owned resources for run ${runId}. Recovery files: ${stage}`)
    }
    throw error
  }
}

export async function recoverTestEnvironment(root, apply = false) {
  const home = await mkdtemp(join(tmpdir(), 'ngb-resource-recovery-'))
  try {
    const env = executionEnvironment(home, { DOCKER_HOST: await localDockerEndpoint() })
    return await recoverTestRuns(root, { apply, env })
  } finally {
    await rm(home, { recursive: true, force: true })
  }
}
