import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { cp, mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { createPlatformUiPackageManifest } from '../../ui/scripts/platform-ui-package-manifest.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const feed = resolve(process.argv[2] ?? join(repository, 'artifacts/nuget-release'))
const version = process.argv[3]
assert.match(version, /^\d+\.\d+\.\d+$/)
const root = await mkdtemp(join(tmpdir(), 'ngb-template-locks-'))
const content = join(repository, 'packaging/templates/content')
await cp(content, root, { recursive: true })
const xml = feed.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;')
await writeFile(join(root, 'NuGet.Config'), `<?xml version="1.0"?><configuration>
  <packageSources><clear /><add key="candidate" value="${xml}" /><add key="registry" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping><clear /><packageSource key="candidate"><package pattern="NGB.Platform.*" /></packageSource><packageSource key="registry"><package pattern="*" /></packageSource></packageSourceMapping>
  <fallbackPackageFolders><clear /></fallbackPackageFolders>
</configuration>\n`)
execFileSync('dotnet', ['restore', 'NgbApplication.slnx', '--force-evaluate', '--no-http-cache', '--configfile', 'NuGet.Config', '--packages', join(root, 'nuget-cache')], {
  cwd: root, stdio: 'inherit',
})
for (const host of ['Api', 'Migrator', 'BackgroundJobs']) {
  const path = `NgbApplication.${host}/packages.lock.json`
  const lock = JSON.parse(await readFile(join(root, path), 'utf8'))
  for (const framework of Object.values(lock.dependencies)) {
    for (const [id, dependency] of Object.entries(framework)) {
      if (id.startsWith('NGB.Platform.')) assert.equal(dependency.resolved, version)
    }
  }
  await cp(join(root, path), join(content, path))
}
const archive = await readFile(join(repository, 'artifacts/npm', `ngbplatform-ui-${version}.tgz`))
const integrity = `sha512-${createHash('sha512').update(archive).digest('base64')}`
const sourceManifest = JSON.parse(await readFile(join(repository, 'ui/ngb-ui-framework/package.json'), 'utf8'))
assert.equal(sourceManifest.version, version)
const published = createPlatformUiPackageManifest(sourceManifest)
for (const filename of ['packaging/templates/content/web/package-lock.json', 'ui/ngb-crm-web/package-lock.json']) {
  const path = join(repository, filename)
  const lock = JSON.parse(await readFile(path, 'utf8'))
  assert.equal(lock.packages[''].dependencies['@ngbplatform/ui'], version)
  const dependency = lock.packages['node_modules/@ngbplatform/ui']
  Object.assign(dependency, {
    version, resolved: `https://registry.npmjs.org/@ngbplatform/ui/-/ui-${version}.tgz`, integrity,
    peerDependencies: published.peerDependencies, peerDependenciesMeta: published.peerDependenciesMeta,
  })
  await writeFile(path, `${JSON.stringify(lock, null, 2)}\n`)
}
console.log(`Generated lockfiles identify the existing ${version} candidate artifacts.`)
