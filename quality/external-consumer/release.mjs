import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { cp, mkdir, readFile, writeFile } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { assertGateResults } from './contracts.mjs'
import { artifactIdentity, assertArtifactIdentity, assertArtifactInventory, digest, validateMatrix } from './release-contracts.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const action = process.argv[2]
const root = resolve(process.argv[3] ?? join(repository, 'artifacts/release-candidate'))
const matrix = JSON.parse(await readFile(new URL('./matrix.json', import.meta.url), 'utf8'))
const inventory = JSON.parse(await readFile(join(repository, matrix.baselines), 'utf8'))
validateMatrix(matrix)
const manifestPath = join(root, 'release-manifest.json')
const derivedInputs = [
  ...['Api', 'Migrator', 'BackgroundJobs'].map(host => `packaging/templates/content/NgbApplication.${host}/packages.lock.json`),
  'packaging/templates/content/web/package-lock.json',
  'ui/ngb-crm-web/package-lock.json',
]
const run = (command, args, env = {}) => execFileSync(command, args, { cwd: repository, env: { ...process.env, ...env }, stdio: 'inherit' })
const readJson = async path => JSON.parse(await readFile(path, 'utf8'))
const save = async (path, value) => writeFile(path, `${JSON.stringify(value, null, 2)}\n`, { flag: 'wx' })

async function sourceIdentity() {
  const paths = execFileSync('git', ['ls-files', '--cached', '--others', '--exclude-standard', '-z'], { cwd: repository, encoding: 'utf8' })
    .split('\0').filter(path => path && path !== 'AGENTS.md' && !path.endsWith('.DS_Store') && !derivedInputs.includes(path)).sort()
  const files = []
  for (const path of paths) files.push([path, digest(await readFile(join(repository, path)))])
  return digest(JSON.stringify(files))
}

async function verify() {
  const manifest = await readJson(manifestPath)
  assertArtifactInventory(manifest, inventory)
  assert.equal(manifest.matrixSha256, digest(await readFile(new URL('./matrix.json', import.meta.url))))
  assert.equal(manifest.sourceFixtureSha256, digest(await readFile(join(repository, matrix.sourceManifest))))
  assert.equal(manifest.sourceTreeSha256, await sourceIdentity(), 'Sources differ from the sealed candidate.')
  assert.deepEqual(Object.keys(manifest.derivedInputs).sort(), [...derivedInputs].sort())
  for (const [path, sha256] of Object.entries(manifest.derivedInputs)) {
    assert.equal(digest(await readFile(join(root, 'generated-inputs', path))), sha256, `Generated input changed: ${path}`)
  }
  for (const artifact of manifest.artifacts) assertArtifactIdentity(artifact, await readFile(join(root, artifact.path)))
  return { manifest, sha256: digest(await readFile(manifestPath)) }
}

async function verifyCandidate() {
  const sealed = await verify()
  const evidence = await readJson(join(root, 'candidate-evidence.json'))
  assertGateResults(matrix, evidence, sealed.sha256)
  for (const result of Object.values(evidence.results)) {
    assert.equal(digest(await readFile(join(root, result.path))), result.evidenceSha256, `Evidence changed: ${result.path}`)
  }
  return sealed
}

