import assert from 'node:assert/strict'
import test from 'node:test'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { inspectCapabilities, preparePayload } from '../../ai-devkit/prepare.mjs'
import { bindExampleAcceptance, exampleFrontendSources, exampleSources, passingBrowserCases, passingTestCases } from './example-evidence.mjs'

const payload = await preparePayload()
const registry = JSON.parse(payload.files.find(file => file.path === 'ai/vendor/capability-registry.json').content)
const checks = registry.capabilities.flatMap(capability => capability.acceptance).filter(check => check.source)
const backendChecks = checks.filter(check => check.kind !== 'browser')
const names = [...new Set(backendChecks.flatMap(check => check.cases))].sort()
const attribute = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;')

function trx(cases = names) {
  return `<TestRun><Results>${cases.map((name, index) =>
    `<UnitTestResult executionId="execution-${index}" testId="${index}" testName="${attribute(name)}" outcome="Passed" />`).join('')}</Results>
<ResultSummary><Counters total="${cases.length}" executed="${cases.length}" passed="${cases.length}" failed="0" /></ResultSummary></TestRun>`
}

function fixture() {
  const xml = trx()
  const coverage = '<coverage />'
  const artifacts = new Map([['suite/tests.trx', xml], ['suite/coverage.xml', coverage]])
  const suite = {
    project: 'EquipmentInspection.IntegrationTests/EquipmentInspection.IntegrationTests.csproj',
    sources: [...new Set(backendChecks.map(check => check.source.slice('examples/'.length)))],
    counts: { total: names.length, executed: names.length, passed: names.length, failed: 0, skipped: 0 },
    cases: names,
    trx: { path: 'suite/tests.trx', sha256: digest(xml) },
    coverage: { path: 'suite/coverage.xml', sha256: digest(coverage) },
  }
  const example = { name: 'equipment-inspection', status: 'passed', suites: [suite],
    coverage: { passed: true, sourceFiles: { required: 9, missingFromReport: 0 }, failingFileCount: 0 } }
  const stage = '/isolated'
  const unit = { success: true, numTotalTests: 1, numPassedTests: 1, numFailedTests: 0, numPendingTests: 0, numTodoTests: 0,
    testResults: [{ status: 'passed', assertionResults: [{ status: 'passed' }] }] }
  const browserChecks = checks.filter(check => check.kind === 'browser')
  const specs = [...new Map(browserChecks.flatMap(check => check.cases.map(name => {
    const [projectName, title] = JSON.parse(name)
    const file = check.source.split('/web/tests/e2e/')[1]
    return [file + name, { title, file, ok: true, tests: [{ projectName, expectedStatus: 'passed', status: 'expected',
      results: [{ status: 'passed' }] }] }]
  }))).values()]
  const browser = { config: { rootDir: `${stage}/${example.name}/web/tests/e2e` }, errors: [],
    suites: [{ title: 'inspection.spec.ts', specs }],
    stats: { expected: specs.length, unexpected: 0, flaky: 0, skipped: 0 } }
  const sources = exampleFrontendSources(exampleSources(payload), example.name)
  const metric = { total: 1, covered: 1, skipped: 0, pct: 100 }
  const frontendCoverage = Object.fromEntries(sources.map(path => [`${stage}/${example.name}/web/${path}`,
    { lines: metric, branches: metric, functions: metric, statements: metric }]))
  const counts = { total: 1, passed: 1, failed: 0, skipped: 0 }
  example.frontend = { status: 'passed', reports: {}, unit: counts,
    browser: { ...counts, total: specs.length, passed: specs.length },
    coverage: { files: sources.length, lines: 100, branches: 100, functions: 100, statements: 100 } }
  for (const [key, value] of Object.entries({ unit, coverage: frontendCoverage, browser })) {
    const bytes = JSON.stringify(value)
    const path = `frontend/${key}.json`
    artifacts.set(path, bytes)
    example.frontend.reports[key] = { path, sha256: digest(bytes) }
  }
  const candidateSha256 = 'a'.repeat(64)
  const runnerSha256 = 'b'.repeat(64)
  const report = { schemaVersion: 2, status: 'passed', version: payload.version, stage,
    candidateSha256, runnerSha256, capabilityInputsSha256: inspectCapabilities(payload).inputsSha256,
    sources: exampleSources(payload), examples: [example] }
  return { payload, report, candidateSha256, runnerSha256, artifacts, example, suite }
}

