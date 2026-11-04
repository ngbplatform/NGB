import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { createHash, randomUUID } from 'node:crypto'
import { lstat, mkdir, mkdtemp, readFile, readdir, realpath, rename, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { basename, dirname, isAbsolute, join, relative, resolve } from 'node:path'
import { initializeConfiguration } from '../configuration.mjs'
import { assertDeployment, exactVersion, localNugetConfig, planUpgrade, registryLockfile, toolVersion, withManagedIgnores } from './contracts.mjs'

const ignoredDirectories = new Set(['.git', '.ngb', '.ngb-packages', '.local', 'node_modules', 'bin', 'obj', 'dist', 'TestResults'])
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

async function replaceFile(path, bytes) {
  await mkdir(dirname(path), { recursive: true })
  const temporary = join(dirname(path), `.${basename(path)}.ngb-${randomUUID()}`)
  try {
    await writeFile(temporary, bytes, { flag: 'wx', mode: 0o600 })
    await rename(temporary, path)
  } finally {
    await rm(temporary, { force: true })
  }
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
  const files = {}
  async function visit(directory) {
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      if (ignoredDirectories.has(entry.name) || /^\.env(?:\.|$)/.test(entry.name)) continue
      const path = join(directory, entry.name)
      assert.ok(!entry.isSymbolicLink(), `Symbolic link requires manual migration: ${path}`)
      if (entry.isDirectory()) await visit(path)
      else files[relative(root, path).split('\\').join('/')] = await readFile(path)
    }
  }
  await visit(root)
  return files
}

