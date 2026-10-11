import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { createReadStream, createWriteStream } from 'node:fs'
import { access, mkdir, mkdtemp, readFile, rename, rm } from 'node:fs/promises'
import { arch, platform, release } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { Readable } from 'node:stream'
import { pipeline } from 'node:stream/promises'
import { fileURLToPath } from 'node:url'
import { atomicJson, lockRun, readOptionalJson } from './checkpoints.mjs'
import { command, retryDownload } from './execution.mjs'
import { digest } from './release-contracts.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
export const toolchain = JSON.parse(await readFile(new URL('./toolchain.json', import.meta.url), 'utf8'))

async function fetchFile(url, destination) {
  await retryDownload(async () => {
    const response = await fetch(url, { signal: AbortSignal.timeout(5 * 60_000) })
    if (!response.ok) throw new Error(`Download HTTP ${response.status}: ${url}`)
    await pipeline(Readable.fromWeb(response.body), createWriteStream(destination, { mode: 0o600 }))
  })
}

async function fetchText(url) {
  return await retryDownload(async () => {
    const response = await fetch(url, { signal: AbortSignal.timeout(30_000) })
    if (!response.ok) throw new Error(`Download HTTP ${response.status}: ${url}`)
    return await response.text()
  })
}

async function fileDigest(path, algorithm = 'sha256') {
  const hash = createHash(algorithm)
  for await (const chunk of createReadStream(path)) hash.update(chunk)
  return hash.digest('hex')
}

async function installTool(name, version) {
  const cache = join(repository, 'artifacts/certification-tools')
  const destination = join(cache, `${name}-${version}-${platform()}-${arch()}`)
  const executable = join(destination, name === 'node' ? 'bin/node' : 'dotnet')
  const receipt = await readOptionalJson(join(destination, 'installation.json'))
  if (receipt) {
    assert.equal(receipt.executableSha256, await fileDigest(executable), `Managed ${name} was modified: ${destination}`)
    return executable
  }
  assert.ok(['darwin', 'linux'].includes(platform()) && ['arm64', 'x64'].includes(arch()),
    'Certification supports macOS/Linux on ARM64 or x64, including Linux through WSL2.')
  await mkdir(cache, { recursive: true })
  const temporary = await mkdtemp(join(cache, `${name}-download-`))
  try {
    let url
    let expected
    let algorithm
    if (name === 'dotnet') {
      const metadata = JSON.parse(await fetchText('https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'))
      const sdk = metadata.releases.flatMap(item => item.sdks ?? [item.sdk]).find(item => item?.version === version)
      const rid = `${platform() === 'darwin' ? 'osx' : 'linux'}-${arch()}`
      const archive = sdk?.files.find(item => item.rid === rid && item.url.endsWith('.tar.gz'))
      assert.ok(archive, `Official .NET SDK ${version}/${rid} archive is missing.`)
      assert.equal(new URL(archive.url).hostname, 'builds.dotnet.microsoft.com')
      url = archive.url
      expected = archive.hash.toLowerCase()
      algorithm = 'sha512'
    } else {
      const filename = `node-v${version}-${platform() === 'darwin' ? 'darwin' : 'linux'}-${arch()}.tar.gz`
      const base = `https://nodejs.org/dist/v${version}`
      const checksums = await fetchText(`${base}/SHASUMS256.txt`)
      expected = checksums.split('\n').map(line => line.trim().split(/\s+/)).find(parts => parts[1] === filename)?.[0]
      assert.match(expected ?? '', /^[a-f0-9]{64}$/)
      url = `${base}/${filename}`
      algorithm = 'sha256'
    }
    console.log(`Preparing isolated ${name} ${version} (${platform()}/${arch()})...`)
    const archive = join(temporary, 'archive.tar.gz')
    await fetchFile(url, archive)
    assert.equal(await fileDigest(archive, algorithm), expected, `${name} archive checksum mismatch.`)
    const unpacked = join(temporary, 'unpacked')
    await mkdir(unpacked)
    await command('tar', ['-xzf', archive, '-C', unpacked, ...(name === 'node' ? ['--strip-components=1'] : [])])
    const binary = join(unpacked, name === 'node' ? 'bin/node' : 'dotnet')
    await atomicJson(join(unpacked, 'installation.json'), { url, algorithm, checksum: expected, executableSha256: await fileDigest(binary) })
    await rename(unpacked, destination)
    return executable
  } finally {
    await rm(temporary, { recursive: true, force: true })
  }
}

