import assert from 'node:assert/strict'
import { chmod, mkdir, mkdtemp, readFile, readdir, realpath, rm, stat, symlink, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { delimiter, join } from 'node:path'
import test from 'node:test'
import { attachTestEnvironment, dockerPluginDirectories, stageTestSources, startTestEnvironment, testCompose } from '../../packaging/templates/content/infrastructure/ngb/environment.mjs'

const config = JSON.parse(await readFile(new URL('../../packaging/templates/content/ngb.config.json', import.meta.url)))
const parameters = {
  runId: 'a6636a30-2e87-4d1a-9298-cf083b23bc78', postgresPassword: 'a'.repeat(48),
  applicationSha256: 'b'.repeat(64),
  keycloakPort: 10001, webPort: 10002, apiPort: 10003,
}

test('nested execution finds Docker plugins beside its executable without inheriting personal configuration', async t => {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'ngb-docker-location-')))
  t.after(() => rm(root, { recursive: true, force: true }))
  const name = process.platform === 'win32' ? 'docker.exe' : 'docker'
  await mkdir(join(root, 'distribution/bin'), { recursive: true })
  await mkdir(join(root, 'distribution/cli-plugins'))
  await mkdir(join(root, 'path'))
  await writeFile(join(root, 'distribution/bin', name), '')
  await symlink(join(root, 'distribution/bin', name), join(root, 'path', name))
  assert.deepEqual(await dockerPluginDirectories({ PATH: join(root, 'path'), HOME: join(root, 'empty-home') }),
    [join(root, 'distribution/cli-plugins')])
  assert.deepEqual(await dockerPluginDirectories({ PATH: join(root, 'missing') }), [])
  assert.deepEqual(await dockerPluginDirectories({ PATH: '.' }), [])
  await rm(join(root, 'distribution/cli-plugins'), { recursive: true })
  assert.deepEqual(await dockerPluginDirectories({ PATH: join(root, 'path') }), [])
  await assert.rejects(dockerPluginDirectories({ PATH: Array(129).fill(root).join(delimiter) }), /path limit/)
})

test('staging keeps container-readable sources and executable scripts while excluding deployment secrets and test outputs', async t => {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'ngb-test-source-')))
  t.after(() => rm(root, { recursive: true, force: true }))
  const source = join(root, 'source')
  const destination = join(root, 'staged')
  await mkdir(source)
  for (const name of ['.env', 'appsettings.json', 'migrate.sh']) await writeFile(join(source, name), name)
  await chmod(join(source, 'appsettings.json'), 0o644)
  await chmod(join(source, 'migrate.sh'), 0o755)
  for (const name of ['.ngb', 'coverage', 'test-results', 'node_modules']) {
    await mkdir(join(source, name))
    await writeFile(join(source, name, 'ignored'), 'Test output or cached data')
  }
  await stageTestSources(source, destination)
  assert.deepEqual((await readdir(destination)).sort(), ['appsettings.json', 'migrate.sh'])
  assert.equal(await readFile(join(destination, 'migrate.sh'), 'utf8'), 'migrate.sh')
  if (process.platform !== 'win32') {
    assert.equal((await stat(join(destination, 'appsettings.json'))).mode & 0o777, 0o644)
    assert.equal((await stat(join(destination, 'migrate.sh'))).mode & 0o777, 0o755)
  }
})

test('browser infrastructure owns every resource and binds only loopback ports', () => {
  const compose = testCompose(config, parameters)
  for (const service of Object.values(compose.services)) {
    assert.equal(service.labels['com.ngb.devkit.run'], parameters.runId)
    for (const port of service.ports ?? []) assert.match(port, /^127\.0\.0\.1:/)
  }
  assert.equal(compose.volumes['test-postgres'].labels['com.ngb.devkit.run'], parameters.runId)
  assert.equal(compose.networks.default.labels['com.ngb.devkit.run'], parameters.runId)
  assert.equal(compose.services.api.depends_on.migrator.condition, 'service_completed_successfully')
  assert.equal(compose.services.api.environment.ConnectionStrings__DefaultConnection,
    `Host=postgres;Database=ngb_test;Username=postgres;Password=${parameters.postgresPassword}`)
  assert.ok(!JSON.stringify(compose).includes('external'))
  assert.equal(compose.services.api.build.args.NUGET_CONFIG, 'NuGet.Config')
  assert.equal(testCompose(config, { ...parameters, candidate: true }).services.api.build.args.NUGET_CONFIG, 'NuGet.Local.Config')
})

test('browser handoffs reject arbitrary endpoints and cancellation precedes infrastructure access', async () => {
  const valid = { NGB_TEST_RUN_ID: parameters.runId, NGB_TEST_PASSWORD: 'a'.repeat(48), NGB_TEST_ORIGIN: 'http://127.0.0.1:10002' }
  for (const replacement of [{ NGB_TEST_RUN_ID: '../other' }, { NGB_TEST_PASSWORD: '' },
    { NGB_TEST_ORIGIN: 'https://example.com:8443' }, { NGB_TEST_ORIGIN: 'http://127.0.0.1:99999' },
    { NGB_TEST_ORIGIN: 'http://127.0.0.1:10002/path' }]) {
    await assert.rejects(attachTestEnvironment({ ...valid, ...replacement }))
  }
  await assert.rejects(startTestEnvironment('/nonexistent', { signal: AbortSignal.abort() }), { name: 'AbortError' })
})

test('resource names, ports, credentials and host roles cannot redirect test provisioning', () => {
  for (const replacement of [{ runId: '../production' }, { postgresPassword: 'insecure' },
    { webPort: 80 }, { keycloakPort: 10003 }, { apiPort: '10004' }]) {
    assert.throws(() => testCompose(config, { ...parameters, ...replacement }))
  }
  assert.throws(() => testCompose({ ...config, projects: [] }, parameters), /one migrator/)
  const invalid = structuredClone(config)
  invalid.projects[0].path = 'nested/NgbApplication.Api/NgbApplication.Api.csproj'
  assert.throws(() => testCompose(invalid, parameters), /layout/)
})
