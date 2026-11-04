import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { lstat, mkdir, mkdtemp, readFile, readdir, realpath, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { basename, dirname, isAbsolute, join, relative, resolve } from 'node:path'
import { initializeConfiguration } from '../configuration.mjs'
import { assertDeployment, exactVersion, localNugetConfig, planUpgrade, registryLockfile, toolVersion, withManagedIgnores } from './contracts.mjs'
import { readAsset, readBytes } from './kit-files.mjs'
import { verifyCliArchive } from './archive.mjs'
import { applyTransaction } from './transaction.mjs'
import { distinctPaths } from './kit-contracts.mjs'
import { initialUpgradeAssets, reviewedUpgradePlan } from './upgrade-assets.mjs'
import { materializeInitialKitUpgrade } from './upgrade-plan.mjs'
import { planSummary } from './kit-plan.mjs'
import { emitReport, report } from './kit.mjs'
import { executionEnvironment, runTool } from './process.mjs'

const ignoredDirectories = new Set(['.git', '.ngb', '.ngb-packages', '.local', 'node_modules', 'bin', 'obj', 'dist',
  'TestResults', 'coverage', 'test-results', 'playwright-report'])
const hash = bytes => createHash('sha256').update(bytes).digest('hex')

export function run(command, args, cwd, env = {}, capture = false) {
  return execFileSync(command, args, {
    cwd,
    env: { ...process.env, ...env },
    stdio: capture ? ['ignore', 'pipe', 'pipe'] : 'inherit',
    encoding: 'utf8',
    maxBuffer: 16 * 1024 * 1024,
  })
}

async function optionalFile(path) {
  try {
    return await readFile(path)
  } catch (error) {
    if (error.code !== 'ENOENT') throw error
    return null
  }
}

async function write(path, bytes) {
  await mkdir(dirname(path), { recursive: true })
  await writeFile(path, bytes)
}

async function assertSafeDestination(root, path) {
  assert.ok(!isAbsolute(path) && !path.split('/').includes('..'), 'Unsafe upgrade path.')
  let current = root
  for (const part of path.split('/')) {
    current = join(current, part)
    try {
      assert.ok(!(await lstat(current)).isSymbolicLink(), `Refusing to write through a symbolic link: ${current}`)
    } catch (error) {
      if (error.code !== 'ENOENT') throw error
    }
  }
}

export async function applicationFiles(root) {
  root = await realpath(root)
  const files = {}
  let count = 0
  let total = 0
  async function visit(directory, depth = 0) {
    assert.ok(depth <= 24, 'Application directory nesting exceeds its limit.')
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      if (ignoredDirectories.has(entry.name) || /^\.env(?:\.|$)/.test(entry.name)) continue
      count += 1
      assert.ok(count <= 8192, 'Application directory exceeds its entry limit.')
      const path = join(directory, entry.name)
      assert.ok(!entry.isSymbolicLink(), `Symbolic link requires manual migration: ${path}`)
      if (entry.isDirectory()) await visit(path, depth + 1)
      else {
        const key = relative(root, path).split('\\').join('/')
        const bytes = await readBytes(root, key, 128 * 1024 * 1024)
        assert.ok(bytes, `Application file disappeared during inspection: ${key}`)
        total += bytes.length
        assert.ok(total <= 512 * 1024 * 1024, 'Application files exceed the inspection size limit.')
        files[key] = bytes
      }
    }
  }
  await visit(root)
  distinctPaths(Object.keys(files))
  return files
}

