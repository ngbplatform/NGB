import assert from 'node:assert/strict'
import test from 'node:test'
import { browserTestCounts, frontendCoverage, frontendTestCounts, uniqueCoverageReport } from '../../packaging/templates/content/infrastructure/ngb/evidence.mjs'
import { testCounts } from '../../packaging/templates/content/infrastructure/ngb/profiles.mjs'

const frontend = () => ({
  success: true, numTotalTests: 1, numPassedTests: 1, numFailedTests: 0, numPendingTests: 0, numTodoTests: 0,
  testResults: [{ status: 'passed', assertionResults: [{ status: 'passed' }] }],
})

const browser = () => ({
  errors: [], stats: { expected: 1, unexpected: 0, flaky: 0, skipped: 0 },
  suites: [{ specs: [], suites: [{ specs: [{ ok: true,
    tests: [{ expectedStatus: 'passed', status: 'expected', results: [{ status: 'passed' }] }],
  }] }] }],
})

test('VSTest attachment copies are deduplicated by content and differing reports fail', () => {
  const original = { path: 'run/a/coverage.cobertura.xml', sha256: 'a'.repeat(64) }
  const copy = { ...original, path: 'run/b/coverage.cobertura.xml' }
  assert.deepEqual(uniqueCoverageReport([original]), original)
  assert.deepEqual(uniqueCoverageReport([copy, original]), original)
  for (const entries of [null, [], Array(257).fill(original), [original, original],
    [{ ...original, sha256: 'bad' }], [original, { ...copy, sha256: 'b'.repeat(64) }]]) {
    assert.throws(() => uniqueCoverageReport(entries))
  }
})

test('frontend discovery rejects empty, skipped, inconsistent and unsuccessful reports', () => {
  assert.equal(frontendTestCounts(frontend()).total, 1)
  for (const change of [
    value => { value.success = false }, value => { value.numTotalTests = 0 },
    value => { value.numPendingTests = 1 }, value => { value.numTodoTests = 1 },
    value => { value.numFailedTests = 1 }, value => { value.numTotalTests = 2 },
    value => { value.numPassedTests = '1' }, value => { value.testResults = [] },
    value => { value.testResults[0].status = 'failed' },
    value => { value.testResults[0].assertionResults = [] },
    value => { value.testResults[0].assertionResults.push({ status: 'passed' }) },
    value => { value.testResults[0].assertionResults[0].status = 'pending' },
  ]) {
    const value = frontend()
    change(value)
    assert.throws(() => frontendTestCounts(value))
  }
  assert.throws(() => frontendTestCounts(null))
})

test('frontend coverage requires every source file and every metric independently', () => {
  const metrics = Object.fromEntries(['lines', 'branches', 'functions', 'statements']
    .map(metric => [metric, { total: 1, covered: 1, skipped: 0, pct: 100 }]))
  const report = { total: metrics, '/app/web/src/main.ts': metrics }
  assert.equal(frontendCoverage(report, ['src/main.ts'], '/app/web/').files, 1)
  assert.equal(frontendCoverage({ 'C:\\app\\web\\src\\main.ts': metrics }, ['src/main.ts'], 'C:\\app\\web').files, 1)
  assert.throws(() => frontendCoverage(null, [], '/app'))
  assert.throws(() => frontendCoverage(report, [], '/app/web'))
  assert.throws(() => frontendCoverage(report, ['src/main.ts', 'src/missing.vue'], '/app/web'))
  assert.throws(() => frontendCoverage({ ...report, '/outside.ts': metrics }, ['src/main.ts'], '/app/web'))
  assert.throws(() => frontendCoverage({ '/app/web/../outside.ts': metrics }, ['src/main.ts'], '/app/web'))
  assert.throws(() => frontendCoverage({ ...report, '\\app\\web\\src\\main.ts': metrics }, ['src/main.ts'], '/app/web'))
  for (const metric of ['lines', 'branches', 'functions', 'statements']) {
    for (const field of ['covered', 'skipped', 'pct', 'total']) {
      const incomplete = structuredClone(report)
      incomplete['/app/web/src/main.ts'][metric][field] = field === 'skipped' ? 1 : 0
      assert.throws(() => frontendCoverage(incomplete, ['src/main.ts'], '/app/web'), /incomplete/)
    }
  }
})

test('browser reports fail on skipped, flaky, expected-failure and missing workflows', () => {
  assert.equal(browserTestCounts(browser()).total, 1)
  for (const change of [
    value => { value.errors.push({ message: 'teardown failed' }) },
    value => { value.suites = [] }, value => { value.suites[0].specs = null },
    value => { value.stats.expected = 2 }, value => { value.stats.flaky = 1 },
    value => { value.suites[0].suites[0].specs[0].ok = false },
    value => { value.suites[0].suites[0].specs[0].tests = [] },
    value => { value.suites[0].suites[0].specs[0].tests[0].expectedStatus = 'failed' },
    value => { value.suites[0].suites[0].specs[0].tests[0].status = 'flaky' },
    value => { value.suites[0].suites[0].specs[0].tests[0].results = [] },
    value => { value.suites[0].suites[0].specs[0].tests[0].results[0].status = 'skipped' },
  ]) {
    const value = browser()
    change(value)
    assert.throws(() => browserTestCounts(value))
  }
  assert.throws(() => browserTestCounts(null))
  const oversized = browser()
  oversized.suites = Array(10001).fill({ specs: [] })
  assert.throws(() => browserTestCounts(oversized), /bounds/)
  const nested = browser()
  let parent = nested.suites[0]
  for (let depth = 0; depth < 33; depth += 1) {
    parent.suites = [{ specs: [] }]
    parent = parent.suites[0]
  }
  assert.throws(() => browserTestCounts(nested), /bounds/)
})

test('TRX evidence rejects empty discovery and skipped backend tests', () => {
  const report = counters => `<TestRun><ResultSummary><Counters ${counters}/></ResultSummary></TestRun>`
  assert.equal(testCounts(report('total="2" executed="2" passed="2" failed="0"')).total, 2)
  assert.throws(() => testCounts('<TestRun/>'), /discovery/)
  for (const counters of ['total="0" executed="0" passed="0" failed="0"',
    'total="2" executed="1" passed="1" failed="0"', 'total="2" executed="2" passed="1" failed="1"',
    'total="bad" executed="1" passed="1" failed="0"']) {
    assert.throws(() => testCounts(report(counters)))
  }
})
