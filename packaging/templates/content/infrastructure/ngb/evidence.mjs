import assert from 'node:assert/strict'
import { posix } from 'node:path'
import { distinctPaths, portablePath } from './kit-contracts.mjs'

export function uniqueCoverageReport(entries) {
  assert.ok(Array.isArray(entries) && entries.length > 0 && entries.length <= 256, 'Missing or excessive backend coverage attachments.')
  distinctPaths(entries.map(entry => entry.path))
  for (const entry of entries) assert.match(entry.sha256, /^[a-f0-9]{64}$/, 'Invalid backend coverage attachment hash.')
  assert.equal(new Set(entries.map(entry => entry.sha256)).size, 1, 'A test project produced differing coverage reports; declare separate execution profiles.')
  // VSTest copies the collector output into the TRX deployment directory.
  // Identical copies represent one measurement, not independent test evidence.
  return [...entries].sort((left, right) => left.path.localeCompare(right.path, 'en'))[0]
}

export function frontendTestCounts(value) {
  assert.ok(value && typeof value === 'object', 'Missing frontend test report.')
  const keys = ['numTotalTests', 'numPassedTests', 'numFailedTests', 'numPendingTests', 'numTodoTests']
  for (const key of keys) assert.ok(Number.isSafeInteger(value[key]) && value[key] >= 0, `Invalid frontend counter: ${key}`)
  assert.ok(value.success === true && value.numTotalTests > 0 && value.numTotalTests === value.numPassedTests
    && value.numFailedTests === 0 && value.numPendingTests === 0 && value.numTodoTests === 0,
  'Frontend tests contain failures, skips, TODOs or no discovered tests.')
  assert.ok(Array.isArray(value.testResults) && value.testResults.length > 0, 'Missing per-file test discovery.')
  const assertions = value.testResults.flatMap(file => {
    assert.equal(file.status, 'passed', 'Frontend test file did not pass.')
    assert.ok(Array.isArray(file.assertionResults) && file.assertionResults.length > 0, 'Empty frontend test file.')
    return file.assertionResults
  })
  assert.equal(assertions.length, value.numTotalTests, 'Frontend discovery counters differ from actual assertions.')
  assert.ok(assertions.every(assertion => assertion.status === 'passed'), 'A frontend assertion did not execute successfully.')
  return { total: value.numTotalTests, passed: value.numPassedTests, failed: 0, skipped: 0 }
}

export function frontendCoverage(value, inventory, root) {
  assert.ok(value && typeof value === 'object', 'Missing frontend coverage report.')
  const prefix = root.replaceAll('\\', '/').replace(/\/$/, '') + '/'
  const entries = new Map()
  for (const [path, coverage] of Object.entries(value)) {
    if (path === 'total') continue
    const normalized = path.replaceAll('\\', '/')
    assert.ok(normalized.startsWith(prefix), 'Coverage contains a file outside the configured frontend.')
    const relative = portablePath(normalized.slice(prefix.length))
    assert.equal(posix.normalize(relative), relative)
    assert.ok(!entries.has(relative), 'Duplicate frontend coverage file.')
    entries.set(relative, coverage)
  }
  assert.ok(inventory.length > 0, 'Frontend source inventory is empty.')
  assert.deepEqual([...entries.keys()].sort(), [...inventory].sort(), 'Frontend coverage must include every application source file exactly once.')
  for (const [path, coverage] of entries) {
    for (const metric of ['lines', 'branches', 'functions', 'statements']) {
      const row = coverage[metric]
      assert.ok(row && Number.isSafeInteger(row.total) && row.total >= 0 && row.covered === row.total
        && row.skipped === 0 && row.pct === 100, `Frontend ${metric} coverage is incomplete: ${path}`)
    }
  }
  return { files: entries.size, lines: 100, branches: 100, functions: 100, statements: 100 }
}

export function browserTestCounts(value) {
  assert.ok(value && Array.isArray(value.suites) && Array.isArray(value.errors), 'Missing browser test report.')
  assert.equal(value.errors.length, 0, 'Browser setup or teardown failed.')
  const tests = []
  function visit(suites, depth) {
    assert.ok(depth <= 32 && suites.length <= 10000, 'Browser suite inventory exceeds its bounds.')
    for (const suite of suites) {
      assert.ok(Array.isArray(suite.specs), 'Invalid browser suite.')
      for (const spec of suite.specs) {
        assert.ok(spec.ok && Array.isArray(spec.tests) && spec.tests.length > 0, 'An expected browser workflow did not pass.')
        tests.push(...spec.tests)
        assert.ok(tests.length <= 10000, 'Browser test inventory exceeds its bounds.')
      }
      if (suite.suites) visit(suite.suites, depth + 1)
    }
  }
  visit(value.suites, 0)
  assert.ok(tests.length > 0, 'No browser tests were discovered.')
  for (const test of tests) {
    assert.equal(test.expectedStatus, 'passed', 'Required browser tests cannot expect failure or skip.')
    assert.equal(test.status, 'expected', 'A browser test was skipped, flaky or failed.')
    assert.equal(test.results?.length, 1, 'Required browser tests must execute once without retries.')
    assert.equal(test.results[0].status, 'passed', 'A browser test did not execute successfully.')
  }
  assert.ok(value.stats?.expected === tests.length && value.stats.unexpected === 0
    && value.stats.flaky === 0 && value.stats.skipped === 0, 'Browser summary and discovered tests differ.')
  return { total: tests.length, passed: tests.length, failed: 0, skipped: 0 }
}