export async function loadCandidate(root) {
  root = await realpath(root)
  const text = await readAsset(root, 'release-manifest.json')
  assert.ok(text, 'The candidate is missing release-manifest.json.')
  const manifest = JSON.parse(text)
  assert.equal(manifest.schemaVersion, 1, 'Unknown candidate manifest format.')
  exactVersion(manifest.version)
  assert.ok(Array.isArray(manifest.artifacts) && manifest.artifacts.length <= 256, 'Invalid candidate artifact inventory.')
  const files = {}
  let total = 0
  for (const artifact of manifest.artifacts) {
    assert.match(artifact.path, /^(nuget-release\/NGB\.Platform\.[A-Za-z0-9.-]+\.(nupkg|snupkg)|npm\/ngbplatform-(?:ui|cli)-\d+\.\d+\.\d+\.tgz)$/,
      'Unsafe candidate artifact path.')
    assert.ok(artifact.path.endsWith(`.${manifest.version}.nupkg`) || artifact.path.endsWith(`.${manifest.version}.snupkg`)
      || artifact.path.endsWith(`-${manifest.version}.tgz`), 'Candidate artifact versions must agree with the manifest.')
    assert.ok(!Object.hasOwn(files, artifact.path), 'Duplicate candidate artifact.')
    assert.ok(Number.isSafeInteger(artifact.size) && artifact.size > 0 && artifact.size <= 128 * 1024 * 1024, 'Invalid candidate artifact size.')
    total += artifact.size
    assert.ok(total <= 256 * 1024 * 1024, 'Candidate package set exceeds its aggregate size limit.')
    const bytes = await readBytes(root, artifact.path, artifact.size)
    assert.ok(bytes, `Candidate artifact is missing: ${artifact.path}`)
    assert.equal(hash(bytes), artifact.sha256, `Candidate changed: ${artifact.path}`)
    assert.equal(bytes.length, artifact.size, `Candidate size changed: ${artifact.path}`)
    files[artifact.path] = bytes
  }
  const template = `nuget-release/NGB.Platform.Templates.${manifest.version}.nupkg`
  const ui = `npm/ngbplatform-ui-${manifest.version}.tgz`
  const cli = `npm/ngbplatform-cli-${manifest.version}.tgz`
  assert.ok(files[template] && files[ui], 'Candidate must contain the template and UI package.')
  const integrity = `sha512-${createHash('sha512').update(files[ui]).digest('base64')}`
  assert.equal(manifest.artifacts.find(item => item.path === ui).integrity, integrity, 'Invalid UI integrity.')
  const [major, minor] = manifest.version.split('.').map(Number)
  if (major > 3 || major === 3 && minor >= 4 || files[cli]) {
    assert.ok(files[cli], 'AI Kit candidates must include the matching CLI archive.')
    assert.equal(manifest.artifacts.find(item => item.path === cli).integrity,
      `sha512-${createHash('sha512').update(files[cli]).digest('base64')}`, 'Invalid CLI integrity.')
    verifyCliArchive(files[cli], manifest.version)
  }
  return { version: manifest.version, files, template, ui, cli, integrity, manifestSha256: hash(text) }
}

async function localPackages(root, candidate) {
  const changes = {}
  for (const [path, bytes] of Object.entries(candidate.files)) {
    if (path.endsWith('.nupkg')) changes[`.ngb/nuget/${path.split('/').at(-1)}`] = bytes
  }
  changes['web/.ngb-packages/ui.tgz'] = candidate.files[candidate.ui]
  changes['NuGet.Local.Config'] = Buffer.from(localNugetConfig(await readFile(join(root, 'NuGet.Config'), 'utf8')))
  changes['.ngb/packages.json'] = Buffer.from(`${JSON.stringify({
    schemaVersion: 1,
    version: candidate.version,
    mode: 'local',
    integrity: candidate.integrity,
    files: Object.fromEntries(Object.entries(changes).map(([path, bytes]) => [path, hash(bytes)])),
  }, null, 2)}\n`)
  for (const [path, bytes] of Object.entries(changes)) await write(join(root, path), bytes)
  return changes
}

export async function managedPackageFiles(root) {
  root = await realpath(root)
  const bytes = await readAsset(root, '.ngb/packages.json')
  let files = {}
  if (bytes) {
    const state = JSON.parse(bytes)
    assert.equal(state.schemaVersion, 1, 'Unknown local package state; review .ngb/packages.json before upgrading.')
    files = state.files
    assert.ok(files && typeof files === 'object' && !Array.isArray(files) && Object.keys(files).length <= 256,
      'Invalid managed package inventory.')
    let total = 0
    for (const [path, expected] of Object.entries(files)) {
      assert.ok(path === 'NuGet.Local.Config' || path === 'web/.ngb-packages/ui.tgz'
        || /^\.ngb\/nuget\/NGB\.Platform\.[A-Za-z0-9.-]+\.nupkg$/.test(path), 'Unsafe managed package path.')
      await assertSafeDestination(root, path)
      assert.match(expected, /^[a-f0-9]{64}$/, 'Invalid managed package identity.')
      const actual = await readBytes(root, path, 128 * 1024 * 1024)
      assert.ok(actual && hash(actual) === expected, `Managed package file was changed or removed: ${path}. Preserve your edits before upgrading.`)
      total += actual.length
      assert.ok(total <= 256 * 1024 * 1024, 'Managed packages exceed their aggregate size limit.')
    }
  }
  for (const path of ['NuGet.Local.Config', 'web/.ngb-packages/ui.tgz']) {
    assert.ok(!(await readBytes(root, path, 128 * 1024 * 1024)) || Object.hasOwn(files, path),
      `Existing ${path} is user-owned. Move or rename it before using managed local packages.`)
  }
  return files
}

