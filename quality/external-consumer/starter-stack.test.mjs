import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { mkdtemp, readFile, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import { initializeConfiguration } from '../../packaging/templates/content/infrastructure/configuration.mjs'

test('the actual starter Compose includes operational services and defaults to private attachments and notes', async t => {
  const root = await mkdtemp(join(tmpdir(), 'ngb-stack-config-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  const env = join(root, '.env')
  await initializeConfiguration(env, 'administrator@starter.test')
  const file = new URL('../../packaging/templates/content/compose.yaml', import.meta.url).pathname
  const parsed = JSON.parse(execFileSync('docker', ['compose', '--env-file', env, '--file', file,
    'config', '--format', 'json'], { encoding: 'utf8' }))
  const services = parsed.services
  assert.deepEqual(Object.keys(services).sort(), [
    'api', 'jobs', 'keycloak', 'migrator', 'minio', 'minio-init', 'pgadmin', 'postgres', 'seq', 'watchdog', 'web',
  ])
  assert.equal(services.api.environment.FeatureManagement__Attachments, 'true')
  assert.equal(services.api.environment.FeatureManagement__Notes, 'true')
  assert.equal(services.jobs.environment.FeatureManagement__Attachments, 'true')
  assert.equal(services.jobs.environment.FeatureManagement__Notes, 'true')
  assert.equal(services.pgadmin.environment.PGADMIN_CONFIG_ALLOW_SPECIAL_EMAIL_DOMAINS, '["test"]')
  assert.equal(services.api.environment.ExternalLinksSettings__HealthUiUrl, 'http://localhost:5185/health-ui')
  assert.equal(services.api.environment.ExternalLinksSettings__BackgroundJobsUiUrl, 'http://localhost:5184/hangfire')
  assert.equal(services.api.depends_on['minio-init'].condition, 'service_completed_successfully')
  assert.equal(services.api.depends_on.migrator.condition, 'service_completed_successfully')
  assert.equal(services.jobs.depends_on.migrator.condition, 'service_completed_successfully')
  assert.equal(services['minio-init'].depends_on.minio.condition, 'service_healthy')
  assert.notEqual(services.api.environment.Attachments__MinIO__AccessKey, services.minio.environment.MINIO_ROOT_USER)
  assert.equal(services.api.environment.Attachments__MinIO__AccessKey, services['minio-init'].environment.MINIO_APP_ACCESS_KEY)
  assert.ok(!Object.hasOwn(services.api.environment, 'MINIO_ROOT_PASSWORD'))
  for (const service of ['api', 'jobs', 'watchdog']) {
    assert.equal(services[service].environment.Serilog__WriteTo__1__Args__serverUrl, 'http://seq')
  }
  assert.equal(services.watchdog.environment.WebClient, 'http://web:8080/')
  assert.equal(services.watchdog.environment.HealthChecksUI__HealthChecks__1__Uri, 'http://api:8080/health')
  assert.equal(services.watchdog.environment.HealthChecksUI__HealthChecks__2__Uri, 'http://jobs:8080/health')
  for (const service of Object.values(services)) {
    for (const port of service.ports ?? []) {
      assert.equal(port.host_ip, '127.0.0.1')
    }
  }
  const realm = JSON.parse(await readFile(new URL('../../packaging/templates/content/infrastructure/realm.json', import.meta.url)))
  for (const [id, port] of [['ngb-jobs', 5184], ['ngb-watchdog', 5185]]) {
    const client = realm.clients.find(item => item.clientId === id)
    assert.deepEqual(client.redirectUris, [`http://localhost:${port}/*`])
    assert.equal(client.protocolMappers.find(item => item.name === 'console-realm-roles').config['id.token.claim'], 'true')
  }

  const disabled = JSON.parse(execFileSync('docker', ['compose', '--env-file', env, '--file', file,
    'config', '--format', 'json'], {
    encoding: 'utf8', env: { ...process.env, FEATURE_ATTACHMENTS: 'false', FEATURE_NOTES: 'false' },
  }))
  for (const host of ['api', 'jobs']) {
    assert.equal(disabled.services[host].environment.FeatureManagement__Attachments, 'false')
    assert.equal(disabled.services[host].environment.FeatureManagement__Notes, 'false')
  }
})
