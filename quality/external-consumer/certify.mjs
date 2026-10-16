import assert from 'node:assert/strict'
import { execFileSync, spawn } from 'node:child_process'
import { createHash, randomBytes } from 'node:crypto'
import { createWriteStream } from 'node:fs'
import { createServer } from 'node:http'
import { cp, mkdir, mkdtemp, readFile, readdir, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { chromium, expect } from '@playwright/test'
import { assertLayering } from './layering.mjs'
import { validateMatrix } from './release-contracts.mjs'
import { startAfterSuccessfulMigration } from './sequencing.mjs'
import { assertConsumerIsolation, verifyFrozenFixture } from './contracts.mjs'
import { withManagedIgnores } from '../../packaging/templates/content/infrastructure/ngb/contracts.mjs'
import { dependencyCache } from './dependency-cache.mjs'
import { saveInfrastructureDiagnostics, writeInfrastructureConfiguration } from './infrastructure.mjs'
import {
  authenticatedApi, createCoreState, createContentState,
  verifyCorePreservation, verifyCoreContinuation, verifyContentPreservation, verifyContentContinuation,
} from '../upgrade-certification/scenarios.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const matrix = JSON.parse(await readFile(process.env.NGB_RELEASE_MATRIX ?? join(repository, 'quality/external-consumer/matrix.json'), 'utf8'))
validateMatrix(matrix)
const sourceVersion = matrix.source
const targetVersion = matrix.target
const profile = process.argv[2]
const profiles = Object.keys(matrix.profiles)
assert.ok(profiles.includes(profile), `Usage: node quality/external-consumer/certify.mjs ${profiles.join('|')} [artifact-directory]`)
const artifacts = resolve(process.argv[3] ?? join(repository, 'artifacts'))
const registryOnly = process.env.NGB_REGISTRY_ONLY === 'true'
assert.ok(!registryOnly || profile === 'clean-starter', 'Registry smoke uses the clean starter profile.')
const upgrade = profile.endsWith('-upgrade')
const extension = profile !== 'clean-starter'
const notes = profile === 'notes-upgrade' || profile === 'attachments-upgrade'
const attachments = profile === 'attachments-upgrade'
const root = await mkdtemp(join(tmpdir(), `ngb-${profile}-`))
const application = join(root, 'application')
const feed = join(root, 'feed')
const infrastructure = join(root, 'infrastructure')
const source = join(repository, matrix.sourceFixture)
const sourceManifest = JSON.parse(await readFile(join(repository, matrix.sourceManifest), 'utf8'))
const project = upgrade ? 'CertificationApp' : 'ConsumerApp'
const realmName = upgrade ? 'certification' : 'ngb-app'
const email = 'administrator@certification.test'
const administratorCode = upgrade ? 'certification.administrator' : 'application.administrator'
const secrets = Object.fromEntries(['postgres', 'administrator', 'client', 'harness', 'minioRoot', 'minioApp'].map(name => [name, randomBytes(32).toString('base64url')]))
const processes = new Set()
const evidence = { schemaVersion: 1, profile, source: upgrade ? sourceVersion : null, target: targetVersion, registryOnly, assertions: {}, status: 'running' }
let browser
let page
let infrastructureStarted = false
let mcpEnabled = false

function run(command, args, cwd = application, env = {}) {
  return execFileSync(command, args, { cwd, env: { ...process.env, ...env }, stdio: 'inherit' })
}
function start(command, args, cwd, env, log) {
  const output = createWriteStream(join(root, log))
  const child = spawn(command, args, { cwd, env: { ...process.env, ...env }, stdio: ['ignore', 'pipe', 'pipe'] })
  child.stdout.pipe(output)
  child.stderr.pipe(output)
  const exited = new Promise(resolveExit => child.once('exit', (code, signal) => {
    output.end()
    resolveExit({ code, signal })
  }))
  const processState = { child, exited }
  processes.add(processState)
  return processState
}
async function stop(processState) {
  processState.child.kill('SIGTERM')
  const timer = setTimeout(() => processState.child.kill('SIGKILL'), 30_000)
  await processState.exited
  clearTimeout(timer)
  processes.delete(processState)
}
async function ready(url) {
  for (let attempt = 0; attempt < 90; attempt += 1) {
    try {
      const response = await fetch(url, { signal: AbortSignal.timeout(2000) })
      if (response.ok) return
    } catch { /* A starting dependency may not accept connections yet. */ }
    await new Promise(resolveWait => setTimeout(resolveWait, 1000))
  }
  throw new Error(`Readiness deadline exceeded: ${url}`)
}
const composeArguments = [
  'compose', '--project-name', `ngb-cert-${profile}`,
  '--env-file', join(infrastructure, '.env'), '--file', join(infrastructure, 'compose.yaml'),
]
const compose = args => run('docker', [...composeArguments, ...args], infrastructure)
const connectionString = `Host=localhost;Port=5183;Database=certification;Username=postgres;Password=${secrets.postgres}`
function hostEnvironment(host) {
  return {
    NGB_CONNECTION_STRING: connectionString,
    ConnectionStrings__DefaultConnection: connectionString,
    KeycloakSettings__Issuer: `http://localhost:5180/realms/${realmName}`,
    KeycloakSettings__RequireHttpsMetadata: 'false',
    KeycloakSettings__ClientIds__0: upgrade ? 'certification-web' : 'ngb-web',
    KeycloakSettings__ClientIds__1: 'certification-harness',
    KeycloakAdminClientSettings__BaseUrl: 'http://localhost:5180',
    KeycloakAdminClientSettings__Realm: realmName,
    KeycloakAdminClientSettings__ClientId: upgrade ? 'certification-tests' : 'ngb-admin-client',
    KeycloakAdminClientSettings__ClientSecret: secrets.client,
    FeatureManagement__Notes: String(notes),
    FeatureManagement__Attachments: String(attachments),
    Attachments__MinIO__InternalEndpoint: 'http://localhost:5185',
    Attachments__MinIO__PublicEndpoint: 'http://localhost:5185',
    Attachments__MinIO__Bucket: 'certification',
    Attachments__MinIO__AccessKey: 'certification-app',
    Attachments__MinIO__SecretKey: secrets.minioApp,
    Attachments__MinIO__AllowInsecureHttp: 'true',
    ASPNETCORE_URLS: host === 'BackgroundJobs' ? 'http://127.0.0.1:5184' : 'http://127.0.0.1:5181',
    ...(mcpEnabled ? {
      FeatureManagement__Mcp: 'true',
      Mcp__Resource: 'http://localhost:5181/mcp',
      Mcp__AllowLocalhostHttp: 'true',
    } : {}),
  }
}
function dotnetHost(host, args = [], live = false) {
  const directory = join(application, `${project}.${host}`)
  const dll = join(directory, 'bin/Release/net10.0', `${project}.${host}.dll`)
  return live ? start('dotnet', [dll, ...args], directory, hostEnvironment(host), `${host}.log`)
    : run('dotnet', [dll, ...args], directory, hostEnvironment(host))
}
async function token(username = email, password = secrets.administrator) {
  const response = await fetch(`http://localhost:5180/realms/${realmName}/protocol/openid-connect/token`, {
    method: 'POST', body: new URLSearchParams({ grant_type: 'password', client_id: 'certification-harness', client_secret: secrets.harness, username, password }),
  })
  assert.equal(response.status, 200, 'Real Keycloak credentials must authenticate.')
  return (await response.json()).access_token
}
async function candidateConfiguration() {
  if (registryOnly) {
    await cp(join(repository, 'NuGet.Registry.Config'), join(application, 'NuGet.Config'))
    return
  }
  await writeFile(join(application, 'NuGet.Config'), `<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear /><add key="candidate" value="${feed}" /><add key="registry" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping><clear /><packageSource key="candidate"><package pattern="NGB.Platform.*" /></packageSource><packageSource key="registry"><package pattern="*" /></packageSource></packageSourceMapping>
  <fallbackPackageFolders><clear /></fallbackPackageFolders>
</configuration>\n`)
}
async function verifyLayers() {
  const projects = []
  for (const name of await readdir(application)) {
    if (!name.startsWith(`${project}.`) || name.endsWith('.slnx')) continue
    const text = await readFile(join(application, name, `${name}.csproj`), 'utf8')
    projects.push({
      name,
      projects: [...text.matchAll(/ProjectReference\s+Include="[^"]*\/([^/]+)\.csproj"/g)].map(match => match[1]),
      packages: [...text.matchAll(/PackageReference\s+Include="([^"]+)"/g)].map(match => match[1]),
    })
  }
  assertLayering(projects)
  evidence.assertions.layering = 'passed'
}

function persistentInfrastructure() {
  const ids = execFileSync('docker', ['compose', '--project-name', `ngb-cert-${profile}`, '--file', join(infrastructure, 'compose.yaml'), 'ps', '--quiet'], { cwd: infrastructure, encoding: 'utf8' }).trim().split('\n')
  const containers = JSON.parse(execFileSync('docker', ['inspect', ...ids], { encoding: 'utf8' }))
  return containers.map(container => ({
    id: container.Id, service: container.Config.Labels['com.docker.compose.service'],
    image: container.Image,
    volumes: container.Mounts.filter(mount => mount.Type === 'volume').map(mount => mount.Name).sort(),
  })).sort((left, right) => left.service.localeCompare(right.service))
}

async function build(version) {
  await verifyLayers()
  const cache = join(root, `nuget-${version}`)
  const downloads = dependencyCache(root, version, registryOnly || profile === 'clean-starter')
  run('dotnet', ['restore', `${project}.slnx`, '--configfile', 'NuGet.Config', '--packages', cache,
    ...(version === sourceVersion || (!extension && !upgrade) ? ['--locked-mode'] : ['--force-evaluate']), '--verbosity', 'minimal'], application,
    { NUGET_HTTP_CACHE_PATH: downloads.nugetHttp })
  run('dotnet', ['build', `${project}.slnx`, '--no-restore', '-c', 'Release', '-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false', '--verbosity', 'minimal'])
  const npmCache = downloads.npm
  if (version === targetVersion && !registryOnly) run('npm', ['cache', 'add', join(feed, `ngbplatform-ui-${targetVersion}.tgz`), '--cache', npmCache])
  run('npm', ['ci', '--workspaces=false', '--cache', npmCache, '--registry', 'https://registry.npmjs.org'], join(application, 'web'))
  run('npm', ['run', 'build'], join(application, 'web'))
  for (const name of await readdir(application)) {
    if (!name.startsWith(`${project}.`) || name.endsWith('.slnx')) continue
    const assets = JSON.parse(await readFile(join(application, name, 'obj/project.assets.json'), 'utf8'))
    for (const dependency of Object.keys(assets.libraries).filter(name => name.startsWith('NGB.Platform.'))) {
      assert.ok(dependency.endsWith(`/${version}`), `Unexpected package resolved: ${dependency}`)
    }
  }
}
async function addExtension() {
  for (const module of ['Definitions', 'Runtime', 'PostgreSql', 'Probe']) {
    const destination = join(application, `${project}.${module}`)
    await cp(join(source, `CertificationApp.${module}`), destination, { recursive: true })
    async function substitute(directory) {
      for (const entry of await readdir(directory, { withFileTypes: true })) {
        const filename = join(directory, entry.name)
        if (entry.isDirectory()) await substitute(filename)
        else if (entry.name.endsWith('.cs') || entry.name.endsWith('.csproj')) {
          const text = (await readFile(filename, 'utf8')).replaceAll('CertificationApp', project)
          const target = filename.replaceAll('CertificationApp', project)
          await writeFile(target, text)
          if (target !== filename) await import('node:fs/promises').then(fs => fs.unlink(filename))
        }
      }
    }
    await substitute(destination)
  }
  const migration = join(application, `${project}.PostgreSql/db/migrations/V2026_10_06_0100__certification_checkpoint.sql`)
  await writeFile(migration, (await readFile(migration, 'utf8')).split('INSERT INTO platform_roles')[0])
  const solution = join(application, `${project}.slnx`)
  let text = await readFile(solution, 'utf8')
  text = text.replace('</Solution>', ['Definitions', 'Runtime', 'PostgreSql', 'Probe'].map(module => `  <Project Path="${project}.${module}/${project}.${module}.csproj" />`).join('\n') + '\n</Solution>')
  await writeFile(solution, text)
  for (const host of ['Api', 'BackgroundJobs', 'Migrator']) {
    const filename = join(application, `${project}.${host}`, `${project}.${host}.csproj`)
    let projectFile = await readFile(filename, 'utf8')
    const modules = host === 'Migrator' ? ['PostgreSql'] : ['Definitions', 'Runtime', 'PostgreSql']
    projectFile = projectFile.replace('</Project>', `    <ItemGroup>\n${modules.map(module => `        <ProjectReference Include="../${project}.${module}/${project}.${module}.csproj" />`).join('\n')}\n    </ItemGroup>\n</Project>`)
    if (host !== 'Migrator') projectFile = projectFile.replace('</Project>', '    <ItemGroup>\n        <PackageReference Include="NGB.Platform.Definitions" Version="[$(NgbPlatformVersion)]" />\n    </ItemGroup>\n</Project>')
    await writeFile(filename, projectFile)
    const programFile = join(application, `${project}.${host}`, 'Program.cs')
    let program = await readFile(programFile, 'utf8')
    if (host === 'Migrator') {
      program = `using ${project}.PostgreSql;\n` + program
      program = program.replace('if (args is not', '_ = typeof(CheckpointMigrationPack).Assembly;\n\nif (args is not')
    } else {
      const fixtureProgram = await readFile(join(source, `CertificationApp.${host}/Program.cs`), 'utf8')
      const registrations = fixtureProgram.slice(fixtureProgram.indexOf('builder.Services.AddSingleton<IDefinitionsContributor'), fixtureProgram.indexOf('var app ='))
      program = `using NGB.Definitions;\nusing NGB.Definitions.Catalogs.Validation;\nusing ${project}.Definitions;\nusing ${project}.Runtime;\nusing ${project}.PostgreSql;\n` + program
      program = program.replace('var app =', registrations + 'var app =')
    }
    await writeFile(programFile, program)
  }
}

async function verifySourceCopy(upgraded = false) {
  const patch = {}
  for (const [path, expected] of Object.entries(sourceManifest.files)) {
    const actual = createHash('sha256').update(await readFile(join(application, path))).digest('hex')
    if (actual === expected) continue
    const allowed = path === 'Directory.Build.props' || path === 'NuGet.Config' || path === '.gitignore' || path.endsWith('packages.lock.json')
      || path === 'web/package.json' || path === 'web/package-lock.json'
    assert.ok(upgraded && allowed, `Unexpected source fixture change: ${path}`)
    if (path === '.gitignore') {
      assert.equal(await readFile(join(application, path), 'utf8'), withManagedIgnores(await readFile(join(source, path), 'utf8')))
    }
    patch[path] = { sourceSha256: expected, targetSha256: actual }
  }
  if (upgraded) {
    evidence.patch = patch
    evidence.patchSha256 = createHash('sha256').update(JSON.stringify(patch)).digest('hex')
  }
}

async function browserLogin(username, password) {
  browser = await chromium.launch({ headless: true })
  page = await browser.newPage()
  await page.goto('http://localhost:5182')
  await page.locator('input[name="username"]').fill(username)
  await page.locator('input[name="password"]').fill(password)
  await page.locator('button[type="submit"], input[type="submit"]').first().click()
  await expect(page.getByTestId('site-shell')).toBeVisible({ timeout: 30_000 })
}
async function verifyAdministratorContract(api, state, content) {
  const roles = await api('security/roles')
  const registered = roles.find(role => role.code === administratorCode)
  const password = randomBytes(32).toString('base64url')
  const roleEmail = 'role-administrator@certification.test'
  const roleUser = await api('security/users', 'POST', {
    email: roleEmail, firstName: 'Registered', lastName: 'Administrator', displayName: 'Registered Administrator',
    enabled: true, temporaryPassword: password, requirePasswordUpdate: false, roleIds: [registered.roleId],
  })
  const roleToken = await token(roleEmail, password)
  const roleApi = authenticatedApi('http://localhost:5181', roleToken)
  const access = await roleApi('security/me/access')
  assert.equal(access.isBootstrapAdmin, false)
  assert.equal(access.isActive, true)
  const definitions = await roleApi('security/permissions/definitions')
  const key = value => `${value.resourceKind}:${value.resourceCode}:${value.actionCode}`
  assert.deepEqual(new Set(access.permissions.map(key)), new Set(definitions.map(key)))
  await roleApi('security/roles')
  await browserLogin(roleEmail, password)
  await expect(page.getByRole('button', { name: 'Create', exact: true })).toBeEnabled()
  if (content) {
    await createContentState(roleApi, state.checkpoint, attachments)
  }
  await roleApi(`security/users/${state.user.userId}/deactivate`, 'POST', undefined, 204)
  await api('security/roles', 'GET', undefined, 403)
  await roleApi(`security/users/${state.user.userId}/reactivate`, 'POST', undefined, 204)
  await api(`security/users/${roleUser.userId}/deactivate`, 'POST', undefined, 204)
  assert.equal((await roleApi('security/me/access')).isActive, false)
  await roleApi('security/roles', 'GET', undefined, 403)
  await page.reload()
  await expect(page.locator('input[name="username"]')).toBeVisible()
  await expect(page.getByTestId('site-shell')).toHaveCount(0)
  await browser.close()
  browser = null
  const fakeRole = await api('security/roles', 'POST', {
    code: 'certification.display-name-only', name: 'Administrator', description: 'A name is not an authority.', permissions: [],
  })
  const fakeEmail = 'ordinary-user@certification.test'
  await api('security/users', 'POST', {
    email: fakeEmail, firstName: 'Ordinary', lastName: 'User', displayName: 'Administrator',
    enabled: true, temporaryPassword: password, requirePasswordUpdate: false, roleIds: [fakeRole.roleId],
  })
  const ordinary = authenticatedApi('http://localhost:5181', await token(fakeEmail, password))
  assert.deepEqual((await ordinary('security/me/access')).permissions, [])
  await ordinary('security/roles', 'GET', undefined, 403)
  if (content) {
    const query = new URLSearchParams({ kind: 'CatalogItem', typeCode: 'certification.checkpoint', objectId: state.checkpoint.id })
    await ordinary(`notes?${query}`, 'GET', undefined, 403)
    await ordinary('notes', 'POST', { target: content.target, text: 'Forbidden' }, 403)
    await ordinary(`attachments?${query}`, 'GET', undefined, attachments ? 403 : 404)
    if (attachments) await ordinary(`attachments/${content.attachment.id}/download`, 'POST', undefined, 403)
    evidence.assertions.contentAuthorization = 'passed'
  }
  await browserLogin(fakeEmail, password)
  await expect(page.getByText('Access denied', { exact: true })).toBeVisible()
  await browser.close()
  browser = null
  evidence.assertions.administratorContract = 'passed'
}

async function prepareRuntimeProbe() {
  const directory = join(root, 'runtime-probe')
  await cp(join(repository, 'quality/external-consumer/runtime-probe'), directory, { recursive: true })
  await writeFile(join(directory, 'Directory.Build.props'), `<Project><PropertyGroup><NgbPlatformVersion>${targetVersion}</NgbPlatformVersion></PropertyGroup></Project>\n`)
  await writeFile(join(directory, 'Directory.Build.targets'), '<Project />\n')
  const config = `<?xml version="1.0"?><configuration><packageSources><clear /><add key="candidate" value="${feed}" /><add key="registry" value="https://api.nuget.org/v3/index.json" /></packageSources><packageSourceMapping><clear /><packageSource key="candidate"><package pattern="NGB.Platform.*" /></packageSource><packageSource key="registry"><package pattern="*" /></packageSource></packageSourceMapping><fallbackPackageFolders><clear /></fallbackPackageFolders></configuration>`
  if (registryOnly) await cp(join(repository, 'NuGet.Registry.Config'), join(directory, 'NuGet.Config'))
  else await writeFile(join(directory, 'NuGet.Config'), config)
  run('dotnet', ['restore', '--configfile', 'NuGet.Config', '--packages', join(root, 'probe-cache')], directory)
  run('dotnet', ['build', '--no-restore', '-c', 'Release', '-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false'], directory)
}
function runtimeProbe(test, state) {
  run('dotnet', ['test', '--no-restore', '--no-build', '-c', 'Release', '--filter', `FullyQualifiedName~${test}`], join(root, 'runtime-probe'), {
    NGB_CONNECTION_STRING: connectionString, NGB_PROBE_STATE: state,
  })
}
async function assertPersistentSnapshot(before, after, historyOnly, assertion) {
  const sourceState = JSON.parse(await readFile(join(root, before), 'utf8'))
  const targetState = JSON.parse(await readFile(join(root, after), 'utf8'))
  const select = state => Object.fromEntries(Object.entries(state).filter(([name]) => !historyOnly || name.startsWith('migration_changelog__')))
  assert.deepEqual(select(targetState), select(sourceState), 'Persistent state or migration history changed unexpectedly.')
  evidence.assertions[assertion ?? (historyOnly ? 'migrationHistoryPreserved' : 'repeatedMigrationSnapshot')] = createHash('sha256').update(JSON.stringify(select(targetState))).digest('hex')
}

async function verifyMcpUpgrade(state, content) {
  const directory = join(application, `${project}.Api`)
  const projectFile = join(directory, `${project}.Api.csproj`)
  const projectText = await readFile(projectFile, 'utf8')
  assert.ok(!projectText.includes('NGB.Platform.Mcp.AspNetCore'), 'The frozen 3.2 consumer must not contain MCP.')
  await writeFile(projectFile, projectText.replace('</Project>',
    '    <ItemGroup>\n        <PackageReference Include="NGB.Platform.Mcp.AspNetCore" Version="$(NgbPlatformVersion)" />\n    </ItemGroup>\n\n</Project>'))
  const program = join(directory, 'Program.cs')
  const programText = await readFile(program, 'utf8')
  assert.ok(programText.includes('var app = builder.Build();') && programText.includes('app.MapControllers();'))
  await writeFile(program, `using NGB.Mcp.AspNetCore;\n${programText}`
    .replace('var app = builder.Build();', 'builder.Services.AddNgbMcp(builder.Configuration);\n\nvar app = builder.Build();')
    .replace('app.UseAuthentication();', 'app.UseCors();\napp.UseAuthentication();')
    .replace('app.MapControllers();', 'app.MapControllers();\napp.MapNgbMcp();'))
  const relativeProject = `${project}.Api/${project}.Api.csproj`
  run('dotnet', ['restore', relativeProject, '--configfile', 'NuGet.Config',
    '--packages', join(root, `nuget-${targetVersion}`), '--force-evaluate'])
  run('dotnet', ['build', relativeProject, '--no-restore', '--no-incremental', '-c', 'Release',
    '-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false'])
  mcpEnabled = true
  const host = dotnetHost('Api', [], true)
  await ready('http://localhost:5181/health')

  const verifier = randomBytes(32).toString('base64url')
  const oauthState = randomBytes(24).toString('base64url')
  const redirect = 'http://127.0.0.1:5182/mcp-callback'
  const issuer = `http://localhost:5180/realms/${realmName}`
  const receiver = createServer((request, response) => {
    response.writeHead(request.url.startsWith('/mcp-callback?') ? 200 : 404, {
      'Content-Type': 'text/plain', 'Cache-Control': 'no-store',
    })
    response.end('Authorization complete.')
  })
  let callback
  try {
    await new Promise((resolveListen, reject) => receiver.once('error', reject).listen(5182, '127.0.0.1', resolveListen))
    browser = await chromium.launch({ headless: true })
    page = await browser.newPage()
    await page.goto(`${issuer}/protocol/openid-connect/auth?${new URLSearchParams({
      client_id: 'certification-mcp', response_type: 'code', scope: 'openid', redirect_uri: redirect,
      state: oauthState, code_challenge: createHash('sha256').update(verifier).digest('base64url'), code_challenge_method: 'S256',
    })}`)
    await page.locator('input[name="username"]').fill(email)
    await page.locator('input[name="password"]').fill(secrets.administrator)
    await page.locator('button[type="submit"], input[type="submit"]').first().click()
    await page.waitForURL(`${redirect}**`)
    callback = new URL(page.url())
  } finally {
    receiver.closeAllConnections()
    await new Promise(resolveClose => receiver.close(resolveClose))
  }
  assert.equal(callback.searchParams.get('state'), oauthState)
  assert.ok(callback.searchParams.get('code'))
  const response = await fetch(`${issuer}/protocol/openid-connect/token`, {
    method: 'POST', signal: AbortSignal.timeout(30_000), body: new URLSearchParams({
      grant_type: 'authorization_code', client_id: 'certification-mcp', redirect_uri: redirect,
      code: callback.searchParams.get('code'), code_verifier: verifier,
    }),
  })
  assert.equal(response.status, 200)
  const accessToken = (await response.json()).access_token
  await browser.close()
  browser = null
  let requestId = 0
  const rpc = async (method, params) => {
    const result = await fetch('http://localhost:5181/mcp', {
      method: 'POST', signal: AbortSignal.timeout(30_000),
      headers: { Authorization: `Bearer ${accessToken}`, 'Content-Type': 'application/json',
        Accept: 'application/json, text/event-stream', 'MCP-Protocol-Version': '2025-11-25' },
      body: JSON.stringify({ jsonrpc: '2.0', id: ++requestId, method, params }),
    })
    assert.equal(result.status, 200)
    const text = await result.text()
    const messages = result.headers.get('content-type').includes('text/event-stream')
      ? text.split('\n').filter(line => line.startsWith('data:')).map(line => JSON.parse(line.slice(5)))
      : [JSON.parse(text)]
    const message = messages.find(item => item.id === requestId)
    assert.ok(message?.result, 'The upgraded MCP host must return a protocol result.')
    assert.notEqual(message.result.isError, true)
    return message.result
  }
  const call = async (name, args) => (await rpc('tools/call', { name, arguments: args })).structuredContent.data
  runtimeProbe('SnapshotPersistentApplicationState', join(root, 'before-mcp.json'))
  const tools = await rpc('tools/list', {})
  assert.ok(tools.tools.some(tool => tool.name === 'ngb_get_record'))
  assert.ok(tools.tools.every(tool => !tool.name.includes('register')))
  const record = await call('ngb_get_record', { kind: 'catalog', typeCode: 'certification.checkpoint', id: state.checkpoint.id })
  assert.equal(record.catalog.id, state.checkpoint.id)
  assert.equal((await call('ngb_get_capabilities', {})).version, targetVersion)
  const target = { kind: 'catalogItem', typeCode: 'certification.checkpoint', id: state.checkpoint.id }
  if (content) {
    const notes = await call('ngb_list_notes', { target })
    assert.ok(notes.items.length > 0)
    if (attachments) assert.ok((await call('ngb_list_attachments', { target })).items.length > 0)
  }
  runtimeProbe('SnapshotPersistentApplicationState', join(root, 'after-mcp.json'))
  await assertPersistentSnapshot('before-mcp.json', 'after-mcp.json', false, 'mcpReadOnlySnapshot')
  await stop(host)
  mcpEnabled = false
  evidence.assertions.mcpOptInAfterUpgrade = 'passed'
}

try {
  await verifyFrozenFixture(source, sourceManifest)
  try {
    const manifest = await readFile(join(artifacts, 'release-manifest.json'))
    evidence.artifactManifestSha256 = createHash('sha256').update(manifest).digest('hex')
  } catch (error) {
    if (error.code !== 'ENOENT') throw error
    evidence.artifactManifestSha256 = null
  }
  await mkdir(feed)
  await mkdir(infrastructure)
  if (!registryOnly) {
    for (const name of await readdir(join(artifacts, 'nuget-release'))) {
      if (name.endsWith('.nupkg')) await cp(join(artifacts, 'nuget-release', name), join(feed, name))
    }
    await cp(join(artifacts, 'npm', `ngbplatform-ui-${targetVersion}.tgz`), join(feed, `ngbplatform-ui-${targetVersion}.tgz`))
  }
  if (upgrade) await cp(source, application, { recursive: true })
  else {
    if (registryOnly) {
      await cp(join(repository, 'NuGet.Registry.Config'), join(root, 'NuGet.Config'))
      run('dotnet', ['new', 'install', `NGB.Platform.Templates::${targetVersion}`, '--nuget-source', 'https://api.nuget.org/v3/index.json', '--debug:custom-hive', join(root, 'hive')], root, {
        NUGET_PACKAGES: join(root, 'template-cache'), NUGET_HTTP_CACHE_PATH: join(root, 'template-http-cache'),
      })
    } else {
      run('dotnet', ['new', 'install', join(feed, `NGB.Platform.Templates.${targetVersion}.nupkg`), '--debug:custom-hive', join(root, 'hive')], root)
    }
    run('dotnet', ['new', 'ngb', '-n', project, '-o', application, '--debug:custom-hive', join(root, 'hive')], root)
    if (extension) await addExtension()
    await candidateConfiguration()
  }
  await assertConsumerIsolation(application, repository)
  evidence.assertions.isolation = 'passed'
  await build(upgrade ? sourceVersion : targetVersion)
  if (upgrade) await verifySourceCopy()
  const realm = JSON.parse(await readFile(join(application, 'infrastructure/realm.json'), 'utf8'))
  if (upgrade && targetVersion === '3.3.0') {
    realm.clients.push({
      clientId: 'certification-mcp', protocol: 'openid-connect', enabled: true, publicClient: true,
      standardFlowEnabled: true, directAccessGrantsEnabled: false, serviceAccountsEnabled: false,
      redirectUris: ['http://127.0.0.1:5182/mcp-callback'], attributes: { 'pkce.code.challenge.method': 'S256' },
      protocolMappers: [{ name: 'MCP resource', protocol: 'openid-connect', protocolMapper: 'oidc-audience-mapper',
        config: { 'included.custom.audience': 'http://localhost:5181/mcp', 'access.token.claim': 'true', 'id.token.claim': 'false' } }],
    })
  }
  await writeInfrastructureConfiguration(infrastructure, realm, email, secrets)
  let composeFile = await readFile(join(source, 'infrastructure/compose.yaml'), 'utf8')
  composeFile = composeFile.replace(/image: postgres:[^\n]+/, `image: ${matrix.infrastructure.postgres}`)
    .replace(/image: quay.io\/keycloak\/keycloak:[^\n]+/, `image: ${matrix.infrastructure.keycloak}`)
  composeFile = composeFile.replace(/^(      KEYCLOAK_ADMIN_CLIENT_SECRET:.*)$/m,
    '$1\n      CERTIFICATION_HARNESS_SECRET: ${CERTIFICATION_HARNESS_SECRET:?Set a private harness secret}')
  if (attachments) {
    const image = matrix.infrastructure.minio
    composeFile = composeFile.replace('volumes:\n  postgres-data:', `  minio:
    image: ${image}
    command: ["server", "/data", "--console-address", ":9001"]
    environment:
      MINIO_ROOT_USER: certification-root
      MINIO_ROOT_PASSWORD: \${MINIO_ROOT_PASSWORD}
      MINIO_ENDPOINT: http://localhost:9000
      MINIO_BUCKET: certification
      MINIO_APP_ACCESS_KEY: certification-app
      MINIO_APP_SECRET_KEY: \${MINIO_APP_SECRET_KEY}
      MINIO_API_CORS_ALLOW_ORIGIN: http://localhost:5182
    ports:
      - "127.0.0.1:5185:9000"
    volumes:
      - minio-data:/data
      - ./minio-init.sh:/tmp/minio-init.sh:ro
volumes:
  minio-data:
  postgres-data:`)
    await cp(join(repository, 'docker/minio/init.sh'), join(infrastructure, 'minio-init.sh'))
  }
  await writeFile(join(infrastructure, 'compose.yaml'), composeFile)
  infrastructureStarted = true
  // A process killed before finally may leave this disposable profile's volumes.
  compose(['down', '--volumes', '--remove-orphans'])
  compose(['up', '-d'])
  await ready(`http://localhost:5180/realms/${realmName}/.well-known/openid-configuration`)
  if (attachments) {
    await ready('http://localhost:5185/minio/health/ready')
    compose(['exec', '-T', 'minio', 'sh', '/tmp/minio-init.sh'])
  }
  evidence.infrastructure = persistentInfrastructure()
  assert.equal(evidence.infrastructure.some(item => item.service === 'minio'), attachments)
  dotnetHost('Migrator')
  if (!upgrade) dotnetHost('Migrator', ['seed-administrator'])
  await prepareRuntimeProbe()
  const platformJob = join(root, 'platform-job.txt')
  runtimeProbe('EnqueueRegisteredPlatformJob', platformJob)
  let apiProcess = dotnetHost('Api', [], true)
  let jobsProcess = dotnetHost('BackgroundJobs', [], true)
  await ready('http://localhost:5181/health')
  await ready('http://localhost:5184/health')
  runtimeProbe('RegisteredPlatformJobSucceedsThroughWorker', platformJob)
  evidence.assertions.registeredPlatformJob = 'passed'
  let webProcess = start(process.execPath, ['node_modules/vite/bin/vite.js', 'preview', '--host', '127.0.0.1', '--port', '5182', '--strictPort'], join(application, 'web'), {}, 'web.log')
  await ready('http://localhost:5182')
  const api = authenticatedApi('http://localhost:5181', await token())
  const state = await createCoreState(api, administratorCode, email, extension)
  const content = notes ? await createContentState(api, state.checkpoint, attachments) : null
  if (!notes) await api('notes?kind=CatalogItem&typeCode=unused&objectId=00000000-0000-0000-0000-000000000001', 'GET', undefined, 404)
  if (!attachments) await api('attachments?kind=CatalogItem&typeCode=unused&objectId=00000000-0000-0000-0000-000000000001', 'GET', undefined, 404)
  browser = await chromium.launch({ headless: true })
  page = await browser.newPage()
  await page.goto('http://localhost:5182')
  await page.locator('input[name="username"]').fill(email)
  await page.locator('input[name="password"]').fill(secrets.administrator)
  await page.locator('button[type="submit"], input[type="submit"]').first().click()
  await expect(page.getByTestId('site-shell')).toBeVisible({ timeout: 30_000 })
  if (upgrade) {
    await page.getByRole('button', { name: 'Create', exact: true }).click()
  } else {
    await page.goto('http://localhost:5182/admin/security/roles/new')
  }
  const field = label => page.locator('label').filter({ hasText: new RegExp(`^\\s*${label}\\s*$`) }).locator('..').locator('input')
  await field('Code').fill(`browser.role.${randomBytes(4).toString('hex')}`)
  await field('Name').fill('Browser role')
  const createdRoleResponse = page.waitForResponse(response => response.url().endsWith('/api/security/roles') && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  const browserRole = await (await createdRoleResponse).json()
  await page.goto(`http://localhost:5182/admin/security/roles/${browserRole.roleId}`)
  await expect(field('Name')).toHaveValue('Browser role')
  await field('Name').fill('Browser role edited')
  const editedRoleResponse = page.waitForResponse(response => response.url().endsWith(`/api/security/roles/${browserRole.roleId}`) && response.request().method() === 'PUT')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  assert.equal((await editedRoleResponse).status(), 200)
  const auditResponse = page.waitForResponse(response => response.url().includes(`/api/audit/entities/9/${browserRole.roleId}`))
  await page.getByTitle('Audit log', { exact: true }).click()
  assert.ok((await (await auditResponse).json()).items.length >= 2)
  evidence.assertions.realBrowserRoleLifecycle = 'passed'
  await browser.close()
  browser = null
  await verifyAdministratorContract(api, state, content)
  state.user = await api(`security/users/${state.user.userId}`)
  evidence.assertions.sourceRuntime = 'passed'
  if (upgrade) {
    await stop(webProcess)
    await stop(apiProcess)
    await stop(jobsProcess)
    runtimeProbe('SnapshotPersistentApplicationState', join(root, 'source-state.json'))
    const pending = join(root, 'pending-jobs.json')
    dotnetHost('Probe', ['enqueue', pending])
    run('node', [join(repository, 'ngb.mjs'), 'upgrade', '--app', application,
      '--to', targetVersion, '--packages', artifacts, '--apply'])
    await candidateConfiguration()
    await build(targetVersion)
    await verifySourceCopy(true)
    await startAfterSuccessfulMigration(async () => {
      dotnetHost('Migrator')
      runtimeProbe('SnapshotPersistentApplicationState', join(root, 'target-state.json'))
      await assertPersistentSnapshot('source-state.json', 'target-state.json', true)
    }, async () => {
      apiProcess = dotnetHost('Api', [], true)
      jobsProcess = dotnetHost('BackgroundJobs', [], true)
    })
    await ready('http://localhost:5181/health')
    const targetApi = authenticatedApi('http://localhost:5181', await token())
    assert.deepEqual(persistentInfrastructure(), evidence.infrastructure, 'Upgrade must retain the original infrastructure containers and volumes.')
    await verifyCorePreservation(targetApi, state)
    if (content) await verifyContentPreservation(targetApi, content)
    await verifyCoreContinuation(targetApi, state)
    webProcess = start(process.execPath, ['node_modules/vite/bin/vite.js', 'preview', '--host', '127.0.0.1', '--port', '5182', '--strictPort'], join(application, 'web'), {}, 'target-web.log')
    await ready('http://localhost:5182')
    await browserLogin(email, secrets.administrator)
    await expect(page.getByText('Reader after upgrade', { exact: true })).toBeVisible()
    await browser.close()
    browser = null
    await stop(webProcess)
    evidence.assertions.targetBrowserSmoke = 'passed'
    if (content) {
      await verifyContentContinuation(targetApi, content)
      evidence.assertions.contentPreserved = 'passed'
    }
    await new Promise(resolveWait => setTimeout(resolveWait, 16_000))
    dotnetHost('Probe', ['verify', pending])
    evidence.assertions.pendingJobAndIdempotentEffect = 'passed'
    await stop(apiProcess)
    await stop(jobsProcess)
    runtimeProbe('SnapshotPersistentApplicationState', join(root, 'before-repeat.json'))
    dotnetHost('Migrator')
    runtimeProbe('SnapshotPersistentApplicationState', join(root, 'after-repeat.json'))
    await assertPersistentSnapshot('before-repeat.json', 'after-repeat.json', false)
    evidence.assertions.repeatedMigrationPreservesState = 'passed'
    evidence.assertions.preservationAndContinuation = 'passed'
    if (targetVersion === '3.3.0') await verifyMcpUpgrade(state, content)
  }
  if (!upgrade) {
    if (extension) {
      const checkpoint = join(root, 'extension-jobs.json')
      dotnetHost('Probe', ['enqueue', checkpoint])
      await new Promise(resolveWait => setTimeout(resolveWait, 16_000))
      dotnetHost('Probe', ['verify', checkpoint])
      evidence.assertions.customHandlerAndProvider = 'passed'
    }
    await stop(apiProcess)
    await stop(jobsProcess)
    runtimeProbe('SnapshotPersistentApplicationState', join(root, 'before-repeat.json'))
    dotnetHost('Migrator')
    dotnetHost('Migrator', ['seed-administrator'])
    runtimeProbe('SnapshotPersistentApplicationState', join(root, 'after-repeat.json'))
    await assertPersistentSnapshot('before-repeat.json', 'after-repeat.json', false)
    evidence.assertions.repeatedMigrationPreservesState = 'passed'
  }
  let writersStartedAfterFailure = false
  await assert.rejects(startAfterSuccessfulMigration(
    async () => dotnetHost('Migrator', ['--modules=certification.missing']),
    async () => { writersStartedAfterFailure = true },
  ))
  assert.equal(writersStartedAfterFailure, false)
  await assert.rejects(fetch('http://localhost:5181/health', { signal: AbortSignal.timeout(2000) }))
  await assert.rejects(fetch('http://localhost:5184/health', { signal: AbortSignal.timeout(2000) }))
  evidence.assertions.failedMigrationBlocksStartup = 'passed'
  for (const host of ['Api', 'BackgroundJobs']) {
    const log = await readFile(join(root, `${host}.log`), 'utf8')
    const events = log.trim().split('\n').filter(line => line.startsWith('{')).map(line => JSON.parse(line))
    assert.ok(events.length > 0, `Missing ${host} structured logs.`)
    for (const event of events) {
      assert.ok(event.Timestamp && event.Level && event.MessageTemplate, `Malformed ${host} log event.`)
    }
    if (host === 'Api') assert.ok(events.some(event => event.Properties?.RequestPath === '/health'))
    else assert.ok(events.some(event => event.Properties?.JobId === 'platform.schema.validate'))
  }
  evidence.assertions.healthAndLogs = 'passed'
  evidence.status = 'passed'
  evidence.state = { userId: state.user.userId, authSubject: state.user.authSubject, roleId: state.role.roleId, catalogId: state.checkpoint?.id, attachmentSha256: content?.sha256 }
} catch (error) {
  evidence.status = 'failed'
  evidence.error = error.message
  if (infrastructureStarted) {
    try {
      const directory = join(artifacts, 'certification/diagnostics', `${registryOnly ? 'registry-' : ''}${profile}`)
      evidence.infrastructureDiagnostics = await saveInfrastructureDiagnostics(directory, composeArguments, secrets)
      console.error(`Infrastructure diagnostics: ${evidence.infrastructureDiagnostics}`)
    } catch {
      console.error('Could not save infrastructure diagnostics; preserving the original certification failure.')
    }
  }
  if (page && !page.isClosed()) {
    await page.screenshot({ path: join(root, 'failure.png'), fullPage: true })
    await writeFile(join(root, 'failure.html'), await page.content())
  }
  throw error
} finally {
  if (browser) await browser.close()
  for (const processState of processes) await stop(processState)
  evidence.workDirectory = root
  await mkdir(join(artifacts, 'certification'), { recursive: true })
  await writeFile(join(artifacts, 'certification', `${registryOnly ? 'registry-' : ''}${profile}.json`), `${JSON.stringify(evidence, null, 2)}\n`)
  console.log(`${profile}: ${evidence.status}; diagnostics: ${root}`)
  if (infrastructureStarted) compose(['down', '--volumes'])
}
