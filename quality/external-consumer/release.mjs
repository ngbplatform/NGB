import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { assertGateResults } from './contracts.mjs'
import {
  artifactIdentity, assertArtifactIdentity, assertArtifactInventory, assertTemplatePackage, digest, includesAiKit, validateMatrix,
} from './release-contracts.mjs'
import { atomicJson, checkpointRunner, lockRun } from './checkpoints.mjs'
import { command } from './execution.mjs'
import { prepareEnvironment, prepareToolchain, verifyHostEnvironment } from './environment.mjs'
import { validateCertification } from '../ai-devkit/certification-data.mjs'
import { readBytes } from '../../packaging/templates/content/infrastructure/ngb/kit-files.mjs'
import { sourceFiles } from './source-files.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const action = process.argv[2]
const root = resolve(process.argv[3] ?? join(repository, 'artifacts/release-candidate'))
const matrix = JSON.parse(await readFile(new URL('./matrix.json', import.meta.url), 'utf8'))
const inventory = JSON.parse(await readFile(join(repository, matrix.baselines), 'utf8'))
validateMatrix(matrix)
const hasGate = id => matrix.gates.some(gate => gate.id === id)
const manifestPath = join(root, 'release-manifest.json')
const derivedInputs = [
  ...['Api', 'Migrator', 'BackgroundJobs', ...(includesAiKit(matrix.target) ? ['Tests', 'IntegrationTests'] : [])]
    .map(host => `packaging/templates/content/NgbApplication.${host}/packages.lock.json`),
  ...(includesAiKit(matrix.target) ? ['packaging/templates/content/infrastructure/validation/packages.lock.json'] : []),
  'packaging/templates/content/web/package-lock.json',
  'ui/ngb-crm-web/package-lock.json',
]
const readJson = async path => JSON.parse(await readFile(path, 'utf8'))
const save = async (path, value) => writeFile(path, `${JSON.stringify(value, null, 2)}\n`, { flag: 'wx' })

async function sourceIdentity() {
  const paths = sourceFiles(repository).filter(path => !derivedInputs.includes(path))
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
  if (includesAiKit(matrix.target)) {
    for (const profile of ['artifacts', 'contracts', 'application', 'upgrade', 'agents']) {
      const report = validateCertification(await readJson(join(root, `certification/ai-kit/${profile}.json`)), profile, sealed.sha256)
      for (const [path, hash] of Object.entries(report.files)) {
        assert.equal(digest(await readBytes(root, path, 16 * 1024 * 1024)), hash, `Kit child evidence changed: ${path}`)
      }
    }
    await verifyToolingQuality(sealed.sha256)
  }
  const identity = await readJson(join(root, 'certification/candidate-identity.json'))
  assert.equal(identity.checkpointsSha256, digest(await readFile(join(root, 'run-state.json'))), 'Certification checkpoints changed.')
  assert.equal(identity.environmentSha256, digest(JSON.stringify(await readJson(join(root, 'certification/environment.json')))), 'Certification environment changed.')
  return sealed
}

async function verifyToolingQuality(candidateSha256) {
  const quality = await readJson(join(root, 'certification/quality-preflight.json'))
  assert.equal(quality.status, 'passed')
  assert.equal(quality.scope, 'quality-preflight')
  assert.equal(quality.artifactManifestSha256, candidateSha256, 'Tooling evidence belongs to another candidate.')
  assert.deepEqual(Object.keys(quality.reports).sort(), ['performance', 'tooling'])
  for (const name of ['performance', 'tooling']) {
    const bytes = await readFile(join(root, `certification/${name}.json`))
    assert.equal(digest(bytes), quality.reports[name].sha256, `Certification ${name} evidence changed.`)
    assert.equal(JSON.parse(bytes).status, 'passed')
  }
}

if (action === 'certify' && !process.argv.includes('--prepared-toolchain')) {
  await verify()
  const prepared = await prepareToolchain()
  await command(prepared.node, [fileURLToPath(import.meta.url), 'certify', root, '--prepared-toolchain'], {
    cwd: repository, env: prepared.env, timeout: 24 * 60 * 60_000,
  })
  process.exit(0)
}

