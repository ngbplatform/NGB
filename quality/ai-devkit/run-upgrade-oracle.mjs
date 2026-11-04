import assert from 'node:assert/strict'
import { mkdir, mkdtemp, readFile, realpath, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { basename, dirname, join, relative, resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'
import { applicationFiles, loadCandidate } from '../../packaging/templates/content/infrastructure/ngb/application.mjs'
import { verifyCliArchive } from '../../packaging/templates/content/infrastructure/ngb/archive.mjs'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { executionEnvironment, runTool } from '../../packaging/templates/content/infrastructure/ngb/process.mjs'
import { assertConsumerIsolation, assertOutsideRepository, verifyFrozenFixture } from '../external-consumer/contracts.mjs'
import { inventory, seedOverlay, verifyPreview, verifyUpgrade } from './evaluation-oracles/customized-kit-upgrade.mjs'

assert.equal(process.argv.length, 7,
  'Usage: node run-upgrade-oracle.mjs <candidate> <application> <trusted-fixture-directory> <new-output> <reviewed-tree-sha256>')
assert.match(process.version, /^v24\./)
const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const candidate = await loadCandidate(resolve(process.argv[2]))
const application = await realpath(resolve(process.argv[3]))
assertOutsideRepository(repository, application)
const fixtureRoot = await realpath(resolve(process.argv[4]))
const output = resolve(process.argv[5])
const outside = relative(application, output)
assert.ok(outside === '..' || outside.startsWith('..' + sep), 'Evidence must stay outside the application.')
const fixtureBytes = await readFile(join(fixtureRoot, 'fixture.json'))
const fixture = JSON.parse(fixtureBytes)
assert.equal(fixture.task, 'customized-kit-upgrade')
assert.equal(fixture.candidateSha256, candidate.manifestSha256)
const sourceBytes = await readFile(join(repository, 'quality/upgrade-certification/source-3.3.0.json'))
assert.equal(fixture.sourceFixtureSha256, digest(sourceBytes))
const frozen = join(repository, 'quality/upgrade-certification/fixtures/3.3.0')
await verifyFrozenFixture(frozen, JSON.parse(sourceBytes))
const before = { ...await applicationFiles(frozen), ...seedOverlay }
const beforeBytes = await readFile(join(fixtureRoot, 'before.json'))
assert.equal(digest(beforeBytes), fixture.beforeSha256)
assert.deepEqual(JSON.parse(beforeBytes), inventory(before))
const previewBytes = await readFile(join(fixtureRoot, 'preview.log'))
assert.equal(digest(previewBytes), fixture.previewSha256)
const preview = JSON.parse(previewBytes)
verifyPreview(inventory(before), JSON.parse(beforeBytes), preview)
assert.equal(preview.plan.planSha256, fixture.previewPlanSha256)
const cli = verifyCliArchive(candidate.files[candidate.cli], candidate.version)
const tooling = Object.fromEntries([...cli.files].filter(([path]) => path.startsWith('package/infrastructure/'))
  .map(([path, bytes]) => [path.slice('package/'.length), bytes.toString('utf8')]))
const payload = JSON.parse(tooling['infrastructure/ngb/kit-payload.json'])
const produced = await applicationFiles(application)
const reviewedTree = digest(JSON.stringify(Object.entries(inventory(produced)).sort()))
assert.equal(reviewedTree, process.argv[6], 'Review the exact produced source before executing it.')
const oracleBytes = await readFile(new URL('./evaluation-oracles/customized-kit-upgrade.mjs', import.meta.url))
assert.equal(fixture.oracleSha256, digest(oracleBytes), 'Recreate the fixture after changing its frozen oracle.')
const contractBytes = await readFile(new URL('./evaluation-fixtures/customized-kit-upgrade.contract.json', import.meta.url))
assert.equal(fixture.contractSha256, digest(contractBytes))
await mkdir(output, { mode: 0o700 })
const stage = await realpath(await mkdtemp(join(tmpdir(), 'ngb-upgrade-oracle-')))
const root = join(stage, 'application')
const environment = executionEnvironment(join(stage, 'home'))
const abort = new AbortController()
const cancel = () => abort.abort()
process.once('SIGINT', cancel)
process.once('SIGTERM', cancel)
const report = {
  schemaVersion: 1,
  task: 'customized-kit-upgrade',
  status: 'not-ready',
  candidateSha256: candidate.manifestSha256,
  oracleSha256: digest(oracleBytes),
  runnerSha256: digest(await readFile(fileURLToPath(import.meta.url))),
  producedTreeSha256: reviewedTree,
  node: process.version,
  stage,
  startedAt: new Date().toISOString(),
  artifacts: {},
  agentExecution: 'not-evaluated-by-this-oracle',
}
const save = async (path, bytes) => {
  const target = join(output, path)
  await mkdir(dirname(target), { recursive: true })
  await writeFile(target, bytes, { flag: 'wx', mode: 0o600 })
  report.artifacts[path] = digest(bytes)
}
const invoke = async (id, tool, args, cwd = root) => {
  const result = await runTool(tool, args, { cwd, env: environment, signal: abort.signal,
    timeoutMs: 600000, maxBytes: 8 * 1024 * 1024 })
  await save(`${id}.log`, result.output)
  assert.equal(result.state, 'passed', `Independent ${id} failed: ${result.reason ?? result.exitCode}.`)
  return result.output
}
try {
  await save('frozen/oracle.mjs', oracleBytes)
  await save('frozen/runner.mjs', await readFile(fileURLToPath(import.meta.url)))
  await save('frozen/contract.json', contractBytes)
  await save('fixture.json', fixtureBytes)
  await save('before.json', beforeBytes)
  await save('preview.log', previewBytes)
  const result = verifyUpgrade({ before, after: produced, payload, tooling,
    version: candidate.version, integrity: candidate.integrity })
  report.checks = result.checks
  report.changed = result.changed
  for (const [path, bytes] of Object.entries(produced)) {
    await mkdir(dirname(join(root, path)), { recursive: true })
    await writeFile(join(root, path), bytes, { flag: 'wx' })
  }
  await assertConsumerIsolation(root, repository)
  await mkdir(join(root, '.ngb/nuget'), { recursive: true })
  for (const [path, bytes] of Object.entries(candidate.files).filter(([path]) => path.endsWith('.nupkg'))) {
    await writeFile(join(root, '.ngb/nuget', basename(path)), bytes, { flag: 'wx', mode: 0o400 })
  }
  await mkdir(join(root, 'web/.ngb-packages'), { recursive: true })
  await writeFile(join(root, 'web/.ngb-packages/ui.tgz'), candidate.files[candidate.ui], { flag: 'wx', mode: 0o400 })
  report.dotnet = (await invoke('dotnet-version', 'dotnet', ['--version'])).trim()
  assert.equal(report.dotnet, '10.0.401')
  for (const [name, project] of [['application', 'CertificationApp.slnx'], ['helper', 'infrastructure/validation/Ngb.Validation.csproj']]) {
    await invoke(`${name}-restore`, 'dotnet', ['restore', project, '--locked-mode', '--disable-parallel',
      '--configfile', 'NuGet.Local.Config', '-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false'])
    await invoke(`${name}-build`, 'dotnet', ['build', project, '--no-restore', '-c', 'Release',
      '-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false'])
  }
  const web = join(root, 'web')
  await invoke('frontend-cache', 'npm', ['cache', 'add', join(web, '.ngb-packages/ui.tgz')], web)
  await invoke('frontend-install', 'npm', ['ci', '--ignore-scripts', '--workspaces=false'], web)
  await invoke('frontend-build', 'npm', ['run', 'build'], web)
  const status = JSON.parse(await invoke('kit-status', 'node', ['infrastructure/ngb.mjs', 'ai', 'status', '--format', 'json']))
  assert.equal(status.status, 'passed')
  assert.deepEqual(inventory(await applicationFiles(root)), inventory(produced), 'Clean verification changed application sources.')
  assert.deepEqual(inventory(await applicationFiles(application)), inventory(produced), 'The reviewed application changed during verification.')
  await verifyFrozenFixture(frozen, JSON.parse(sourceBytes))
  report.checks.push('trusted-preview-read-only', 'clean-locked-builds', 'installed-kit-integrity', 'source-fixture-unchanged')
  report.status = 'passed'
} catch (error) {
  report.failure = error.message
  process.exitCode = 2
} finally {
  process.removeListener('SIGINT', cancel)
  process.removeListener('SIGTERM', cancel)
  report.completedAt = new Date().toISOString()
  await writeFile(join(output, 'acceptance.json'), JSON.stringify(report, null, 2) + '\n', { flag: 'wx', mode: 0o600 })
}
console.log(`Independent customized upgrade: ${report.status}. Evidence: ${join(output, 'acceptance.json')}`)