if (action === 'seal') {
  await mkdir(root)
  await mkdir(join(root, 'nuget-release'))
  await mkdir(join(root, 'npm'))
  const paths = inventory.packages.flatMap(item => [
    `nuget-release/${item.id}.${matrix.target}.nupkg`,
    ...(item.kind === 'runtime' ? [`nuget-release/${item.id}.${matrix.target}.snupkg`] : []),
  ])
  paths.push(`npm/ngbplatform-ui-${matrix.target}.tgz`)
  const artifacts = []
  for (const path of paths) {
    const bytes = await readFile(join(repository, 'artifacts', path))
    artifacts.push(artifactIdentity(path, bytes))
    await writeFile(join(root, path), bytes, { flag: 'wx', mode: 0o444 })
  }
  const inputs = {}
  for (const path of derivedInputs) {
    const bytes = await readFile(join(repository, path))
    inputs[path] = digest(bytes)
    const destination = join(root, 'generated-inputs', path)
    await mkdir(dirname(destination), { recursive: true })
    await writeFile(destination, bytes, { flag: 'wx', mode: 0o444 })
  }
  await save(manifestPath, {
    schemaVersion: 1, version: matrix.target,
    sourceCommit: execFileSync('git', ['rev-parse', 'HEAD'], { cwd: repository, encoding: 'utf8' }).trim(),
    sourceTreeSha256: await sourceIdentity(),
    derivedInputs: inputs,
    matrixSha256: digest(await readFile(new URL('./matrix.json', import.meta.url))),
    sourceFixtureSha256: digest(await readFile(join(repository, matrix.sourceManifest))),
    artifacts,
  })
  await verify()
} else if (action === 'verify') {
  await verify()
} else if (action === 'certify') {
  const sealed = await verify()
  await mkdir(join(root, 'certification'), { recursive: true })
  run('node', ['quality/external-consumer/test-tooling.mjs'])
  run('node', ['quality/external-consumer/verify-template.mjs', root])
  run('node', ['quality/external-consumer/verify-compose.mjs', root])
  run('node', ['quality/upgrade-certification/verify-source.mjs'])
  run('node', ['packaging/nuget/verify-api-compatibility.mjs', join(root, 'nuget-release')])
  run('node', ['ui/scripts/verify-platform-ui-contracts.mjs', join(root, 'npm', `ngbplatform-ui-${matrix.target}.tgz`)])
  for (const profile of Object.keys(matrix.profiles)) run('node', ['quality/external-consumer/certify.mjs', profile, root])
  run('node', ['quality/external-consumer/full-quality.mjs', root])
  const results = {}
  const bind = async (gates, path) => {
    const evidenceSha256 = digest(await readFile(join(root, path)))
    for (const gate of gates) results[gate] = { status: 'passed', path, evidenceSha256 }
  }
  for (const [profile, expected] of Object.entries(matrix.profiles)) {
    const path = `certification/${profile}.json`
    const evidence = await readJson(join(root, path))
    assert.equal(evidence.status, 'passed')
    assert.equal(evidence.artifactManifestSha256, sealed.sha256)
    for (const assertion of expected.assertions) assert.ok(evidence.assertions[assertion], `Missing ${profile}: ${assertion}`)
    await bind([profile], path)
  }
  await bind(['template-artifact', 'frontend-artifact'], 'certification/template.json')
  const compose = await readJson(join(root, 'certification/compose.json'))
  assert.equal(compose.status, 'passed')
  const template = sealed.manifest.artifacts.find(item => item.path.includes('NGB.Platform.Templates.'))
  assert.equal(compose.templateSha256, template.sha256)
  await bind(['clean-starter'], 'certification/compose.json')
  await bind(['starter-job', 'administrator-contract'], 'certification/clean-starter.json')
  await bind(['upgrade-job', 'migration-sequencing', 'source-fixture'], 'certification/core-upgrade.json')
  await cp(join(repository, 'artifacts/certification/tooling.json'), join(root, 'certification/tooling.json'))
  await bind(['external-isolation', 'tooling-quality'], 'certification/tooling.json')
  await bind(['nuget-compatibility'], 'nuget-release/api-compatibility.json')
  await bind(['npm-compatibility'], 'npm/npm-compatibility.json')
  await cp(join(repository, 'artifacts/certification/full-quality.json'), join(root, 'certification/full-quality.json'))
  await bind(['backend-quality', 'frontend-quality', 'regression-quality'], 'certification/full-quality.json')
  await save(join(root, 'certification/candidate-identity.json'), {
    artifactManifestSha256: sealed.sha256, scope: matrix.scope, source: matrix.source, target: matrix.target,
    baselines: inventory, status: 'passed',
  })
  await bind(['candidate-evidence'], 'certification/candidate-identity.json')
  await save(join(root, 'candidate-evidence.json'), { schemaVersion: 1, stage: 'candidate', artifactManifestSha256: sealed.sha256, results })
  await verifyCandidate()
} else if (action === 'verify-candidate') {
  await verifyCandidate()
} else if (action === 'verify-publication' || action === 'verify-promotion') {
  const sealed = await verifyCandidate()
  const publication = await readJson(join(root, action === 'verify-promotion' ? 'promotion-evidence.json' : 'publication-evidence.json'))
  assertGateResults(matrix, publication, sealed.sha256)
  const result = publication.results[action === 'verify-promotion' ? 'registry-verification' : 'publish-identity']
  assert.equal(digest(await readFile(join(root, result.path))), result.evidenceSha256)
} else {
  throw new Error('Usage: node quality/external-consumer/release.mjs seal|verify|certify|verify-candidate|verify-publication|verify-promotion [candidate-directory]')
}
console.log(`Release ${action}: passed (${root})`)
