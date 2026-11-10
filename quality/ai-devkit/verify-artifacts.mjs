import assert from 'node:assert/strict'
import { lstat, mkdir, readFile, writeFile } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { unzipSync } from 'fflate'
import { preparePayload, prepareTemplate } from '../../ai-devkit/prepare.mjs'
import { verifyGuidance } from '../../ai-devkit/guidance.mjs'
import { readNpmArchive, verifyCliArchive } from '../../packaging/templates/content/infrastructure/ngb/archive.mjs'
import { assetPaths } from '../../packaging/templates/content/infrastructure/ngb/kit-files.mjs'
import { createPlatformUiPackageManifest } from '../../ui/scripts/platform-ui-package-manifest.mjs'
import { assertUiConsumerLock } from './example-evidence.mjs'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { validateInstalled } from '../../packaging/templates/content/infrastructure/ngb/kit-plan.mjs'
import { assertArtifactIdentity, assertArtifactInventory, assertTemplatePackage } from '../external-consumer/release-contracts.mjs'

assert.equal(process.argv.length, 4, 'Usage: node verify-artifacts.mjs <sealed-candidate> <new-report.json>')
const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const candidate = resolve(process.argv[2])
const output = resolve(process.argv[3])
const manifestBytes = await readFile(join(candidate, 'release-manifest.json'))
const manifest = JSON.parse(manifestBytes)
const inventory = JSON.parse(await readFile(join(repository, 'packaging/nuget/baselines.json')))
assertArtifactInventory(manifest, inventory)
const artifacts = new Map()
for (const artifact of manifest.artifacts) {
  const bytes = await readFile(join(candidate, artifact.path))
  assertArtifactIdentity(artifact, bytes)
  artifacts.set(artifact.path, bytes)
}
const uiArtifact = manifest.artifacts.find(item => item.path === `npm/ngbplatform-ui-${manifest.version}.tgz`)
assert.ok(uiArtifact, 'The candidate does not contain the exact UI package.')
const ui = readNpmArchive(artifacts.get(uiArtifact.path))
const uiRoot = join(repository, 'ui/ngb-ui-framework')
const uiManifest = createPlatformUiPackageManifest(JSON.parse(await readFile(join(uiRoot, 'package.json'))))
assert.deepEqual(JSON.parse(ui.get('package/package.json')), uiManifest, 'UI package manifest differs from current sources.')
const uiPaths = []
for (const path of uiManifest.files) {
  const paths = (await lstat(join(uiRoot, path))).isDirectory() ? await assetPaths(uiRoot, path) : [path]
  // npm excludes Finder metadata even when it is present under a packaged directory.
  uiPaths.push(...paths.filter(path => !path.endsWith('/.DS_Store')))
}
assert.deepEqual([...ui.keys()].sort(), ['package/package.json', ...uiPaths.map(path => `package/${path}`)].sort(),
  'UI package inventory differs from current sources.')
for (const path of uiPaths) {
  assert.deepEqual(ui.get(`package/${path}`), await readFile(join(uiRoot, path)), `Stale UI package file: ${path}`)
}
await prepareTemplate()
const templateBytes = artifacts.get(`nuget-release/NGB.Platform.Templates.${manifest.version}.nupkg`)
await assertTemplatePackage(templateBytes, join(repository, 'packaging/templates/content'))
const template = unzipSync(templateBytes)
const cli = verifyCliArchive(artifacts.get(`npm/ngbplatform-cli-${manifest.version}.tgz`), manifest.version)
const shared = []
for (const [path, bytes] of cli.files) {
  if (!path.startsWith('package/infrastructure/')) continue
  const templatePath = `content/${path.slice('package/'.length)}`
  assert.ok(template[templatePath], `CLI file is missing from the template: ${path}`)
  assert.deepEqual(bytes, Buffer.from(template[templatePath]), `CLI/template bytes differ: ${path}`)
  shared.push({ path: path.slice('package/'.length), sha256: digest(bytes) })
}
const payload = JSON.parse(cli.files.get('package/infrastructure/ngb/kit-payload.json'))
assert.deepEqual(payload, await preparePayload(), 'Packaged guidance differs from canonical authoring inputs.')
validateInstalled(JSON.parse(Buffer.from(template['content/ai/kit.json'])), payload)
verifyGuidance(payload)
for (const file of payload.files.filter(file => file.role === 'file')) {
  assert.equal(Buffer.from(template[`content/${file.path}`]).toString('utf8'), file.content, `Packaged Kit file differs: ${file.path}`)
}
const config = JSON.parse(Buffer.from(template['content/ngb.config.json']))
for (const file of payload.files.filter(file => file.path.endsWith('/web/package-lock.json'))) {
  assertUiConsumerLock(JSON.parse(file.content), uiArtifact, manifest.version)
}
assertUiConsumerLock(JSON.parse(Buffer.from(template['content/web/package-lock.json'])), uiArtifact, manifest.version)
for (const project of config.projects) {
  const lock = `content/${dirname(project.path)}/packages.lock.json`
  assert.ok(template[lock], `The external starter must ship its locked dependencies: ${lock}`)
}
const report = { schemaVersion: 1, status: 'passed', artifactManifestSha256: digest(manifestBytes),
  version: manifest.version, kitSha256: digest(JSON.stringify(payload)), files: payload.files.length,
  shared, assertions: ['exact-artifact-inventory', 'canonical-template', 'cli-template-parity',
    'kit-integrity', 'deterministic-guidance', 'agent-adapter-parity', 'all-project-lockfiles',
    'ui-current-source-parity', 'consumer-ui-lock-identities'] }
await mkdir(dirname(output), { recursive: true })
await writeFile(output, `${JSON.stringify(report, null, 2)}\n`, { flag: 'wx' })
console.log(`Kit artifacts: passed (${shared.length} shared files; ${payload.files.length} guidance files).`)
