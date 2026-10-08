import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { mkdir, mkdtemp, readFile, readdir, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { unzipSync } from 'fflate'
import { assertConsumerIsolation, hashDirectory } from './contracts.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const artifacts = resolve(process.argv[2] ?? join(repository, 'artifacts'))
const matrix = JSON.parse(await readFile(new URL('./matrix.json', import.meta.url), 'utf8'))
const root = await mkdtemp(join(tmpdir(), 'ngb-template-contract-'))
const application = join(root, 'application')
const hive = join(root, 'hive')
const run = (command, args, cwd = application) => execFileSync(command, args, { cwd, stdio: 'inherit' })
const template = join(artifacts, 'nuget-release', `NGB.Platform.Templates.${matrix.target}.nupkg`)
const archive = unzipSync(await readFile(template))
assert.ok(Object.keys(archive).some(name => name.endsWith('.template.config/template.json')))
for (const filename of ['.template.config/template.json', 'infrastructure/Dockerfile', 'web/Dockerfile']) {
  assert.ok(archive[`content/${filename}`], `Required template file missing: ${filename}`)
}
assert.ok(!Object.keys(archive).some(name => name.startsWith('lib/')), 'Template must not masquerade as a runtime assembly.')
run('dotnet', ['new', 'install', template, '--debug:custom-hive', hive], root)
run('dotnet', ['new', 'ngb', '-n', 'ExternalReview', '-o', application, '--debug:custom-hive', hive], root)
await assertConsumerIsolation(application, repository)
const files = await hashDirectory(application)
for (const filename of Object.keys(files)) {
  const text = await readFile(join(application, filename), 'utf8')
  assert.ok(!text.includes('NgbApplication'), `Unsubstituted template name: ${filename}`)
  assert.ok(!filename.includes('NgbApplication'))
}
const projects = Object.keys(files).filter(name => name.endsWith('.csproj'))
assert.deepEqual(projects.sort(), ['Api', 'BackgroundJobs', 'Migrator'].map(name => `ExternalReview.${name}/ExternalReview.${name}.csproj`).sort())
for (const filename of projects) {
  const project = await readFile(join(application, filename), 'utf8')
  assert.ok(!project.includes('ProjectReference'))
  for (const reference of project.matchAll(/PackageReference Include="(NGB\.Platform\.[^"]+)" Version="([^"]+)"/g)) {
    assert.equal(reference[2], '[$(NgbPlatformVersion)]')
  }
}
run('node', ['infrastructure/configure.mjs', 'administrator@example.test'])
const compose = JSON.parse(execFileSync('docker', ['compose', 'config', '--format', 'json'], { cwd: application, encoding: 'utf8' }))
assert.deepEqual(Object.keys(compose.services).sort(), ['api', 'jobs', 'keycloak', 'migrator', 'postgres', 'web'])
for (const name of ['api', 'jobs']) assert.equal(compose.services[name].depends_on.migrator.condition, 'service_completed_successfully')
assert.throws(() => run('node', ['infrastructure/configure.mjs', 'replacement@example.test']))
const web = join(application, 'web')
const npmCache = join(root, 'npm-cache')
run('npm', ['cache', 'add', join(artifacts, 'npm', `ngbplatform-ui-${matrix.target}.tgz`), '--cache', npmCache], web)
run('npm', ['ci', '--workspaces=false', '--cache', npmCache, '--registry', 'https://registry.npmjs.org'], web)
const packageRoot = join(web, 'node_modules/@ngbplatform/ui')
const manifest = JSON.parse(await readFile(join(packageRoot, 'package.json'), 'utf8'))
const imports = []
for (const [subpath, target] of Object.entries(manifest.exports)) {
  const targets = typeof target === 'string' ? [target] : Object.values(target)
  for (const file of targets) await readFile(join(packageRoot, file))
  const specifier = subpath === '.' ? '@ngbplatform/ui' : `@ngbplatform/ui/${subpath.slice(2)}`
  if (subpath === './styles') imports.push(`import '${specifier}'`)
  else imports.push(`import * as surface${imports.length} from '${specifier}'\nvoid surface${imports.length}`)
}
await writeFile(join(web, 'src/contract-smoke.ts'), `${imports.join('\n')}\n`)
await writeFile(join(web, 'src/TailwindSmoke.vue'), '<template><div class="w-[137px] bg-ngb-bg" /></template>\n')
run('npm', ['run', 'build'], web)
const css = (await Promise.all((await readdir(join(web, 'dist/assets'))).filter(name => name.endsWith('.css'))
  .map(name => readFile(join(web, 'dist/assets', name), 'utf8')))).join('\n')
assert.ok(css.includes('width:137px'), 'Application classes must be discovered.')
assert.ok(css.includes('--ngb-bg'), 'The public preset must retain the design tokens.')
const publicAssets = await hashDirectory(join(packageRoot, 'public'))
for (const filename of Object.keys(publicAssets)) {
  const bytes = await readFile(join(web, 'dist', filename))
  assert.equal(createHash('sha256').update(bytes).digest('hex'), publicAssets[filename])
}
await mkdir(join(artifacts, 'certification'), { recursive: true })
await writeFile(join(artifacts, 'certification/template.json'), `${JSON.stringify({
  schemaVersion: 1, version: matrix.target, status: 'passed', templateSha256: createHash('sha256').update(await readFile(template)).digest('hex'),
  assertions: ['installation', 'substitution', 'minimal-projects', 'exact-packages', 'isolation', 'migration-dependencies', 'secret-initializer', 'all-exports', 'tailwind', 'public-assets', 'production-build'],
}, null, 2)}\n`)
console.log(`Template and all ${Object.keys(manifest.exports).length} npm entry points passed: ${root}`)