async function verifyDependencies(root, version) {
  const files = await applicationFiles(root)
  let lockfiles = 0
  for (const [path, bytes] of Object.entries(files)) {
    if (!path.endsWith('/packages.lock.json')) continue
    lockfiles += 1
    const lock = JSON.parse(bytes)
    for (const framework of Object.values(lock.dependencies)) {
      for (const [name, dependency] of Object.entries(framework)) {
        if (name.startsWith('NGB.Platform.')) {
          assert.equal(dependency.resolved, version, `Unexpected resolved version: ${name} in ${path}`)
        }
      }
    }
  }
  assert.ok(lockfiles > 0, 'No NuGet lockfiles were generated.')
  const lock = JSON.parse(await readFile(join(root, 'web/package-lock.json'), 'utf8'))
  assert.equal(lock.packages['node_modules/@ngbplatform/ui'].version, version)
}

export async function buildApplication(root, version, candidate, updateLocks, signal, { quiet = false } = {}) {
  const cache = join(root, '.ngb/cache')
  const env = executionEnvironment(cache)
  const execute = async (tool, args, cwd) => {
    const result = await runTool(tool, args, { cwd, env, signal, maxBytes: 16 * 1024 * 1024 })
    assert.equal(result.state, 'passed', `${tool} ${args[0]} failed (${result.reason ?? result.exitCode}): ${result.output}`)
    if (!quiet && result.output) process.stdout.write(result.output)
  }
  const solutions = (await readdir(root)).filter(name => /\.slnx?$/.test(name))
  assert.equal(solutions.length, 1, 'Exactly one root solution is required.')
  await execute('dotnet', ['restore', solutions[0], updateLocks ? '--force-evaluate' : '--locked-mode',
    '--configfile', candidate ? 'NuGet.Local.Config' : 'NuGet.Config', '-m:1', '-nodeReuse:false'], root)
  await execute('dotnet', ['build', solutions[0], '--no-restore', '-c', 'Release', '-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false'], root)
  const helper = 'infrastructure/validation/Ngb.Validation.csproj'
  if (await readAsset(await realpath(root), helper)) {
    await execute('dotnet', ['restore', helper, '--locked-mode', '--configfile',
      candidate ? 'NuGet.Local.Config' : 'NuGet.Config', '-m:1', '-nodeReuse:false'], root)
    await execute('dotnet', ['build', helper, '--no-restore', '-c', 'Release', '-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false'], root)
  }

  const web = join(root, 'web')
  const npmArgs = ['--workspaces=false', '--cache', join(cache, 'npm')]
  if (candidate) await execute('npm', ['cache', 'add', join(web, '.ngb-packages/ui.tgz'), '--cache', join(cache, 'npm')], web)
  if (updateLocks) {
    const manifest = await readFile(join(web, 'package.json'), 'utf8')
    await execute('npm', ['install', '--package-lock-only', '--ignore-scripts', ...npmArgs,
      ...(candidate ? ['--save-exact', join(web, '.ngb-packages/ui.tgz')] : [])], web)
    await writeFile(join(web, 'package.json'), manifest)
    if (candidate) {
      const path = join(web, 'package-lock.json')
      await writeFile(path, registryLockfile(await readFile(path, 'utf8'), version, candidate.integrity))
    }
  }
  await execute('npm', ['ci', ...npmArgs], web)
  await execute('npm', ['run', 'build'], web)
  await verifyDependencies(root, version)
}

