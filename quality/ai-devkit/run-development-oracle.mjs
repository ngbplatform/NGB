import assert from 'node:assert/strict'
import { mkdir, mkdtemp, readFile, realpath, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { basename, dirname, join, relative, resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'
import { applicationFiles, loadCandidate } from '../../packaging/templates/content/infrastructure/ngb/application.mjs'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { executionEnvironment, runTool } from '../../packaging/templates/content/infrastructure/ngb/process.mjs'
import { assertConsumerIsolation, assertOutsideRepository } from '../external-consumer/contracts.mjs'
import { passingTestCases } from './example-evidence.mjs'
import { requiresFrontendAcceptance, reviewFrontendTests, verifyAuthoredFrontend } from './development-evidence.mjs'

assert.ok([8, 9].includes(process.argv.length),
  'Usage: node run-development-oracle.mjs <task> <candidate> <application> <trusted-before-inventory.json> <new-output> <reviewed-tree-sha256> [reviewed-test-selection.json]')
const task = process.argv[2]
assert.ok(['existing-document-change', 'new-catalog', 'report-extension', 'permission-correction'].includes(task),
  'Unknown independent development oracle.')
const { harnessSha256, harnessSources, testClass, inspectChange, verifyChange, verifyHarness } =
  await import(`./evaluation-oracles/${task}.mjs`)
const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const candidate = await loadCandidate(resolve(process.argv[3]))
assert.equal(candidate.version, '3.4.0', 'This frozen harness targets SDK 3.4.0.')
assert.match(process.version, /^v24\./, 'Run independent acceptance with the supported Node 24 toolchain.')
const application = await realpath(resolve(process.argv[4]))
assertOutsideRepository(repository, application)
const before = JSON.parse(await readFile(resolve(process.argv[5]), 'utf8'))
const output = resolve(process.argv[6])
const relativeOutput = relative(application, output)
assert.ok(relativeOutput === '..' || relativeOutput.startsWith('..' + sep),
  'Independent evidence must stay outside the application.')
const inventory = async root => Object.fromEntries(Object.entries(await applicationFiles(root))
  .map(([path, bytes]) => [path, digest(bytes)]).sort(([left], [right]) => left.localeCompare(right, 'en')))
const producedFiles = await applicationFiles(application)
const produced = await inventory(application)
const inspected = inspectChange(before, produced)
assert.equal(inspected.producedTreeSha256, process.argv[7],
  'Review the produced source before executing it; the reviewed tree identity must match.')
const selectionBytes = process.argv[8] ? await readFile(resolve(process.argv[8])) : null
const selection = selectionBytes ? JSON.parse(selectionBytes) : null
if (selection) {
  if (requiresFrontendAcceptance(task)) reviewFrontendTests(inspected.changed, selection.reviewedFrontendFiles)
  assert.equal(selection.producedTreeSha256, inspected.producedTreeSha256, 'Test review belongs to another source tree.')
  const changedTests = inspected.changed.filter(path => /^EquipmentInspection\.(?:IntegrationTests|Tests)\/.+\.cs$/.test(path))
  assert.ok(changedTests.length > 0, 'The task requires application-authored tests.')
  assert.deepEqual(selection.reviewedTestFiles, changedTests, 'Review every changed application test source.')
  assert.ok(Array.isArray(selection.integrationClasses) && selection.integrationClasses.length <= 32)
  assert.equal(new Set(selection.integrationClasses).size, selection.integrationClasses.length)
  for (const name of selection.integrationClasses) {
    assert.match(name, /^EquipmentInspection\.IntegrationTests\.[A-Za-z_][A-Za-z0-9_]*$/,
      'Use reviewed fully qualified integration test class names.')
  }
  assert.ok(!changedTests.some(path => path.startsWith('EquipmentInspection.IntegrationTests/'))
    || selection.integrationClasses.length > 0, 'Changed integration tests require an executable review selection.')
}
const harnessRoot = join(repository, 'quality/ai-devkit/evaluation-fixtures')
const harnessFiles = Object.fromEntries(await Promise.all(Object.keys(harnessSha256)
  .map(async path => [path, await readFile(join(harnessRoot, harnessSources[path]))])))
verifyHarness(harnessFiles)
const oraclePath = join(repository, `quality/ai-devkit/evaluation-oracles/${task}.mjs`)
const oracleBytes = await readFile(oraclePath)
const runnerBytes = await readFile(fileURLToPath(import.meta.url))
await mkdir(output, { mode: 0o700 })
const stage = await realpath(await mkdtemp(join(tmpdir(), 'ngb-development-oracle-')))
const harness = join(stage, 'harness')
const testedApplication = join(stage, 'application')
const feed = join(stage, 'feed')
await mkdir(harness)
await mkdir(feed)
const abort = new AbortController()
const cancel = () => abort.abort()
process.once('SIGINT', cancel)
process.once('SIGTERM', cancel)
const environment = executionEnvironment(join(stage, 'home'))
const report = {
  schemaVersion: 1,
  task,
  status: 'not-ready',
  candidateSha256: candidate.manifestSha256,
  oracleSha256: digest(oracleBytes),
  runnerSha256: digest(runnerBytes),
  producedTreeSha256: inspected.producedTreeSha256,
  node: process.version,
  stage,
  startedAt: new Date().toISOString(),
  artifacts: {},
  authoredTests: { status: selection ? 'not-run' : 'not-reviewed',
    reviewSha256: selectionBytes ? digest(selectionBytes) : null },
}
const save = async (path, bytes) => {
  await mkdir(dirname(join(output, path)), { recursive: true })
  await writeFile(join(output, path), bytes, { mode: 0o600, flag: 'wx' })
  report.artifacts[path] = digest(bytes)
}
const invoke = async (name, args, tool = 'dotnet', cwd = harness, env = environment) => {
  const result = await runTool(tool, args, { cwd, env, signal: abort.signal,
    timeoutMs: 600000, maxBytes: 8 * 1024 * 1024 })
  await save(`${name}.log`, result.output)
  assert.equal(result.state, 'passed', `Independent ${name} failed: ${result.reason ?? result.exitCode}.`)
  return result.output
}
try {
  for (const [path, bytes] of Object.entries(producedFiles)) {
    const target = join(testedApplication, path)
    await mkdir(dirname(target), { recursive: true })
    await writeFile(target, bytes, { flag: 'wx' })
  }
  assert.deepEqual(await inventory(testedApplication), produced, 'The isolated source copy differs from the reviewed tree.')
  await assertConsumerIsolation(testedApplication, repository)
  for (const [path, bytes] of Object.entries(harnessFiles)) {
    await writeFile(join(harness, path), bytes, { flag: 'wx' })
    await save(`frozen/harness/${path}`, bytes)
  }
  await save('frozen/oracle.mjs', oracleBytes)
  await save('frozen/runner.mjs', runnerBytes)
  await save('frozen/development-evidence.mjs', await readFile(new URL('./development-evidence.mjs', import.meta.url)))
  if (selectionBytes) await save('frozen/test-selection.json', selectionBytes)
  const contractName = task === 'existing-document-change' ? 'document-change' : task
  await save('frozen/contract.json', await readFile(join(harnessRoot, `${contractName}.contract.json`)))
  for (const [path, bytes] of Object.entries(candidate.files).filter(([path]) => path.endsWith('.nupkg'))) {
    await writeFile(join(feed, basename(path)), bytes, { mode: 0o400, flag: 'wx' })
  }
  const nuget = join(stage, 'NuGet.Config')
  await writeFile(nuget, `<?xml version="1.0" encoding="utf-8"?>
<configuration>
    <packageSources><clear />
        <add key="candidate" value="feed" />
        <add key="registry" value="https://api.nuget.org/v3/index.json" />
    </packageSources>
    <packageSourceMapping><clear />
        <packageSource key="candidate"><package pattern="NGB.Platform.*" /></packageSource>
        <packageSource key="registry"><package pattern="*" /></packageSource>
    </packageSourceMapping>
    <fallbackPackageFolders><clear /></fallbackPackageFolders>
</configuration>
`, { flag: 'wx' })
  await save('fixture.json', JSON.stringify({ before, produced, harnessSha256, version: candidate.version,
    candidateSha256: candidate.manifestSha256 }, null, 2) + '\n')
  const properties = [`-p:AgentApplicationRoot=${testedApplication}`, '-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false']
  report.dotnet = (await invoke('dotnet-version', ['--version'])).trim()
  assert.equal(report.dotnet, '10.0.401', 'The independent runner requires the pinned .NET SDK.')
  const solution = join(testedApplication, 'EquipmentInspection.slnx')
  await invoke('application-restore', ['restore', solution, '--configfile', nuget, ...properties])
  await invoke('application-build', ['build', solution, '--no-restore', '-c', 'Release', ...properties])
  await invoke('restore', ['restore', 'AgentAcceptance.csproj', '--configfile', nuget, ...properties])
  await invoke('build', ['build', 'AgentAcceptance.csproj', '--no-restore', '-c', 'Release', ...properties])
  try {
    await invoke('tests', ['test', 'AgentAcceptance.csproj', '--no-build', '--no-restore', '-c', 'Release',
      '--filter', `FullyQualifiedName~${testClass}`, '--logger', 'trx;LogFileName=acceptance.trx',
      '--results-directory', join(stage, 'results'), ...properties])
  } finally {
    const trx = await readFile(join(stage, 'results/acceptance.trx')).catch(error => {
      if (error.code !== 'ENOENT') throw error
      return null
    })
    if (trx) await save('acceptance.trx', trx)
  }
  let browserReport
  if (requiresFrontendAcceptance(task)) {
    const web = join(testedApplication, 'web')
    const archive = join(stage, 'ui.tgz')
    await writeFile(archive, candidate.files[candidate.ui], { mode: 0o400, flag: 'wx' })
    await invoke('frontend-cache', ['cache', 'add', archive, '--ignore-scripts'], 'npm', web)
    await invoke('frontend-install', ['ci', '--ignore-scripts', '--no-audit', '--no-fund'], 'npm', web)
    const installed = JSON.parse(await readFile(join(web, 'node_modules/@ngbplatform/ui/package.json'), 'utf8'))
    assert.equal(installed.version, candidate.version, 'The application resolved a different UI version.')
    await invoke('frontend-build', ['run', 'build'], 'npm', web)
    await invoke('browser-install', ['exec', '--no', '--', 'playwright', 'install', 'chromium'], 'npm', web)
  }
  if (task === 'permission-correction') {
    const web = join(testedApplication, 'web')
    try {
      await invoke('browser-tests', [join(web, 'node_modules/playwright/cli.js'), 'test',
        '--config', join(harness, 'playwright.config.mjs')], 'node', harness,
        { ...environment, NGB_EVAL_APPLICATION: testedApplication })
    } finally {
      const bytes = await readFile(join(harness, 'browser-results/report.json')).catch(error => {
        if (error.code !== 'ENOENT') throw error
        return null
      })
      if (bytes) {
        await save('browser.json', bytes)
        browserReport = JSON.parse(bytes)
      }
    }
  }
  assert.deepEqual(await inventory(application), produced, 'The produced application changed during acceptance.')
  assert.deepEqual(await inventory(testedApplication), produced, 'Acceptance execution changed the tested application source.')
  const result = verifyChange({ before, after: produced, harnessFiles, browserReport,
    trx: await readFile(join(output, 'acceptance.trx'), 'utf8') })
  report.checks = result.checks
  if (selection) {
    const runAuthored = async (name, project, filter) => {
      const results = join(stage, `authored-${name}`)
      try {
        await invoke(`authored-${name}`, ['test', join(testedApplication, project), '--no-build', '--no-restore',
          '-c', 'Release', '--logger', 'trx;LogFileName=tests.trx', '--results-directory', results,
          ...(filter ? ['--filter', filter] : []), ...properties])
      } finally {
        const bytes = await readFile(join(results, 'tests.trx')).catch(error => {
          if (error.code !== 'ENOENT') throw error
          return null
        })
        if (bytes) await save(`authored-${name}.trx`, bytes)
      }
      return passingTestCases(await readFile(join(output, `authored-${name}.trx`), 'utf8'))
    }
    try {
      report.authoredTests.unit = await runAuthored('unit', 'EquipmentInspection.Tests/EquipmentInspection.Tests.csproj')
      if (selection.integrationClasses.length) {
        report.authoredTests.integration = await runAuthored('integration',
          'EquipmentInspection.IntegrationTests/EquipmentInspection.IntegrationTests.csproj',
          selection.integrationClasses.map(name => `FullyQualifiedName~${name}.`).join('|'))
        for (const name of selection.integrationClasses) {
          assert.ok(report.authoredTests.integration.some(test => test.startsWith(name + '.')),
            `No passing tests were discovered for reviewed class: ${name}`)
        }
      }
      if (requiresFrontendAcceptance(task)) {
        const web = join(testedApplication, 'web')
        const raw = {}
        const collect = async (key, path) => {
          const bytes = await readFile(path).catch(error => {
            if (error.code !== 'ENOENT') throw error
            return null
          })
          if (bytes) {
            await save(`authored-frontend-${key}.json`, bytes)
            raw[key] = JSON.parse(bytes)
          }
        }
        try {
          await invoke('authored-frontend-unit', ['run', 'test', '--', '--run', '--coverage',
            '--reporter=json', '--outputFile=' + join(stage, 'authored-frontend-unit.json')], 'npm', web)
        } finally {
          await collect('unit', join(stage, 'authored-frontend-unit.json'))
          await collect('coverage', join(testedApplication, '.ngb/coverage/frontend/coverage-summary.json'))
        }
        try {
          await invoke('authored-frontend-browser', ['run', 'test:e2e'], 'npm', web)
        } finally {
          await collect('browser', join(testedApplication, '.ngb/browser-results/report.json'))
        }
        report.authoredTests.frontend = verifyAuthoredFrontend({ files: produced, webRoot: web, ...raw })
      }
      report.authoredTests.status = 'passed'
    } catch (error) {
      report.authoredTests.status = 'failed'
      throw error
    }
  }
  assert.deepEqual(await inventory(application), produced, 'The produced application changed during authored-test acceptance.')
  assert.deepEqual(await inventory(testedApplication), produced, 'Authored tests changed application source.')
  report.status = 'passed'
} catch (error) {
  report.failure = error.message
  process.exitCode = 2
} finally {
  process.removeListener('SIGINT', cancel)
  process.removeListener('SIGTERM', cancel)
  report.completedAt = new Date().toISOString()
  await writeFile(join(output, 'acceptance.json'), JSON.stringify(report, null, 2) + '\n', { mode: 0o600, flag: 'wx' })
}
console.log(`Independent ${task}: ${report.status.toUpperCase()}. ${report.failure ?? ''}`)
