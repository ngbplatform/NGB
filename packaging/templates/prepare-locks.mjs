import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { cp, mkdir, mkdtemp, readFile, realpath, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { createPlatformUiPackageManifest } from '../../ui/scripts/platform-ui-package-manifest.mjs'
import { applicationFiles } from './content/infrastructure/ngb/application.mjs'
import { prepareTemplate } from '../../ai-devkit/prepare.mjs'
import { validateConfiguration } from './content/infrastructure/ngb/project-data.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const feed = resolve(process.argv[2] ?? join(repository, 'artifacts/nuget-release'))
const version = process.argv[3]
assert.match(version, /^\d+\.\d+\.\d+$/)
await prepareTemplate()
const root = await realpath(await mkdtemp(join(tmpdir(), 'ngb-template-locks-')))
const content = join(repository, 'packaging/templates/content')
const templateFiles = await applicationFiles(content)
const configuration = validateConfiguration(JSON.parse(templateFiles['ngb.config.json']))
for (const [path, bytes] of Object.entries(templateFiles)) {
  await mkdir(dirname(join(root, path)), { recursive: true })
  await writeFile(join(root, path), bytes)
}
const xml = feed.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;')
await writeFile(join(root, 'NuGet.Config'), `<?xml version="1.0"?><configuration>
  <packageSources><clear /><add key="candidate" value="${xml}" /><add key="registry" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping><clear /><packageSource key="candidate"><package pattern="NGB.Platform.*" /></packageSource><packageSource key="registry"><package pattern="*" /></packageSource></packageSourceMapping>
  <fallbackPackageFolders><clear /></fallbackPackageFolders>
</configuration>\n`)
execFileSync('dotnet', ['restore', 'NgbApplication.slnx', '--force-evaluate', '--no-http-cache', '--configfile', 'NuGet.Config', '--packages', join(root, 'nuget-cache')], {
  cwd: root, stdio: 'inherit',
})
for (const { path: project } of configuration.projects) {
  const path = `${dirname(project)}/packages.lock.json`
  const lock = JSON.parse(await readFile(join(root, path), 'utf8'))
  for (const framework of Object.values(lock.dependencies)) {
    for (const [id, dependency] of Object.entries(framework)) {
      if (id.toLowerCase().startsWith('ngb.platform.')) assert.equal(dependency.resolved, version)
    }
  }
  await cp(join(root, path), join(content, path))
}
const archive = await readFile(join(repository, 'artifacts/npm', `ngbplatform-ui-${version}.tgz`))
const integrity = `sha512-${createHash('sha512').update(archive).digest('base64')}`
const sourceManifest = JSON.parse(await readFile(join(repository, 'ui/ngb-ui-framework/package.json'), 'utf8'))
assert.equal(sourceManifest.version, version)
const published = createPlatformUiPackageManifest(sourceManifest)
const consumerLocks = [
  'packaging/templates/content/web/package-lock.json',
  'ui/ngb-crm-web/package-lock.json',
  'ai-devkit/examples/equipment-inspection/web/package-lock.json',
]
for (const [index, filename] of consumerLocks.entries()) {
  const path = join(repository, filename)
  const workspace = join(root, `consumer-lock-${index}`)
  await mkdir(workspace)
  const manifest = JSON.parse(await readFile(join(dirname(path), 'package.json'), 'utf8'))
  assert.equal(manifest.dependencies['@ngbplatform/ui'], version)
  manifest.dependencies['@ngbplatform/ui'] = `file:${join(repository, 'artifacts/npm', `ngbplatform-ui-${version}.tgz`)}`
  await writeFile(join(workspace, 'package.json'), `${JSON.stringify(manifest, null, 2)}\n`)
  const previousLock = JSON.parse(await readFile(path, 'utf8'))
  previousLock.packages[''].dependencies['@ngbplatform/ui'] = manifest.dependencies['@ngbplatform/ui']
  // The candidate may add runtime dependencies while retaining the release version.
  // Removing only this node makes npm read its real manifest and preserve other pins.
  delete previousLock.packages['node_modules/@ngbplatform/ui']
  await writeFile(join(workspace, 'package-lock.json'), `${JSON.stringify(previousLock, null, 2)}\n`)
  execFileSync('npm', ['install', '--package-lock-only', '--ignore-scripts', '--workspaces=false', '--cache', join(root, 'npm-cache')], {
    cwd: workspace, stdio: 'inherit',
  })
  const lock = JSON.parse(await readFile(join(workspace, 'package-lock.json'), 'utf8'))
  lock.packages[''].dependencies['@ngbplatform/ui'] = version
  assert.equal(lock.packages[''].dependencies['@ngbplatform/ui'], version)
  const dependency = lock.packages['node_modules/@ngbplatform/ui']
  Object.assign(dependency, {
    version, resolved: `https://registry.npmjs.org/@ngbplatform/ui/-/ui-${version}.tgz`, integrity,
    dependencies: published.dependencies,
    peerDependencies: published.peerDependencies, peerDependenciesMeta: published.peerDependenciesMeta,
  })
  await writeFile(path, `${JSON.stringify(lock, null, 2)}\n`)
}
await prepareTemplate(true)
console.log(`Generated lockfiles identify the existing ${version} candidate artifacts.`)