export async function createApplication(options, settings) {
  const candidate = options.local || options.packages ? await loadCandidate(settings.packagesDirectory) : null
  const version = exactVersion(options.version ?? candidate?.version ?? toolVersion)
  if (candidate) assert.equal(candidate.version, version, 'Candidate version does not match --version.')
  const destination = resolve(options.output ?? join(settings.outputDirectory, options.name))
  await mkdir(dirname(destination), { recursive: true })
  if (settings.repository) {
    const path = relative(await realpath(settings.repository), join(await realpath(dirname(destination)), basename(destination)))
    assert.ok(path === '..' || path.startsWith('../') || isAbsolute(path), 'Create the independent application outside the NGB repository.')
  }
  await mkdir(destination)
  const temporary = await mkdtemp(join(tmpdir(), 'ngb-create-'))
  try {
    const hive = join(temporary, 'templates')
    let template = `NGB.Platform.Templates::${version}`
    if (candidate) {
      template = join(temporary, `NGB.Platform.Templates.${version}.nupkg`)
      await writeFile(template, candidate.files[candidate.template])
    }
    run('dotnet', ['new', 'install', template, '--debug:custom-hive', hive], temporary)
    run('dotnet', ['new', 'ngb', '-n', options.name, '-o', destination, '--debug:custom-hive', hive], temporary)
    if (candidate) {
      assert.match(await readFile(join(destination, 'infrastructure/Dockerfile'), 'utf8'), /ARG NUGET_CONFIG=NuGet.Config/,
        'Rebuild the candidate: this template predates local-package support.')
      await localPackages(destination, candidate)
    }
    await initializeConfiguration(join(destination, '.env'), options.email ?? 'administrator@example.com')
    if (candidate) await writeFile(join(destination, '.env'), 'NGB_NUGET_CONFIG=NuGet.Local.Config\n', { flag: 'a' })
    console.log(`Created ${destination}. Private login credentials are in its .env file.`)
    if (options.start) await startApplication(destination, false)
    else console.log(`Start from that directory: node infrastructure/ngb.mjs start`)
  } finally {
    await rm(temporary, { recursive: true, force: true })
  }
}

async function assertUnchanged(root, original) {
  const current = await applicationFiles(root)
  assert.deepEqual(Object.keys(current).sort(), Object.keys(original).sort(), 'Application files changed during validation; retry the upgrade.')
  for (const [path, bytes] of Object.entries(original)) {
    assert.equal(hash(current[path]), hash(bytes), `Application changed during validation: ${path}`)
  }
}

export async function commitUpgrade(root, original, changes, plan, replace = null, managedSnapshot = {}, { quiet = false } = {}) {
  root = await realpath(root)
  await assertUnchanged(root, original)
  await assertSafeDestination(root, '.ngb/transactions')
  for (const path of Object.keys(changes)) await assertSafeDestination(root, path)
  const snapshot = { ...managedSnapshot }
  for (const path of Object.keys(changes)) snapshot[path] = original[path] ?? managedSnapshot[path] ?? null
  const receipt = await applyTransaction(root, snapshot, changes, hash(JSON.stringify(plan)), {
    replaceFile: replace,
    beforeCommit: () => assertUnchanged(root, original),
  })
  if (!quiet) {
    console.log(`Upgrade applied and builds passed. Recovery receipt: .ngb/transactions/${receipt.id}/receipt.json`)
    console.log('Database and running services were not changed. Review the diff, test your application, then deploy with a confirmed backup.')
  }
  return receipt
}

