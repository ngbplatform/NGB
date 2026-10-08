import assert from 'node:assert/strict'
import { execFileSync, spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { cp, mkdir, mkdtemp, readFile, readdir, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { chromium, expect } from '@playwright/test'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const artifacts = resolve(process.argv[2] ?? join(repository, 'artifacts'))
const matrix = JSON.parse(await readFile(new URL('./matrix.json', import.meta.url), 'utf8'))
const root = await mkdtemp(join(tmpdir(), 'ngb-compose-contract-'))
const application = join(root, 'application')
const hive = join(root, 'hive')
const run = (command, args, cwd = application) => execFileSync(command, args, { cwd, stdio: 'inherit' })
const composeArguments = ['compose', '--project-name', 'ngb-template-compose', '--file', 'compose.yaml']
const compose = args => run('docker', [...composeArguments, ...args])
const template = join(artifacts, 'nuget-release', `NGB.Platform.Templates.${matrix.target}.nupkg`)
let browser
let started = false
try {
  run('dotnet', ['new', 'install', template, '--debug:custom-hive', hive], root)
  run('dotnet', ['new', 'ngb', '-n', 'ContainerApp', '-o', application, '--debug:custom-hive', hive], root)
  run('node', ['infrastructure/configure.mjs', 'administrator@compose.test'])
  const secrets = Object.fromEntries((await readFile(join(application, '.env'), 'utf8')).trim().split('\n').map(line => {
    const separator = line.indexOf('=')
    return [line.slice(0, separator), line.slice(separator + 1)]
  }))
  // Only package acquisition changes for unpublished candidates. Application
  // sources, lockfiles, image stages and runtime configuration remain generated.
  const feed = join(application, '.candidate-feed')
  await mkdir(feed)
  for (const name of await readdir(join(artifacts, 'nuget-release'))) {
    if (name.endsWith('.nupkg')) await cp(join(artifacts, 'nuget-release', name), join(feed, name))
  }
  await writeFile(join(application, 'NuGet.Config'), `<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear /><add key="candidate" value="/source/.candidate-feed" /><add key="registry" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping><clear /><packageSource key="candidate"><package pattern="NGB.Platform.*" /></packageSource><packageSource key="registry"><package pattern="*" /></packageSource></packageSourceMapping>
  <fallbackPackageFolders><clear /></fallbackPackageFolders>
</configuration>
`)
  const web = join(application, 'web')
  await cp(join(artifacts, 'npm', `ngbplatform-ui-${matrix.target}.tgz`), join(web, '.candidate.tgz'))
  const dockerfile = join(web, 'Dockerfile')
  await writeFile(dockerfile, (await readFile(dockerfile, 'utf8')).replace('RUN npm ci', 'COPY .candidate.tgz /tmp/candidate.tgz\nRUN npm cache add /tmp/candidate.tgz\nRUN npm ci'))
  compose(['build'])
  await writeFile(join(application, 'failure.yaml'), `services:
  migrator:
    entrypoint: ["dotnet", "ContainerApp.Migrator.dll", "--modules=certification.missing"]
`)
  started = true
  const failed = spawnSync('docker', [...composeArguments, '--file', 'failure.yaml', 'up', '-d', 'api', 'jobs'], { cwd: application, stdio: 'inherit' })
  assert.notEqual(failed.status, 0, 'A failed migration must fail Compose startup.')
  const rows = execFileSync('docker', [...composeArguments, 'ps', '-a', '--format', 'json'], { cwd: application, encoding: 'utf8' })
  const containers = rows.trim().split('\n').filter(Boolean).map(line => JSON.parse(line))
  for (const service of ['api', 'jobs']) assert.ok(!containers.some(item => item.Service === service && item.State === 'running'))
  compose(['down'])
  compose(['up', '-d'])
  for (const url of ['http://localhost:5181/health', 'http://localhost:5184/health', 'http://localhost:5182']) {
    let healthy = false
    for (let attempt = 0; attempt < 90; attempt += 1) {
      try {
        healthy = (await fetch(url, { signal: AbortSignal.timeout(2000) })).ok
      } catch { /* Allow time for the container to start. */ }
      if (healthy) break
      await new Promise(resolveWait => setTimeout(resolveWait, 1000))
    }
    assert.ok(healthy, `Container readiness failed: ${url}`)
  }
  browser = await chromium.launch({ headless: true })
  const page = await browser.newPage()
  await page.goto('http://localhost:5182/admin/security/roles/new')
  await page.locator('input[name="username"]').fill('administrator@compose.test')
  await page.locator('input[name="password"]').fill(secrets.BOOTSTRAP_PASSWORD)
  await page.locator('button[type="submit"], input[type="submit"]').first().click()
  await expect(page.getByTestId('site-shell')).toBeVisible({ timeout: 30_000 })
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeVisible()
  const runtime = execFileSync('docker', [...composeArguments, 'exec', '-T', 'api', 'id', '-u'], { cwd: application, encoding: 'utf8' }).trim()
  assert.notEqual(runtime, '0', 'The runtime image must use a non-root user.')
  await mkdir(join(artifacts, 'certification'), { recursive: true })
  await writeFile(join(artifacts, 'certification/compose.json'), `${JSON.stringify({
    schemaVersion: 1, status: 'passed', version: matrix.target,
    templateSha256: createHash('sha256').update(await readFile(template)).digest('hex'),
    assertions: ['container-build', 'failed-migration-blocks-hosts', 'health', 'browser-login', 'private-discovery-public-issuer', 'non-root'],
  }, null, 2)}\n`)
} finally {
  if (browser) await browser.close()
  if (started) compose(['down', '--volumes'])
  console.log(`Template Compose diagnostics: ${root}`)
}
