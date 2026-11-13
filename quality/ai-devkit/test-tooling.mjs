import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { mkdir, readFile, readdir, writeFile } from 'node:fs/promises'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { assetPaths } from '../../packaging/templates/content/infrastructure/ngb/kit-files.mjs'
import { applicationFiles } from '../../packaging/templates/content/infrastructure/ngb/application.mjs'
import { prepareTemplate } from '../../ai-devkit/prepare.mjs'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const modules = 'packaging/templates/content/infrastructure/ngb'
const measured = ['contracts', 'kit-contracts', 'kit-plan', 'upgrade-plan', 'project-data', 'resolved-data', 'evidence', 'archive', 'performance', 'resource-data']
  .map(name => `${modules}/${name}.mjs`)
const authoringModules = ['ai-devkit/guidance.mjs', 'ai-devkit/capabilities.mjs',
  'quality/ai-devkit/certification-data.mjs', 'quality/ai-devkit/evaluation-data.mjs',
  'quality/ai-devkit/contract-references.mjs',
  'quality/ai-devkit/example-execution.mjs',
  'quality/ai-devkit/review-contracts.mjs',
  'quality/ai-devkit/agent-availability.mjs',
  'quality/ai-devkit/example-evidence.mjs', 'quality/ai-devkit/development-evidence.mjs', 'quality/ai-devkit/agent-output.mjs',
  'quality/ai-devkit/upgrade-launcher.mjs',
  'quality/ai-devkit/evaluation-oracles/skill-discovery.mjs',
  'quality/ai-devkit/evaluation-oracles/existing-document-change.mjs',
  'quality/ai-devkit/evaluation-oracles/new-catalog.mjs',
  'quality/ai-devkit/evaluation-oracles/report-extension.mjs',
  'quality/ai-devkit/evaluation-oracles/permission-correction.mjs',
  'quality/ai-devkit/evaluation-oracles/customized-kit-upgrade.mjs']
const processModules = ['application', 'cli', 'file-data', 'kit-files', 'kit', 'process', 'profiles', 'validation',
  'environment', 'acquisition', 'delegation', 'transaction', 'upgrade-assets', 'coverage', 'load', 'performance-runner', 'resources']
  .map(name => `${modules}/${name}.mjs`)
const discovered = (await readdir(join(root, modules))).filter(name => name.endsWith('.mjs')).map(name => `${modules}/${name}`)
assert.deepEqual(discovered.sort(), [...measured, ...processModules].sort(),
  'Every shared CLI module must have an explicit verification classification.')
assert.match(process.version, /^v24\./, 'Run Kit quality with the supported Node 24 toolchain.')
await prepareTemplate()

async function inputSnapshot() {
  const paths = [...await assetPaths(root, 'quality/ai-devkit'), ...await assetPaths(root, 'ai-devkit')]
  const rows = await Promise.all(paths.map(async path => [path, digest(await readFile(join(root, path)))]))
  for (const [path, bytes] of Object.entries(await applicationFiles(join(root, 'packaging/templates/content')))) {
    rows.push([`packaging/templates/content/${path}`, digest(bytes)])
  }
  for (const path of ['quality/external-consumer/application-contracts.test.mjs', 'quality/external-consumer/application-process.test.mjs',
    'quality/coverage/verify-backend-full-coverage.mjs', 'quality/coverage/verify-backend-full-coverage.test.mjs']) {
    rows.push([path, digest(await readFile(join(root, path)))])
  }
  return Object.fromEntries(rows.sort(([left], [right]) => left.localeCompare(right, 'en')))
}

const tests = (await readdir(join(root, 'quality/ai-devkit'))).filter(name => name.endsWith('.test.mjs'))
  .map(name => `quality/ai-devkit/${name}`)
tests.push('quality/external-consumer/application-contracts.test.mjs', 'quality/external-consumer/application-process.test.mjs')
tests.push('quality/coverage/verify-backend-full-coverage.test.mjs')
const started = new Date().toISOString()
const inputs = await inputSnapshot()
const output = execFileSync(process.execPath, ['--test', '--experimental-test-coverage',
  '--test-reporter=./quality/ai-devkit/reporter.mjs', ...[...measured, ...authoringModules].map(path => `--test-coverage-include=${path}`),
  '--test-coverage-lines=100', '--test-coverage-branches=100', '--test-coverage-functions=100', ...tests],
{ cwd: root, encoding: 'utf8', timeout: 120000, maxBuffer: 8 * 1024 * 1024 })
const events = output.trim().split('\n').map(line => JSON.parse(line))
const summaries = events.filter(event => event.type === 'test:summary')
const summary = summaries.find(event => !event.data.file)?.data
assert.ok(summary?.success && summary.counts.tests > 0, 'Node test discovery is missing or unsuccessful.')
for (const field of ['failed', 'cancelled', 'skipped', 'todo']) assert.equal(summary.counts[field], 0, `Required Node tests were ${field}.`)
assert.deepEqual(summaries.filter(event => event.data.file).map(event => relative(root, event.data.file).replaceAll('\\', '/')).sort(),
  [...tests].sort(), 'Every declared test file must execute.')
const coverage = events.filter(event => event.type === 'test:coverage')
assert.equal(coverage.length, 1, 'Expected one complete Node coverage report.')
const files = coverage[0].files.map(file => ({ ...file, path: relative(root, file.path).replaceAll('\\', '/') }))
assert.deepEqual(files.map(file => file.path).sort(), [...measured, ...authoringModules].sort(), 'Missing or unexpected measured source file.')
for (const file of files) {
  assert.ok(file.totalLineCount > 0 && file.totalFunctionCount > 0, `Empty measured source: ${file.path}`)
  for (const metric of ['Line', 'Branch', 'Function']) {
    assert.equal(file[`covered${metric}Count`], file[`total${metric}Count`], `Incomplete ${metric} coverage: ${file.path}`)
  }
}
assert.deepEqual(await inputSnapshot(), inputs, 'Sources changed during the test run; rerun against one consistent input snapshot.')
assert.deepEqual((await readdir(join(root, modules))).filter(name => name.endsWith('.mjs')).map(name => `${modules}/${name}`).sort(),
  [...measured, ...processModules].sort(), 'CLI module inventory changed during the test run.')
const evidence = { schemaVersion: 1, status: 'passed', scope: 'tooling-contracts', startedAt: started,
  completedAt: new Date().toISOString(), node: process.version, tests: summary.counts,
  sourceSha256: digest(JSON.stringify(inputs)), inputs, measured: files,
  processModules, processCoveragePolicy: 'Filesystem, subprocess and CLI behavior use executable process contracts; no numeric process coverage is inferred.' }
await mkdir(join(root, 'artifacts/ai-devkit'), { recursive: true })
await writeFile(join(root, 'artifacts/ai-devkit/tooling.json'), `${JSON.stringify(evidence, null, 2)}\n`)
console.log(`Kit tooling: ${summary.counts.tests} tests passed; ${files.length} measured modules have complete per-file coverage.`)