if (action === 'seal') {
  if (includesAiKit(matrix.target)) {
    const prepared = await prepareToolchain()
    await command(prepared.node, ['quality/ai-devkit/prepare-contracts.mjs', '--feed', join(repository, 'artifacts/nuget-release')], {
      cwd: repository, env: prepared.env,
    })
  }
  const sourceTreeSha256 = await sourceIdentity()
  const templatePath = `nuget-release/NGB.Platform.Templates.${matrix.target}.nupkg`
  const templateBytes = await readFile(join(repository, 'artifacts', templatePath))
  await assertTemplatePackage(templateBytes, join(repository, 'packaging/templates/content'))
  await mkdir(root)
  await mkdir(join(root, 'nuget-release'))
  await mkdir(join(root, 'npm'))
  const paths = inventory.packages.flatMap(item => [
    `nuget-release/${item.id}.${matrix.target}.nupkg`,
    ...(item.kind === 'runtime' ? [`nuget-release/${item.id}.${matrix.target}.snupkg`] : []),
  ])
  paths.push(`npm/ngbplatform-ui-${matrix.target}.tgz`)
  if (includesAiKit(matrix.target)) paths.push(`npm/ngbplatform-cli-${matrix.target}.tgz`)
  const artifacts = []
  for (const path of paths) {
    const bytes = path === templatePath ? templateBytes : await readFile(join(repository, 'artifacts', path))
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
    sourceTreeSha256,
    derivedInputs: inputs,
    matrixSha256: digest(await readFile(new URL('./matrix.json', import.meta.url))),
    sourceFixtureSha256: digest(await readFile(join(repository, matrix.sourceManifest))),
    artifacts,
  })
  await verify()
} else if (action === 'verify') {
  await verify()
} else if (action === 'certify') {
  const unlock = await lockRun(join(repository, 'artifacts/certification-run.lock'))
  try {
    const sealed = await verify()
    await mkdir(join(root, 'certification'), { recursive: true })
    const cache = join(repository, 'artifacts/certification-cache', sealed.sha256)
    const stageEnv = { ...process.env, NGB_CERTIFICATION_CACHE: cache }
    const checked = operation => async () => {
      await verify()
      await operation()
      await verify()
    }
    const execute = (script, args = []) => command('node', [script, ...args], {
      cwd: repository, env: stageEnv, timeout: 24 * 60 * 60_000,
    })
    const kit = includesAiKit(matrix.target)
    const preflight = async (name, operation) => {
      console.log(`[start] preflight/${name}`)
      const started = performance.now()
      let status = 'failed'
      try {
        await checked(operation)()
        status = 'passed'
      } finally {
        console.log(`[${status}] preflight/${name}: ${((performance.now() - started) / 1000).toFixed(1)}s`)
      }
    }
    // These bounded checks run on every invocation, before image acquisition or
    // integration work. Their evidence is still validated by the final gates.
    await preflight('host', verifyHostEnvironment)
    await preflight('source-fixture', () => execute('quality/upgrade-certification/verify-source.mjs'))
    if (kit) {
      for (const profile of ['artifacts', 'contracts']) {
        await preflight(`ai-kit-${profile}`, () => execute('quality/ai-devkit/certify.mjs', [profile, root]))
      }
      await preflight('ai-kit-agent-access', () => execute('quality/ai-devkit/run-agent-evaluations.mjs', [root, '--check']))
    }
    console.log('[prepare] Checking browser and every required container image after preflight.')
    const preparationStarted = performance.now()
    const environment = await prepareEnvironment(root)
    console.log(`[passed] prepare: ${((performance.now() - preparationStarted) / 1000).toFixed(1)}s`)
    const identity = { artifactManifestSha256: sealed.sha256, environment }
    const stage = await checkpointRunner(root, identity)
    await atomicJson(join(root, 'certification/environment.json'), environment)
    const qualityArgs = [root, '--certification', '--prepared-toolchain', '--prepared-environment']
    await stage('quality-preflight', checked(() => execute('quality/external-consumer/full-quality.mjs', [
      ...qualityArgs, '--preflight',
    ])), ['certification/quality-preflight.json', 'certification/tooling.json', 'certification/performance.json',
      'certification/certification-quality-preflight-stages.json'])
    await stage('template', checked(() => execute('quality/external-consumer/verify-template.mjs', [root])), ['certification/template.json'])
    await stage('nuget-compatibility', checked(() => execute('packaging/nuget/verify-api-compatibility.mjs', [join(root, 'nuget-release')])), ['nuget-release/api-compatibility.json'])
    await stage('npm-compatibility', checked(() => execute('ui/scripts/verify-platform-ui-contracts.mjs', [join(root, 'npm', `ngbplatform-ui-${matrix.target}.tgz`)])), ['npm/npm-compatibility.json'])
    await stage('compose', checked(() => execute('quality/external-consumer/verify-compose.mjs', [root])), ['certification/compose.json'])
    if (hasGate('mcp-contract')) {
      await stage('mcp-contract', checked(() => execute('quality/mcp/verify-contract.mjs', [join(root, 'certification/mcp-contract')])),
        ['certification/mcp-contract/contracts.json'])
    }
    if (hasGate('mcp-external')) {
      await stage('mcp-external', checked(() => execute('quality/mcp/verify-external.mjs', [join(root, 'nuget-release'), join(root, 'certification/mcp-external')])),
        ['certification/mcp-external/inputs.json'])
    }
    if (hasGate('mcp-verticals')) {
      await stage('mcp-verticals', checked(() => execute('quality/mcp/verify-verticals.mjs', [join(root, 'certification/mcp-verticals')])),
        ['certification/mcp-verticals/verticals.json'])
    }
    for (const profile of Object.keys(matrix.profiles)) {
      await stage(profile, checked(() => execute('quality/external-consumer/certify.mjs', [profile, root])), [`certification/${profile}.json`])
    }
    if (kit) {
      for (const profile of ['application', 'upgrade']) {
        await stage(`ai-kit-${profile}`, checked(() => execute('quality/ai-devkit/certify.mjs', [profile, root])),
          [`certification/ai-kit/${profile}.json`])
      }
    }
    // Historical matrices include the framework browser gate. The current
    // release explicitly leaves platform regression and coverage to the maintainer.
    if (hasGate('browser-quality')) {
      await stage('certification-quality', checked(() => execute('quality/external-consumer/full-quality.mjs', qualityArgs)), [
        'certification/certification-quality.json', 'certification/tooling.json',
        'certification/cross-browser.json', 'certification/performance.json',
        'certification/certification-quality-stages.json',
      ])
    }
    if (kit) {
      await stage('ai-kit-agents', checked(() => execute('quality/ai-devkit/certify.mjs', ['agents', root])),
        ['certification/ai-kit/agents.json'])
    }
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
    await bind(['external-isolation', 'tooling-quality'], 'certification/tooling.json')
    await bind(['nuget-compatibility'], 'nuget-release/api-compatibility.json')
    await bind(['npm-compatibility'], 'npm/npm-compatibility.json')
    if (hasGate('browser-quality')) await bind(['browser-quality'], 'certification/cross-browser.json')
    await bind(['performance-tooling'], 'certification/performance.json')
    if (kit) {
      for (const [gate, profile] of Object.entries({ 'ai-kit-artifacts': 'artifacts', 'ai-kit-contracts': 'contracts',
        'ai-kit-independent-app': 'application', 'ai-kit-upgrade': 'upgrade', 'ai-kit-agents': 'agents' })) {
        validateCertification(await readJson(join(root, `certification/ai-kit/${profile}.json`)), profile, sealed.sha256)
        await bind([gate], `certification/ai-kit/${profile}.json`)
      }
      await verifyToolingQuality(sealed.sha256)
    }
    for (const [gate, path] of Object.entries({ 'mcp-contract': 'contracts.json', 'mcp-external': 'inputs.json', 'mcp-verticals': 'verticals.json' })) {
      if (hasGate(gate)) await bind([gate], `certification/${gate}/${path}`)
    }
    await atomicJson(join(root, 'certification/candidate-identity.json'), {
      artifactManifestSha256: sealed.sha256, scope: matrix.scope, source: matrix.source, target: matrix.target,
      baselines: inventory, status: 'passed', environmentSha256: digest(JSON.stringify(environment)),
      checkpointsSha256: digest(await readFile(join(root, 'run-state.json'))),
    })
    await bind(['candidate-evidence'], 'certification/candidate-identity.json')
    await verify()
    await atomicJson(join(root, 'candidate-evidence.json'), { schemaVersion: 1, stage: 'candidate', artifactManifestSha256: sealed.sha256, results })
    await verifyCandidate()
  } finally {
    await unlock()
  }
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
