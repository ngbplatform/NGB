import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { mkdir, mkdtemp, readFile, readdir, writeFile } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { digest } from './release-contracts.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const inventoryPath = join(repository, 'quality/external-consumer/quality-inventory.json')
const inventoryBytes = await readFile(inventoryPath)
const inventory = JSON.parse(inventoryBytes)
const run = (command, args, cwd = repository) => execFileSync(command, args, { cwd, stdio: 'inherit' })
run('node', [
  '--test', '--experimental-test-coverage',
  ...inventory.measuredJavaScript.map(path => `--test-coverage-include=${path}`),
  '--test-coverage-lines=100', '--test-coverage-functions=100', '--test-coverage-branches=100',
  'quality/external-consumer/contracts.test.mjs', 'quality/external-consumer/layering.test.mjs', 'quality/external-consumer/release-contracts.test.mjs',
  'quality/external-consumer/registry-fetch.test.mjs', 'quality/external-consumer/sequencing.test.mjs',
  'quality/external-consumer/configuration.test.mjs', 'quality/upgrade-certification/scenarios.test.mjs',
  'ui/scripts/platform-ui-compatibility.test.mjs',
  'quality/external-consumer/application-contracts.test.mjs', 'quality/external-consumer/release-source.test.mjs',
  'quality/external-consumer/container-source.test.mjs',
])
run('node', ['--test',
  'quality/external-consumer/process-contracts.test.mjs', 'quality/external-consumer/application-process.test.mjs',
  'quality/external-consumer/checkpoints.test.mjs', 'quality/external-consumer/execution.test.mjs',
  'quality/external-consumer/environment-contracts.test.mjs',
])
await mkdir(join(repository, 'artifacts/coverage'), { recursive: true })
const reports = await mkdtemp(join(repository, 'artifacts/coverage/generated-helpers-'))
run('dotnet', [
  'test', 'quality/external-consumer/generated-helpers/tests/GeneratedHelpers.Tests.csproj',
  '-c', 'Release', '-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false',
  '--settings', 'quality/external-consumer/generated-helpers/coverage.runsettings',
  '--collect:XPlat Code Coverage', '--results-directory', reports,
])
const cobertura = []
for (const directory of await readdir(reports)) {
  if (!directory.endsWith('.xml')) {
    const path = join(reports, directory, 'coverage.cobertura.xml')
    cobertura.push(await readFile(path, 'utf8'))
  }
}
assert.equal(cobertura.length, 1)
assert.match(cobertura[0], /<coverage line-rate="1" branch-rate="1"/)
for (const path of inventory.measuredGeneratedDotNet) assert.ok(cobertura[0].includes(path.split('/').at(-1)), `Unmeasured generated helper: ${path}`)
for (const method of cobertura[0].matchAll(/<method\s[^>]*>/g)) assert.match(method[0], /line-rate="1"/)
run('npm', ['exec', '--', 'vitest', 'run', '--config', 'vitest.template.config.ts', '--coverage'], join(repository, 'ui'))
const frontend = JSON.parse(await readFile(join(repository, 'artifacts/coverage/template-frontend/coverage-summary.json'), 'utf8'))
for (const path of inventory.measuredGeneratedFrontend) {
  const entry = frontend[join(repository, path)]
  assert.ok(entry, `Unmeasured generated frontend: ${path}`)
  for (const metric of ['lines', 'branches', 'functions', 'statements']) assert.equal(entry[metric].pct, 100, `${path}: ${metric}`)
}
await mkdir(join(repository, 'artifacts/certification'), { recursive: true })
await writeFile(join(repository, 'artifacts/certification/tooling.json'), `${JSON.stringify({
  schemaVersion: 1, status: 'passed', inventorySha256: digest(inventoryBytes),
  measuredJavaScript: inventory.measuredJavaScript, generatedDotNet: inventory.measuredGeneratedDotNet,
  generatedFrontend: inventory.measuredGeneratedFrontend, threshold: 100,
  generatedDotNetReportSha256: digest(cobertura[0]), frontend: frontend.total,
  processContracts: 'passed',
}, null, 2)}\n`)
console.log('Tooling and generated executable helpers: all measured thresholds are 100%; process contracts passed.')
