import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { cp, mkdir, mkdtemp, readFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { atomicJson, lockRun, readOptionalJson } from './checkpoints.mjs'
import { command } from './execution.mjs'
import { prepareEnvironment, prepareToolchain } from './environment.mjs'
import { digest } from './release-contracts.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const candidate = resolve(process.argv[2] ?? join(repository, 'artifacts/release-candidate'))
if (!process.argv.includes('--prepared-toolchain')) {
  const prepared = await prepareToolchain()
  await command(prepared.node, [fileURLToPath(import.meta.url), candidate, '--prepared-toolchain'], { cwd: repository, env: prepared.env })
  process.exit(0)
}
// The release runner already owns this lock when it supplies the environment.
const unlockCheckout = process.argv.includes('--prepared-environment')
  ? null
  : await lockRun(join(repository, 'artifacts/certification-run.lock'))
let unlockWorkspace
try {
  unlockWorkspace = await lockRun(join(candidate, 'quality-workspace.lock'))
  await command('node', ['quality/external-consumer/release.mjs', 'verify', candidate], { cwd: repository })
  const environment = process.argv.includes('--prepared-environment')
    ? JSON.parse(await readFile(join(candidate, 'certification/environment.json'), 'utf8'))
    : await prepareEnvironment(candidate)
  const manifestBytes = await readFile(join(candidate, 'release-manifest.json'))
  const manifest = JSON.parse(manifestBytes)
  const identity = { artifactManifestSha256: digest(manifestBytes), environment }
  const locationFile = join(candidate, 'quality-workspace.json')
  let workspace = await readOptionalJson(locationFile)
  if (workspace) {
    assert.equal(workspace.identitySha256, digest(JSON.stringify(identity)), 'Quality environment changed; seal a new candidate.')
    try {
      await readFile(join(workspace.path, 'quality-identity.json'))
    } catch (error) {
      if (error.code !== 'ENOENT') throw error
      workspace = null
    }
  }
  if (!workspace) {
    workspace = { path: await mkdtemp(join(tmpdir(), 'ngb-full-quality-')), identitySha256: digest(JSON.stringify(identity)) }
    await atomicJson(locationFile, workspace)
  }
  const root = workspace.path
  const containerName = `ngb-quality-${digest(candidate).slice(0, 20)}`
  const containerLabel = `ngb.certification.workspace=${digest(candidate)}`
  const stopContainer = async () => {
    const id = await command('docker', ['ps', '-aq', '--filter', `name=^/${containerName}$`, '--filter', `label=${containerLabel}`], { capture: true })
    if (id) await command('docker', ['rm', '--force', id], { capture: true })
  }
  // The CLI can die while Docker keeps running. Stop only this candidate's
  // labelled container before touching its persistent workspace or checkpoints.
  await stopContainer()
  const paths = execFileSync('git', ['ls-files', '--cached', '--others', '--exclude-standard', '-z'], { cwd: repository, encoding: 'utf8' })
    .split('\0').filter(path => path && path !== 'AGENTS.md' && !path.endsWith('.DS_Store'))
  for (const path of paths) {
    await mkdir(dirname(join(root, path)), { recursive: true })
    // Re-copy tracked inputs on resume; retain only this candidate's build and dependency caches.
    await cp(join(repository, path), join(root, path))
  }
  for (const path of Object.keys(manifest.derivedInputs)) {
    await cp(join(candidate, 'generated-inputs', path), join(root, path))
  }
  await cp(join(candidate, 'nuget-release'), join(root, 'artifacts/nuget'), { recursive: true })
  await cp(join(candidate, 'npm'), join(root, 'artifacts/npm'), { recursive: true })
  await atomicJson(join(root, 'quality-identity.json'), identity)
  const endpoint = environment.dockerEndpoint
  assert.ok(endpoint.startsWith('unix://'), 'Certification requires a local Unix Docker socket (Docker Desktop or Linux/WSL2).')
  const socket = endpoint.slice('unix://'.length)
  for (const path of ['dotnet-home', 'config', 'cache']) await mkdir(join(root, 'artifacts', path), { recursive: true })
  console.log(`Linux quality workspace (retained for resume): ${root}`)
  try {
    await command('docker', [
      'run', '--rm', '--init', '--ipc=host', '--add-host', 'host.docker.internal:host-gateway',
      '--name', containerName, '--label', containerLabel,
      '--user', `${process.getuid()}:${process.getgid()}`, '--group-add', environment.dockerSocketGroup,
      '-e', 'TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal',
      '-e', 'CI=true',
      '-e', 'NGB_FRONTEND_COVERAGE_NO_INSTALL=true',
      '-e', `NGB_BACKEND_COVERAGE_JOBS=${process.env.NGB_BACKEND_COVERAGE_JOBS ?? '2'}`,
      '-e', `NGB_FRONTEND_COVERAGE_JOBS=${process.env.NGB_FRONTEND_COVERAGE_JOBS ?? '1'}`,
      '-e', 'NUGET_ENHANCED_MAX_NETWORK_TRY_COUNT=3',
      '-e', 'NUGET_ENHANCED_NETWORK_RETRY_DELAY_MILLISECONDS=1000',
      '-e', 'npm_config_fetch_retries=2', '-e', 'npm_config_fetch_retry_mintimeout=1000',
      '-e', 'npm_config_fetch_retry_maxtimeout=4000', '-e', 'npm_config_fetch_timeout=60000',
      '-e', 'npm_config_cache=/workspace/artifacts/npm-cache',
      '-e', 'DOTNET_CLI_HOME=/workspace/artifacts/dotnet-home',
      '-e', 'DOTNET_CLI_TELEMETRY_OPTOUT=1', '-e', 'DOTNET_NOLOGO=1',
      '-e', 'NUGET_HTTP_CACHE_PATH=/workspace/artifacts/nuget-http',
      '-e', 'XDG_CONFIG_HOME=/workspace/artifacts/config', '-e', 'XDG_CACHE_HOME=/workspace/artifacts/cache',
      '-v', `${socket}:/var/run/docker.sock`,
      '-v', `${root}:/workspace`, '-w', '/workspace', environment.images.quality,
      'bash', '-euc', [
        'npm cache add artifacts/npm/*.tgz',
        'npm --prefix ui ci',
        'npm --prefix quality ci',
        'bash run-full-quality.sh /workspace/quality-identity.json',
      ].join('\n'),
    ], { cwd: repository })
  } finally {
    await stopContainer()
    for (const name of ['coverage', 'certification']) {
      await cp(join(root, 'artifacts', name), join(repository, 'artifacts', name), { recursive: true }).catch(error => {
        if (error.code !== 'ENOENT') throw error
      })
    }
  }
  await command('node', ['quality/external-consumer/release.mjs', 'verify', candidate], { cwd: repository })
  await mkdir(join(candidate, 'certification'), { recursive: true })
  for (const name of ['full-quality.json', 'tooling.json']) {
    await cp(join(root, 'artifacts/certification', name), join(candidate, 'certification', name))
  }
  await cp(join(root, 'artifacts/run-state.json'), join(candidate, 'certification/quality-stages.json'))
  console.log(`Complete Linux quality diagnostics: ${root}`)
} finally {
  try {
    await unlockWorkspace?.()
  } finally {
    await unlockCheckout?.()
  }
}
