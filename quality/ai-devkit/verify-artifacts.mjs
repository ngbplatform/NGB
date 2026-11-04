import assert from 'node:assert/strict'
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { unzipSync } from 'fflate'
import { preparePayload, prepareTemplate } from '../../ai-devkit/prepare.mjs'
import { verifyGuidance } from '../../ai-devkit/guidance.mjs'
import { verifyCliArchive } from '../../packaging/templates/content/infrastructure/ngb/archive.mjs'
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
for (const project of config.projects) {
  const lock = `content/${dirname(project.path)}/packages.lock.json`
  assert.ok(template[lock], `The external starter must ship its locked dependencies: ${lock}`)
}
const report = { schemaVersion: 1, status: 'passed', artifactManifestSha256: digest(manifestBytes),
  version: manifest.version, kitSha256: digest(JSON.stringify(payload)), files: payload.files.length,
  shared, assertions: ['exact-artifact-inventory', 'canonical-template', 'cli-template-parity',
    'kit-integrity', 'deterministic-guidance', 'agent-adapter-parity', 'all-project-lockfiles'] }
await mkdir(dirname(output), { recursive: true })
await writeFile(output, `${JSON.stringify(report, null, 2)}\n`, { flag: 'wx' })
console.log(`Kit artifacts: passed (${shared.length} shared files; ${payload.files.length} guidance files).`)
