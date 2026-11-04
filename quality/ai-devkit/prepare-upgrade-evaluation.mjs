import assert from 'node:assert/strict'
import { mkdir, readFile, realpath, writeFile } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { acquireCli } from '../../packaging/templates/content/infrastructure/ngb/acquisition.mjs'
import { applicationFiles, loadCandidate } from '../../packaging/templates/content/infrastructure/ngb/application.mjs'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { executionEnvironment, runTool } from '../../packaging/templates/content/infrastructure/ngb/process.mjs'
import { assertConsumerIsolation, assertOutsideRepository, verifyFrozenFixture } from '../external-consumer/contracts.mjs'
import { inventory, seedOverlay, verifyPreview } from './evaluation-oracles/customized-kit-upgrade.mjs'

assert.equal(process.argv.length, 4,
  'Usage: node prepare-upgrade-evaluation.mjs <sealed-candidate> <new-external-directory>')
assert.match(process.version, /^v24\./)
const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const candidatePath = await realpath(resolve(process.argv[2]))
const candidate = await loadCandidate(candidatePath)
assert.equal(candidate.version, '3.4.0')
const output = resolve(process.argv[3])
assertOutsideRepository(repository, output)
assertOutsideRepository(repository, await realpath(dirname(output)))
const sourceBytes = await readFile(join(repository, 'quality/upgrade-certification/source-3.3.0.json'))
const source = JSON.parse(sourceBytes)
const frozen = join(repository, 'quality/upgrade-certification/fixtures/3.3.0')
await verifyFrozenFixture(frozen, source)
const files = { ...await applicationFiles(frozen), ...seedOverlay }
const matrixBytes = await readFile(new URL('./agent-evaluations.json', import.meta.url))
const matrix = JSON.parse(matrixBytes)
const contractBytes = await readFile(new URL('./evaluation-fixtures/customized-kit-upgrade.contract.json', import.meta.url))
const contract = JSON.parse(contractBytes)
assert.equal(matrix.tasks.find(task => task.id === contract.task)?.prompt, contract.prompt)
await mkdir(output, { mode: 0o700 })
const root = await realpath(output)
const application = join(root, 'application')
for (const [path, bytes] of Object.entries(files)) {
  const target = join(application, path)
  await mkdir(dirname(target), { recursive: true })
  await writeFile(target, bytes, { flag: 'wx' })
}
await assertConsumerIsolation(application, repository)
// The candidate and target CLI are supplied outside the writable application.
// No SDK checkout, existing application cache or personal configuration is used.
for (const path of ['release-manifest.json', ...Object.keys(candidate.files)]) {
  const target = join(root, 'candidate', path)
  await mkdir(dirname(target), { recursive: true })
  await writeFile(target, await readFile(join(candidatePath, path)), { flag: 'wx', mode: 0o400 })
}
const acquired = await acquireCli(candidate.version, { candidate, applicationRoot: application,
  cacheRoot: join(root, 'target-cli') })
const before = inventory(await applicationFiles(application))
const preview = await runTool('node', [acquired.entry, 'upgrade', '--app', application, '--to', candidate.version,
  '--packages', join(root, 'candidate'), '--format', 'json'], {
  cwd: root, env: executionEnvironment(join(root, 'preview-home')), timeoutMs: 120000, maxBytes: 8 * 1024 * 1024,
})
await writeFile(join(root, 'preview.log'), preview.output, { flag: 'wx', mode: 0o600 })
assert.equal(preview.exitCode, 4, 'The declared collision must require an explicit resolution.')
const report = JSON.parse(preview.output)
verifyPreview(before, inventory(await applicationFiles(application)), report)
await verifyFrozenFixture(frozen, source)
const beforeBytes = JSON.stringify(before, null, 2) + '\n'
await writeFile(join(root, 'before.json'), beforeBytes, { flag: 'wx', mode: 0o600 })
await writeFile(join(root, 'task.json'), contractBytes, { flag: 'wx', mode: 0o600 })
const fixture = {
  schemaVersion: 1,
  task: contract.task,
  status: 'prepared-not-evaluated',
  candidateSha256: candidate.manifestSha256,
  sourceFixtureSha256: digest(sourceBytes),
  matrixSha256: digest(matrixBytes),
  contractSha256: digest(contractBytes),
  runnerSha256: digest(await readFile(fileURLToPath(import.meta.url))),
  oracleSha256: digest(await readFile(new URL('./evaluation-oracles/customized-kit-upgrade.mjs', import.meta.url))),
  beforeSha256: digest(beforeBytes),
  previewSha256: digest(preview.output),
  previewPlanSha256: report.plan.planSha256,
  cliIntegrity: acquired.integrity,
  cliEntry: acquired.entry,
  application,
  sourceFiles: Object.keys(before).length,
  checks: ['verified-frozen-source', 'isolated-target-cli', 'declared-ownership-collision', 'preview-read-only'],
}
await writeFile(join(root, 'fixture.json'), JSON.stringify(fixture, null, 2) + '\n', { flag: 'wx', mode: 0o600 })
console.log(JSON.stringify(fixture, null, 2))