test('example acceptance binds every declared case and refuses missing capability evidence', () => {
  const value = fixture()
  const evidence = bindExampleAcceptance(value)
  assert.deepEqual(evidence.results.map(result => result.id), checks.map(check => check.id))
  for (const result of evidence.results) {
    const check = checks.find(check => check.id === result.id)
    const path = check.kind === 'browser' ? 'frontend/browser.json' : 'suite/tests.trx'
    assert.equal(result.reportSha256, digest(value.artifacts.get(path)))
  }
  const result = inspectCapabilities(payload, evidence)
  assert.equal(result.passedCapabilities,
    registry.capabilities.filter(capability => capability.acceptance.every(check => check.source)).length)
  assert.equal(result.status, 'passed')
  const incomplete = inspectCapabilities(payload, { ...evidence, results: evidence.results.slice(1) })
  assert.equal(incomplete.status, 'not-ready')
  assert.ok(incomplete.passedCapabilities < result.passedCapabilities)
})

test('TRX acceptance rejects contradictory counters, failures and ambiguous case identities', () => {
  assert.deepEqual(passingTestCases(trx()), names)
  assert.deepEqual(passingTestCases(trx().replace('testId="1"', 'testId="0"')), names)
  for (const xml of [
    trx().replace('<Results>', '<Results></Results><Results>'),
    trx().replace(`total="${names.length}"`, `total="${names.length + 1}"`),
    trx().replace('executionId="execution-1"', 'executionId="execution-0"'),
    trx().replace('executionId="execution-0"', 'executionId=""'),
    trx().replace('testId="0"', 'testId=""'),
    trx().replace(attribute(names[1]), attribute(names[0])),
    trx().replace('testName="' + attribute(names[0]) + '"', 'testName=""'),
    trx().replace('outcome="Passed"', 'outcome="Failed"'),
    trx().replace('UnitTestResult', 'OtherResult'),
    trx().replace(/<UnitTestResult[^>]+\/>/, ''),
    trx([]),
  ]) {
    assert.throws(() => passingTestCases(xml))
  }
})

test('TRX parameter names preserve escaped whitespace and reject equivalent duplicate names', () => {
  const xml = trx(['whitespace value: TAB', 'empty value']).replace('value: TAB', 'value: &#x9;')
  assert.deepEqual(passingTestCases(xml), ['empty value', 'whitespace value: \t'])
  assert.throws(() => passingTestCases(xml.replace('empty value', 'whitespace value: &#9;')), /duplicate/)
})

test('example binding rejects stale packages, inputs, raw reports and unexecuted acceptance', () => {
  const mutations = [
    value => { value.report.schemaVersion = 1 },
    value => { value.report.status = 'not-ready' },
    value => { value.report.version = '3.3.0' },
    value => { value.report.candidateSha256 = 'c'.repeat(64) },
    value => { value.report.runnerSha256 = 'c'.repeat(64) },
    value => { value.report.capabilityInputsSha256 = 'c'.repeat(64) },
    value => { delete value.report.sources[Object.keys(value.report.sources)[0]] },
    value => { value.example.name = '../outside' },
    value => { value.report.examples.push(value.example) },
    value => { value.example.status = 'failed' },
    value => { value.example.coverage.passed = false },
    value => { value.example.coverage.sourceFiles.required = 0 },
    value => { value.example.coverage.sourceFiles.missingFromReport = 1 },
    value => { value.example.coverage.failingFileCount = 1 },
    value => { delete value.example.frontend },
    value => { value.example.frontend.status = 'failed' },
    value => { value.example.frontend.reports.unit.path = '../outside.json' },
    value => { value.artifacts.delete('frontend/browser.json') },
    value => { value.artifacts.set('frontend/coverage.json', '{}') },
    value => { value.example.frontend.unit = {} },
    value => { value.example.frontend.browser = {} },
    value => { value.example.frontend.coverage.files = 0 },
    value => { value.report.stage = '/another-stage' },
    value => { value.example.suites.push(value.suite) },
    value => { value.suite.project = '../outside.csproj' },
    value => { value.suite.trx.path = '../outside.trx' },
    value => { value.artifacts.set('suite/tests.trx', 'changed') },
    value => { value.artifacts.delete('suite/coverage.xml') },
    value => { value.suite.counts.total = 100 },
    value => { value.suite.cases = ['invented'] },
    value => { value.suite.sources.push('unknown.cs') },
    value => { value.suite.sources.push(value.suite.sources[0]) },
    value => { value.suite.sources = [] },
    value => { value.example.suites = [] },
    value => { value.report.examples = [] },
    value => { value.report.examples.push({ ...value.example, name: 'unrelated', suites: [] }) },
    value => {
      const xml = trx([names[0], 'Unrelated.Test'])
      value.artifacts.set('suite/tests.trx', xml)
      value.suite.trx.sha256 = digest(xml)
      value.suite.cases = passingTestCases(xml)
      value.suite.counts = { total: 2, executed: 2, passed: 2, failed: 0, skipped: 0 }
    },
  ]
  for (const mutate of mutations) {
    const value = fixture()
    mutate(value)
    assert.throws(() => bindExampleAcceptance(value))
  }
})