export async function prepareToolchain() {
  const unlock = await lockRun(join(repository, 'artifacts/certification-tools.lock'))
  try {
    return await configureToolchain()
  } finally {
    await unlock()
  }
}

async function configureToolchain() {
  const env = { ...process.env }
  let node = process.execPath
  if (process.versions.node !== toolchain.node) node = await installTool('node', toolchain.node)
  let dotnetVersion
  try {
    dotnetVersion = execFileSync('dotnet', ['--version'], { cwd: repository, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim()
  } catch { /* Install the exact SDK below when it is missing. */ }
  if (dotnetVersion !== toolchain.dotnet) {
    const dotnet = await installTool('dotnet', toolchain.dotnet)
    env.DOTNET_ROOT = dirname(dotnet)
    env.PATH = `${dirname(dotnet)}:${env.PATH}`
  }
  env.PATH = `${dirname(node)}:${env.PATH}`
  env.DOTNET_CLI_TELEMETRY_OPTOUT = '1'
  env.DOTNET_NOLOGO = '1'
  env.PLAYWRIGHT_SKIP_BROWSER_GC = '1'
  // Retry HTTP acquisition inside the package managers, never whole builds or tests.
  env.NUGET_ENHANCED_MAX_NETWORK_TRY_COUNT = '3'
  env.NUGET_ENHANCED_NETWORK_RETRY_DELAY_MILLISECONDS = '1000'
  env.npm_config_fetch_retries = '2'
  env.npm_config_fetch_retry_mintimeout = '1000'
  env.npm_config_fetch_retry_maxtimeout = '4000'
  env.npm_config_fetch_timeout = '60000'
  return { node, env }
}

export async function prepareEnvironment(candidate) {
  assert.equal(process.versions.node, toolchain.node, 'Certification must use the prepared Node runtime.')
  assert.equal(await command('dotnet', ['--version'], { capture: true }), toolchain.dotnet)
  for (const name of ['NGB_FRONTEND_COVERAGE_SKIP_E2E', 'NGB_BACKEND_COVERAGE_NO_BUILD', 'NGB_BACKEND_COVERAGE_NO_RESTORE']) {
    assert.notEqual(process.env[name], 'true', `${name} is forbidden during certification.`)
  }
  for (const name of ['NGB_RELEASE_MATRIX', 'NGB_REGISTRY_ONLY', 'NGB_APICOMPAT']) {
    assert.ok(!process.env[name], `${name} cannot override release certification.`)
  }
  for (const name of ['NGB_BACKEND_COVERAGE_JOBS', 'NGB_FRONTEND_COVERAGE_JOBS']) {
    assert.match(process.env[name] ?? '1', /^[1-9][0-9]*$/, `${name} must be a positive integer.`)
  }
  const matrix = JSON.parse(await readFile(new URL('./matrix.json', import.meta.url), 'utf8'))
  const { unzipSync } = await import('fflate')
  const template = unzipSync(await readFile(join(candidate, 'nuget-release', `NGB.Platform.Templates.${matrix.target}.nupkg`)))
  const files = [await readFile(new URL('./Dockerfile.quality', import.meta.url), 'utf8')]
  for (const path of ['content/infrastructure/Dockerfile', 'content/web/Dockerfile']) files.push(new TextDecoder().decode(template[path]))
  const images = new Set([
    ...Object.values(matrix.infrastructure), 'postgres:16',
    'testcontainers/ryuk:0.14.0@sha256:7c1a8a9a47c780ed0f983770a662f80deb115d95cce3e2daa3d12115b8cd28f0',
  ])
  for (const file of files) {
    for (const match of file.matchAll(/^FROM\s+(\S+)/gm)) images.add(match[1])
  }
  const imageIds = {}
  for (const image of [...images].sort()) {
    try {
      imageIds[image] = await command('docker', ['image', 'inspect', image, '--format', '{{.Id}}'], { capture: true })
    } catch {
      await retryDownload(() => command('docker', ['pull', image], { timeout: 5 * 60_000 }))
      imageIds[image] = await command('docker', ['image', 'inspect', image, '--format', '{{.Id}}'], { capture: true })
    }
  }
  await command('docker', ['build', '-t', 'ngb-certification-quality:local', '-f', 'quality/external-consumer/Dockerfile.quality', 'quality/external-consumer'], { cwd: repository })
  imageIds.quality = await command('docker', ['image', 'inspect', 'ngb-certification-quality:local', '--format', '{{.Id}}'], { capture: true })
  const expected = `dotnet=${toolchain.dotnet}\nnode=v${toolchain.node}\nk6=${toolchain.k6}`
  const actual = await command('docker', ['run', '--rm', '--network', 'none', '--entrypoint', 'bash', imageIds.quality,
    '-euc', 'printf "dotnet=%s\\nnode=%s\\nk6=%s\\n" "$(dotnet --version)" "$(node --version)" "$(k6 version | cut -d " " -f 2 | cut -c 2-)"'], { capture: true })
  assert.equal(actual, expected, 'Host and Linux quality toolchains must match.')
  const dockerEndpoint = process.env.DOCKER_HOST && !process.env.DOCKER_CONTEXT
    ? process.env.DOCKER_HOST
    : await command('docker', ['context', 'inspect', '--format', '{{.Endpoints.docker.Host}}'], { capture: true })
  assert.ok(dockerEndpoint.startsWith('unix://'), 'Certification requires a local Unix Docker socket (Docker Desktop or Linux/WSL2).')
  const socketMount = `${dockerEndpoint.slice('unix://'.length)}:/var/run/docker.sock`
  // Docker Desktop may map the socket to a different group inside its Linux VM.
  const dockerSocketGroup = await command('docker', ['run', '--rm', '--network', 'none', '-v', socketMount,
    '--entrypoint', 'stat', imageIds.quality, '-c', '%g', '/var/run/docker.sock'], { capture: true })
  assert.match(dockerSocketGroup, /^\d+$/)
  assert.equal(await command('docker', ['run', '--rm', '--network', 'none',
    '--user', `${process.getuid()}:${process.getgid()}`, '--group-add', dockerSocketGroup,
    '-v', socketMount, '--entrypoint', 'curl', imageIds.quality,
    '--fail', '--silent', '--show-error', '--unix-socket', '/var/run/docker.sock', 'http://localhost/_ping'], { capture: true }), 'OK',
  'The quality container must be able to reach Docker before tests start.')
  await command('node', ['quality/node_modules/@playwright/test/cli.js', 'install', 'chromium'], { cwd: repository })
  const { chromium } = await import('@playwright/test')
  await access(chromium.executablePath())
  const browser = await chromium.launch({ headless: true })
  const browserVersion = browser.version()
  await browser.close()
  const sdks = await command('dotnet', ['--list-sdks'], { capture: true })
  const sdkRoot = sdks.split('\n').find(line => line.startsWith(`${toolchain.dotnet} [`))?.match(/\[(.+)\]/)?.[1]
  assert.ok(sdkRoot, 'Prepared .NET SDK location was not reported.')
  const compilerSha256 = await fileDigest(join(sdkRoot, toolchain.dotnet, 'Roslyn/bincore/Microsoft.CodeAnalysis.CSharp.dll'))
  const settings = Object.fromEntries(Object.entries(process.env).filter(([key]) =>
    /^(?:NGB_|DOTNET_|COMPlus_|NUGET_|MSBUILD|DOCKER_|TESTCONTAINERS_|PLAYWRIGHT_|npm_config_)/i.test(key)
    || ['NODE_OPTIONS', 'CI', 'TZ'].includes(key)).sort(([left], [right]) => left.localeCompare(right)))
  return {
    schemaVersion: 1, platform: platform(), architecture: arch(), kernel: release(), uid: process.getuid(), gid: process.getgid(),
    node: process.versions.node, nodeSha256: await fileDigest(process.execPath),
    dotnet: await command('dotnet', ['--info'], { capture: true }), compilerSha256,
    npm: await command('npm', ['--version'], { capture: true }),
    docker: await command('docker', ['version', '--format', '{{json .Server}}'], { capture: true }),
    dockerResources: JSON.parse(await command('docker', ['info', '--format',
      '{"id":{{json .ID}},"cpus":{{.NCPU}},"memory":{{.MemTotal}},"storage":{{json .Driver}}}'], { capture: true })),
    compose: await command('docker', ['compose', 'version', '--short'], { capture: true }),
    dockerEndpoint, dockerSocketGroup,
    images: imageIds, browser: browserVersion, browserSha256: await fileDigest(chromium.executablePath()),
    settingsSha256: digest(JSON.stringify(settings)),
  }
}