export async function loadCandidate(root) {
  const manifest = JSON.parse(await readFile(join(root, 'release-manifest.json'), 'utf8'))
  assert.equal(manifest.schemaVersion, 1, 'Unknown candidate manifest format.')
  exactVersion(manifest.version)
  const files = {}
  for (const artifact of manifest.artifacts) {
    assert.match(artifact.path, /^(nuget-release\/[A-Za-z0-9.-]+\.(nupkg|snupkg)|npm\/ngbplatform-ui-\d+\.\d+\.\d+\.tgz)$/,
      'Unsafe candidate artifact path.')
    assert.ok(!Object.hasOwn(files, artifact.path), 'Duplicate candidate artifact.')
    const bytes = await readFile(join(root, artifact.path))
    assert.equal(hash(bytes), artifact.sha256, `Candidate changed: ${artifact.path}`)
    assert.equal(bytes.length, artifact.size, `Candidate size changed: ${artifact.path}`)
    files[artifact.path] = bytes
  }
  const template = `nuget-release/NGB.Platform.Templates.${manifest.version}.nupkg`
  const ui = `npm/ngbplatform-ui-${manifest.version}.tgz`
  assert.ok(files[template] && files[ui], 'Candidate must contain the template and UI package.')
  const integrity = `sha512-${createHash('sha512').update(files[ui]).digest('base64')}`
  assert.equal(manifest.artifacts.find(item => item.path === ui).integrity, integrity, 'Invalid UI integrity.')
  return { version: manifest.version, files, template, ui, integrity }
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
  const bytes = await optionalFile(join(root, '.ngb/packages.json'))
  let files = {}
  if (bytes) {
    const state = JSON.parse(bytes)
    assert.equal(state.schemaVersion, 1, 'Unknown local package state; review .ngb/packages.json before upgrading.')
    files = state.files
    for (const [path, expected] of Object.entries(files)) {
      assert.ok(path === 'NuGet.Local.Config' || path === 'web/.ngb-packages/ui.tgz'
        || /^\.ngb\/nuget\/NGB\.Platform\.[A-Za-z0-9.-]+\.nupkg$/.test(path), 'Unsafe managed package path.')
      await assertSafeDestination(root, path)
      const actual = await optionalFile(join(root, path))
      assert.ok(actual && hash(actual) === expected, `Managed package file was changed or removed: ${path}. Preserve your edits before upgrading.`)
    }
  }
  for (const path of ['NuGet.Local.Config', 'web/.ngb-packages/ui.tgz']) {
    assert.ok(!(await optionalFile(join(root, path))) || Object.hasOwn(files, path),
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

export async function buildApplication(root, version, candidate, updateLocks) {
  const cache = join(root, '.ngb/cache')
  const env = {
    NUGET_PACKAGES: join(cache, 'nuget'),
    NUGET_HTTP_CACHE_PATH: join(cache, 'nuget-http'),
  }
  const solutions = (await readdir(root)).filter(name => /\.slnx?$/.test(name))
  assert.equal(solutions.length, 1, 'Exactly one root solution is required.')
  run('dotnet', ['restore', solutions[0], updateLocks ? '--force-evaluate' : '--locked-mode',
    '--configfile', candidate ? 'NuGet.Local.Config' : 'NuGet.Config'], root, env)
  run('dotnet', ['build', solutions[0], '--no-restore', '-c', 'Release', '-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false'], root, env)

  const web = join(root, 'web')
  const npmArgs = ['--workspaces=false', '--cache', join(cache, 'npm')]
  if (candidate) run('npm', ['cache', 'add', join(web, '.ngb-packages/ui.tgz'), '--cache', join(cache, 'npm')], web)
  if (updateLocks) {
    const manifest = await readFile(join(web, 'package.json'), 'utf8')
    run('npm', ['install', '--package-lock-only', '--ignore-scripts', ...npmArgs,
      ...(candidate ? ['--save-exact', join(web, '.ngb-packages/ui.tgz')] : [])], web)
    await writeFile(join(web, 'package.json'), manifest)
    if (candidate) {
      const path = join(web, 'package-lock.json')
      await writeFile(path, registryLockfile(await readFile(path, 'utf8'), version, candidate.integrity))
    }
  }
  run('npm', ['ci', ...npmArgs], web)
  run('npm', ['run', 'build'], web)
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

export async function commitUpgrade(root, original, changes, plan, replace = replaceFile) {
  await assertUnchanged(root, original)
  await assertSafeDestination(root, '.ngb/upgrades')
  for (const path of Object.keys(changes)) await assertSafeDestination(root, path)
  const backup = join(root, '.ngb/upgrades', `${new Date().toISOString().replaceAll(':', '-')}-${randomUUID()}`)
  await mkdir(backup, { recursive: true, mode: 0o700 })
  const previous = {}
  for (const path of Object.keys(changes)) {
    previous[path] = await optionalFile(join(root, path))
    if (previous[path]) {
      await write(join(backup, 'before', path), previous[path])
    }
  }
  await write(join(backup, 'plan.json'), JSON.stringify({
    source: plan.source,
    target: plan.target,
    guide: plan.guide,
    files: Object.fromEntries(Object.entries(previous).map(([path, bytes]) => [path, bytes === null ? null : hash(bytes)])),
  }, null, 2))
  const applied = []
  try {
    for (const [path, bytes] of Object.entries(changes)) {
      const target = join(root, path)
      if (bytes === null) {
        await rm(target, { force: true })
      } else {
        await replace(target, bytes)
      }
      applied.push(path)
    }
  } catch (error) {
    for (const path of applied.reverse()) {
      if (previous[path] === null) await rm(join(root, path), { force: true })
      else await replaceFile(join(root, path), previous[path])
    }
    throw error
  }
  console.log(`Upgrade applied and builds passed. Original dependency files: ${backup}`)
  console.log('Database and running services were not changed. Review the diff, test your application, then deploy with a confirmed backup.')
}

export async function upgradeApplication(root, options, settings) {
  const original = await applicationFiles(root)
  const texts = Object.fromEntries(Object.entries(original).map(([path, bytes]) => [path, bytes.toString('utf8')]))
  const plan = planUpgrade(texts, options.to)
  const managedBefore = await managedPackageFiles(root)
  const candidate = options.local || options.packages ? await loadCandidate(settings.packagesDirectory) : null
  if (candidate) assert.equal(candidate.version, plan.target, 'Candidate version does not match --to.')
  console.log(`Upgrade ${plan.source} → ${plan.target} using ${candidate ? 'local packages' : 'configured registries'}.`)
  console.log(`Dependency changes: ${Object.keys(plan.changes).join(', ')}; NuGet/npm lockfiles will be regenerated.`)
  console.log(`Migration guide: ${plan.guide}`)
  if (!options.apply) {
    console.log('Plan only: no files or services changed. Add --apply to validate and apply this upgrade.')
    return
  }

  await assertSafeDestination(root, '.ngb/operation.lock')
  await mkdir(join(root, '.ngb'), { recursive: true, mode: 0o700 })
  const lock = join(root, '.ngb/operation.lock')
  await mkdir(lock)
  const stage = await mkdtemp(join(tmpdir(), 'ngb-upgrade-'))
  try {
    for (const [path, bytes] of Object.entries(original)) await write(join(stage, path), bytes)
    for (const [path, text] of Object.entries(plan.changes)) await write(join(stage, path), text)
    const managed = candidate ? await localPackages(stage, candidate) : {
      '.ngb/packages.json': Buffer.from(`${JSON.stringify({ schemaVersion: 1, mode: 'registry', version: plan.target, files: {} }, null, 2)}\n`),
    }
    for (const path of Object.keys(managedBefore)) {
      if (!Object.hasOwn(managed, path)) managed[path] = null
    }
    await buildApplication(stage, plan.target, candidate, true)
    const verified = await applicationFiles(stage)
    const changes = { ...managed }
    for (const [path, bytes] of Object.entries(verified)) {
      if (Object.hasOwn(plan.changes, path) || path.endsWith('packages.lock.json') || path === 'web/package-lock.json') {
        if (!original[path]?.equals(bytes)) changes[path] = bytes
      }
    }
    const ignore = texts['.gitignore'] ?? ''
    if (withManagedIgnores(ignore) !== ignore) changes['.gitignore'] = Buffer.from(withManagedIgnores(ignore))
    assert.deepEqual(await managedPackageFiles(root), managedBefore, 'Local package configuration changed during validation.')
    await commitUpgrade(root, original, changes, plan)
  } finally {
    await rm(stage, { recursive: true, force: true })
    await rm(lock, { recursive: true })
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