export async function upgradeApplication(root, options, settings, signal) {
  const original = await applicationFiles(root)
  const quiet = options.format === 'json'
  const texts = Object.fromEntries(Object.entries(original).map(([path, bytes]) => [path, bytes.toString('utf8')]))
  const plan = planUpgrade(texts, options.to)
  const managedBefore = await managedPackageFiles(root)
  const managedSnapshot = {}
  for (const path of [...Object.keys(managedBefore), '.ngb/packages.json']) {
    managedSnapshot[path] = await readBytes(await realpath(root), path, 128 * 1024 * 1024)
  }
  const candidate = options.local || options.packages ? await loadCandidate(settings.packagesDirectory) : null
  if (candidate) assert.equal(candidate.version, plan.target, 'Candidate version does not match --to.')
  let assets = null
  let assetChanges = {}
  if (plan.source === '3.3.0') {
    assets = await initialUpgradeAssets(root, original, plan, candidate, signal)
    assets.plan = await reviewedUpgradePlan(assets.plan, options.resolutions)
    const conflicts = assets.plan.entries.filter(entry => ['conflict', 'deleted'].includes(entry.state))
    const value = report('upgrade', [{ id: 'kit.conflicts', required: true, state: conflicts.length ? 'failed' : 'passed',
      message: `${conflicts.length} unresolved CLI/Kit ownership conflicts.` }], {
      sourceVersion: plan.source, targetVersion: plan.target, plan: planSummary(assets.plan),
      dependencies: Object.keys(plan.changes), applied: false,
    })
    if (!options.apply || conflicts.length) {
      emitReport(value, options.format)
      if (options.format !== 'json') console.log('Plan only: no files or services changed. Review CLI/Kit and dependency changes before --apply.')
      return
    }
    assetChanges = materializeInitialKitUpgrade(assets.plan, assets.current)
  } else {
    assert.ok(!options.resolutions, 'Historical dependency-only upgrades do not accept Kit conflict resolutions.')
  }
  if (!quiet) {
    console.log(`Upgrade ${plan.source} → ${plan.target} using ${candidate ? 'local packages' : 'configured registries'}.`)
    console.log(`Dependency changes: ${Object.keys(plan.changes).join(', ')}; NuGet/npm lockfiles will be regenerated.`)
    console.log(`Migration guide: ${plan.guide}`)
  }
  if (!options.apply) {
    if (quiet) emitReport(report('upgrade', [], { sourceVersion: plan.source, targetVersion: plan.target,
      dependencies: Object.keys(plan.changes), applied: false }), 'json')
    else console.log('Plan only: no files or services changed. Add --apply to validate and apply this upgrade.')
    return
  }

  const stage = await mkdtemp(join(tmpdir(), 'ngb-upgrade-'))
  try {
    for (const [path, bytes] of Object.entries(original)) await write(join(stage, path), bytes)
    for (const [path, text] of Object.entries(plan.changes)) await write(join(stage, path), text)
    for (const [path, text] of Object.entries(assetChanges)) {
      if (text === null) await rm(join(stage, path), { force: true })
      else await write(join(stage, path), text)
    }
    const managed = candidate ? await localPackages(stage, candidate) : {
      '.ngb/packages.json': Buffer.from(`${JSON.stringify({ schemaVersion: 1, mode: 'registry', version: plan.target, files: {} }, null, 2)}\n`),
    }
    for (const path of Object.keys(managedBefore)) {
      if (!Object.hasOwn(managed, path)) managed[path] = null
    }
    await buildApplication(stage, plan.target, candidate, true, signal, { quiet })
    const verified = await applicationFiles(stage)
    const changes = { ...managed }
    for (const [path, bytes] of Object.entries(verified)) {
      if (Object.hasOwn(plan.changes, path) || Object.hasOwn(assetChanges, path)
        || path.endsWith('packages.lock.json') || path === 'web/package-lock.json') {
        if (!original[path]?.equals(bytes)) changes[path] = bytes
      }
    }
    for (const [path, value] of Object.entries(assetChanges)) {
      if (value === null) changes[path] = null
    }
    const ignore = texts['.gitignore'] ?? ''
    if (withManagedIgnores(ignore) !== ignore) changes['.gitignore'] = Buffer.from(withManagedIgnores(ignore))
    assert.deepEqual(await managedPackageFiles(root), managedBefore, 'Local package configuration changed during validation.')
    signal?.throwIfAborted()
    const receipt = await commitUpgrade(root, original, changes,
      assets ? { ...plan, kitPlanSha256: assets.plan.sha256 } : plan, null, managedSnapshot, { quiet })
    if (quiet || assets) {
      const customized = assets?.plan.entries.filter(entry => entry.result !== entry.incoming).length ?? 0
      emitReport(report('upgrade', [{ id: 'upgrade.build', required: true, state: 'passed', message: 'Isolated backend, frontend and supplied helper builds passed.' },
        { id: 'kit.customizations', required: true, state: customized ? 'not-run' : 'passed',
          message: customized ? `${customized} preserved guidance customizations need application review.` : 'No customized incoming contracts.' }],
      { sourceVersion: plan.source, targetVersion: plan.target, applied: true, receipt }), options.format)
    }
  } finally {
    await rm(stage, { recursive: true, force: true })
  }
}

export async function startApplication(root, deploy) {
  const packages = await optionalFile(join(root, '.ngb/packages.json'))
  const local = packages !== null && JSON.parse(packages).mode === 'local'
  const env = { NGB_NUGET_CONFIG: local ? 'NuGet.Local.Config' : 'NuGet.Config' }
  const compose = args => run('docker', ['compose', ...args], root, env)
  const configuration = JSON.parse(run('docker', ['compose', 'config', '--format', 'json'], root, env, true))
  assertDeployment(configuration.services)
  if (!deploy) {
    const active = run('docker', ['compose', 'ps', '--quiet', '--all'], root, env, true).trim()
    assert.equal(active, '', 'An existing deployment was found. Use deploy --backup-confirmed for an upgrade, or docker compose start to resume it.')
  }
  compose(['build'])
  if (deploy) {
    compose(['stop', 'api', 'jobs', 'web'])
    compose(['run', '--rm', '--no-deps', 'migrator'])
    compose(['up', '-d', '--no-deps', 'api', 'jobs', 'web'])
  } else {
    compose(['up', '-d'])
  }
  console.log('Application started. Open http://localhost:5182; inspect docker compose ps and logs for readiness.')
}