test('frontend inventory excludes declarations and unrelated sources and permits backend-only examples', () => {
  assert.deepEqual(exampleFrontendSources({
    'sample/web/src/main.ts': '', 'sample/web/src/view.vue': '', 'sample/web/src/env.d.ts': '',
    'sample/web/src/logo.svg': '', 'sample/web/tests/main.ts': '', 'other/web/src/main.ts': '',
  }, 'sample'), ['src/main.ts', 'src/view.vue'])
  const value = fixture()
  value.payload = { ...payload, files: payload.files.filter(file => !file.path.includes('/examples/equipment-inspection/web/')) }
  const backendRegistry = structuredClone(registry)
  for (const capability of backendRegistry.capabilities) {
    for (const check of capability.acceptance.filter(item => item.kind === 'browser')) {
      delete check.source
      delete check.test
      delete check.cases
    }
  }
  value.payload.files = value.payload.files.map(file => file.path === 'ai/vendor/capability-registry.json'
    ? { ...file, content: JSON.stringify(backendRegistry) } : file)
  value.report.sources = exampleSources(value.payload)
  value.report.capabilityInputsSha256 = inspectCapabilities(value.payload).inputsSha256
  delete value.example.frontend
  assert.ok(bindExampleAcceptance(value).results.length > 0)
})

test('browser cases bind exact source, project and describe titles on portable paths', () => {
  const value = fixture()
  const report = JSON.parse(value.artifacts.get('frontend/browser.json'))
  const spec = report.suites[0].specs[0]
  report.suites = [{ title: 'inspection.spec.ts', specs: [], suites: [{ title: 'Month close', specs: [spec] }] }]
  report.stats.expected = 1
  spec.tests[0].projectName = 'chromium'
  assert.deepEqual([...passingBrowserCases(report, '/isolated/equipment-inspection/web')], [
    ['tests/e2e/inspection.spec.ts', [JSON.stringify(['chromium', 'Month close', spec.title])]],
  ])
  report.config.rootDir = 'C:\\isolated\\equipment-inspection\\web\\tests\\e2e'
  assert.equal(passingBrowserCases(report, 'C:\\isolated\\equipment-inspection\\web\\').size, 1)
})

test('browser identities reject missing paths, escaped roots, duplicate executions and unexecuted titles', () => {
  const mutations = [
    report => { delete report.config },
    report => { report.config.rootDir = '/outside' },
    report => { report.config.rootDir += '/../outside' },
    report => { delete report.suites[0].title },
    report => { report.suites[0].specs[0].title = '' },
    report => { report.suites[0].specs[0].file = '../outside.ts' },
    report => { report.suites[0].specs[0].file = '/outside.ts' },
    report => { report.suites[0].specs[0].file = 'unpackaged.ts' },
    report => { delete report.suites[0].specs[0].tests[0].projectName },
    report => { report.suites[0].specs[0].tests[0].projectName = 'unexecuted-project' },
    report => { report.suites[0].specs[0].title = 'Unexecuted title' },
    report => { report.suites[0].specs[1] = report.suites[0].specs[0] },
    report => { report.suites[0].specs[0].tests[0].results.push({ status: 'passed' }) },
  ]
  for (const mutate of mutations) {
    const value = fixture()
    const report = JSON.parse(value.artifacts.get('frontend/browser.json'))
    mutate(report)
    const bytes = JSON.stringify(report)
    value.artifacts.set('frontend/browser.json', bytes)
    value.example.frontend.reports.browser.sha256 = digest(bytes)
    assert.throws(() => bindExampleAcceptance(value))
  }
  const value = fixture()
  value.suite.sources.push('equipment-inspection/web/src/main.ts')
  assert.throws(() => bindExampleAcceptance(value), /C# source/)
})
