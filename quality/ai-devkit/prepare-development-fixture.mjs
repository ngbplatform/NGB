import assert from 'node:assert/strict'
import { mkdir, readFile, realpath, rm, writeFile } from 'node:fs/promises'
import { basename, dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { unzipSync } from 'fflate'
import { applicationFiles, loadCandidate } from '../../packaging/templates/content/infrastructure/ngb/application.mjs'
import { verifyCliArchive } from '../../packaging/templates/content/infrastructure/ngb/archive.mjs'
import { localNugetConfig } from '../../packaging/templates/content/infrastructure/ngb/contracts.mjs'
import { digest, portablePath } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { validateInstalled } from '../../packaging/templates/content/infrastructure/ngb/kit-plan.mjs'
import { validateConfiguration } from '../../packaging/templates/content/infrastructure/ngb/project-data.mjs'
import { executionEnvironment, runTool } from '../../packaging/templates/content/infrastructure/ngb/process.mjs'
import { assertConsumerIsolation, assertOutsideRepository } from '../external-consumer/contracts.mjs'

assert.equal(process.argv.length, 5,
  'Usage: node prepare-development-fixture.mjs <task> <sealed-candidate> <new-external-directory>')
const task = process.argv[2]
assert.ok(['existing-document-change', 'new-catalog', 'report-extension', 'permission-correction'].includes(task),
  'Unknown package-only development fixture.')
const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const candidate = await loadCandidate(resolve(process.argv[3]))
const output = resolve(process.argv[4])
assertOutsideRepository(repository, output)
assertOutsideRepository(repository, await realpath(dirname(output)))
const matrixBytes = await readFile(new URL('./agent-evaluations.json', import.meta.url))
const matrix = JSON.parse(matrixBytes)
assert.equal(matrix.version, candidate.version)
assert.equal(process.version, 'v24.19.0', 'Prepare evaluation fixtures with the pinned Node toolchain.')
const contractName = task === 'existing-document-change' ? 'document-change' : task
const contractBytes = await readFile(new URL(`./evaluation-fixtures/${contractName}.contract.json`, import.meta.url))
const contract = JSON.parse(contractBytes)
assert.equal(contract.prompt, matrix.tasks.find(item => item.id === task)?.prompt)
const cli = verifyCliArchive(candidate.files[candidate.cli], candidate.version)
const payloadBytes = cli.files.get('package/infrastructure/ngb/kit-payload.json')
const payload = JSON.parse(payloadBytes)
const template = unzipSync(candidate.files[candidate.template])
validateInstalled(JSON.parse(Buffer.from(template['content/ai/kit.json'])), payload)
const files = {}
const add = (path, bytes) => {
  portablePath(path)
  assert.ok(!Object.hasOwn(files, path), `Duplicate seed file: ${path}`)
  files[path] = Buffer.from(bytes)
}
const prefix = 'ai/vendor/examples/equipment-inspection/'
for (const file of payload.files.filter(file => file.path.startsWith(prefix))) {
  add(file.path.slice(prefix.length), file.content)
}
assert.ok(files['EquipmentInspection.slnx'], 'The candidate is missing the independent application example.')
const props = files['Directory.Build.props'].toString('utf8')
const declarations = [...props.matchAll(/<NgbPlatformVersion(?: Condition="[^"]*")?>[^<]+<\/NgbPlatformVersion>/g)]
assert.equal(declarations.length, 1, 'The example must declare one platform version.')
files['Directory.Build.props'] = Buffer.from(props.replace(declarations[0][0],
  `<NgbPlatformVersion>${candidate.version}</NgbPlatformVersion>`))
for (const [name, bytes] of Object.entries(template)) {
  if (!name.startsWith('content/') || name.endsWith('/')) continue
  const path = name.slice('content/'.length)
  if (['AGENTS.md', 'CLAUDE.md', '.gitattributes'].includes(path)
    || /^(?:ai|\.agents|\.claude)\//.test(path)
    || /^infrastructure\/(?:ngb\.mjs|configuration\.mjs|ngb\/|validation\/)/.test(path)) {
    add(path, bytes)
  }
}
for (const file of payload.files.filter(file => file.role === 'file')) {
  assert.equal(files[file.path]?.toString('utf8'), file.content, `Installed guidance differs from the candidate: ${file.path}`)
}
for (const [path, bytes] of cli.files) {
  if (path.startsWith('package/infrastructure/')) {
    assert.deepEqual(files[path.slice('package/'.length)], bytes, `CLI/template bytes differ: ${path}`)
  }
}
const roles = {
  Api: 'api', BackgroundJobs: 'worker', Migrator: 'migrator', Setup: 'worker',
  Definitions: 'definitions', Runtime: 'runtime', PostgreSql: 'persistence', Tests: 'tests', IntegrationTests: 'tests',
}
const configuration = JSON.parse(Buffer.from(template['content/ngb.config.json']).toString('utf8')
  .replaceAll('NgbApplication', 'EquipmentInspection'))
configuration.projects = Object.keys(files).filter(path => path.endsWith('.csproj') && !path.startsWith('ai/'))
  .map(path => ({ path, role: path.startsWith('infrastructure/') ? 'tooling'
    : path.startsWith('MigrationFixtures/') ? 'tooling' : roles[dirname(path).split('.').at(-1)] }))
validateConfiguration(configuration)
add('ngb.config.json', JSON.stringify(configuration, null, 2) + '\n')
files['NuGet.Config'] = Buffer.from(template['content/NuGet.Config'])
files['EquipmentInspection.slnx'] = Buffer.from(files['EquipmentInspection.slnx'].toString('utf8')
  .replace('</Solution>', '  <Project Path="infrastructure/validation/Ngb.Validation.csproj" />\n</Solution>'))
const overlay = task === 'report-extension' ? 'report-seed' : task === 'permission-correction' ? 'permission-seed' : null
let overlaySha256 = null
if (overlay) {
  const manifestBytes = await readFile(new URL(`./evaluation-fixtures/${overlay}.json`, import.meta.url))
  const manifest = JSON.parse(manifestBytes)
  assert.equal(manifest.task, task)
  overlaySha256 = digest(manifestBytes)
  for (const [path, expected] of Object.entries(manifest.files)) {
    portablePath(path)
    const bytes = await readFile(new URL(`./evaluation-fixtures/${overlay}/${path}`, import.meta.url))
    assert.equal(digest(bytes), expected, `Frozen seed overlay changed: ${path}`)
    files[path] = bytes
  }
}
await mkdir(output, { mode: 0o700 })
const root = await realpath(output)
const application = join(root, 'application')
for (const [path, bytes] of Object.entries(files)) {
  const target = join(application, path)
  await mkdir(dirname(target), { recursive: true })
  await writeFile(target, bytes, { flag: 'wx' })
}
await assertConsumerIsolation(application, repository)
await mkdir(join(root, 'feed'))
for (const [path, bytes] of Object.entries(candidate.files).filter(([path]) => path.endsWith('.nupkg'))) {
  await writeFile(join(root, 'feed', basename(path)), bytes, { mode: 0o400, flag: 'wx' })
}
await writeFile(join(root, 'ui.tgz'), candidate.files[candidate.ui], { mode: 0o400, flag: 'wx' })
// Use the same manifest-bound local package layout as the public CLI. Keep
// the primary registry configuration portable and install locks before freezing
// the agent's initial source inventory.
const managed = {
  'NuGet.Local.Config': Buffer.from(localNugetConfig(files['NuGet.Config'].toString('utf8'))),
  'web/.ngb-packages/ui.tgz': candidate.files[candidate.ui],
}
for (const [path, bytes] of Object.entries(candidate.files).filter(([path]) => path.endsWith('.nupkg'))) {
  managed[`.ngb/nuget/${basename(path)}`] = bytes
}
for (const [path, bytes] of Object.entries(managed)) {
  await mkdir(dirname(join(application, path)), { recursive: true })
  await writeFile(join(application, path), bytes, { flag: 'wx', mode: 0o400 })
}
const packageState = JSON.stringify({ schemaVersion: 1, version: candidate.version, mode: 'local',
  integrity: candidate.integrity,
  files: Object.fromEntries(Object.entries(managed).map(([path, bytes]) => [path, digest(bytes)])),
}, null, 2) + '\n'
await writeFile(join(application, '.ngb/packages.json'), packageState, { flag: 'wx', mode: 0o400 })
const environment = executionEnvironment(join(root, 'preparation-home'))
const invoke = async (name, tool, args) => {
  const result = await runTool(tool, args, { cwd: application, env: environment,
    timeoutMs: 600000, maxBytes: 8 * 1024 * 1024 })
  await writeFile(join(root, `${name}.log`), result.output, { flag: 'wx', mode: 0o600 })
  assert.equal(result.state, 'passed', `Fixture preparation failed: ${name}.`)
  return result.output
}
assert.equal((await invoke('dotnet-version', 'dotnet', ['--version'])).trim(), '10.0.401')
await invoke('restore', 'dotnet', ['restore', 'EquipmentInspection.slnx', '--use-lock-file',
  '--configfile', 'NuGet.Local.Config', '-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false'])
const validationBytes = await invoke('validate', 'node', ['infrastructure/ngb.mjs', 'validate', '--format', 'json'])
const validation = JSON.parse(validationBytes)
assert.equal(validation.status, 'passed', 'The prepared application must pass installed CLI inspection before inference.')
assert.ok(validation.checks.length > 0 && validation.checks.every(check => check.state === 'passed'))
// Lockfiles are portable source inputs. Restore outputs contain absolute cache
// and SDK paths and must be regenerated in the agent's own build environment.
for (const project of configuration.projects) {
  await rm(join(application, dirname(project.path), 'obj'), { recursive: true, force: true })
}
await assertConsumerIsolation(application, repository)
const before = Object.fromEntries(Object.entries(await applicationFiles(application))
  .map(([path, bytes]) => [path, digest(bytes)]).sort(([left], [right]) => left.localeCompare(right, 'en')))
const beforeBytes = JSON.stringify(before, null, 2) + '\n'
await writeFile(join(root, 'before.json'), beforeBytes, { mode: 0o600, flag: 'wx' })
await writeFile(join(root, 'task.json'), contractBytes, { mode: 0o600, flag: 'wx' })
const report = {
  schemaVersion: 1,
  task,
  status: 'prepared-not-evaluated',
  candidateSha256: candidate.manifestSha256,
  matrixSha256: digest(matrixBytes),
  contractSha256: digest(contractBytes),
  kitSha256: digest(payloadBytes),
  overlaySha256,
  runnerSha256: digest(await readFile(fileURLToPath(import.meta.url))),
  beforeSha256: digest(beforeBytes),
  managedPackagesSha256: digest(packageState),
  validationSha256: digest(validationBytes),
  sourceTreeSha256: digest(JSON.stringify(Object.entries(before).sort())),
  application,
  sourceFiles: Object.keys(before).length,
  applicationConfiguration: ['exact-unconditional-sdk-version', 'manifest-bound-local-packages', 'locked-dependencies',
    'complete-solution-and-project-roles', 'installed-cli-inspection'],
}
await writeFile(join(root, 'fixture.json'), JSON.stringify(report, null, 2) + '\n', { mode: 0o600, flag: 'wx' })
console.log(JSON.stringify(report, null, 2))
