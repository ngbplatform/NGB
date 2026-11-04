import assert from 'node:assert/strict'
import test from 'node:test'
import { requiresFrontendAcceptance, reviewFrontendTests, verifyAuthoredFrontend } from './development-evidence.mjs'

function fixture() {
  const webRoot = '/isolated/application/web'
  const metric = { total: 1, covered: 1, skipped: 0, pct: 100 }
  return {
    webRoot,
    files: Object.fromEntries(['README.md', 'web/src/main.ts', 'web/src/App.vue', 'web/src/env.d.ts',
      'web/src/logo.svg', 'web/tests/unit/bootstrap.spec.ts', 'web/tests/e2e/inspection.spec.ts']
      .map(path => [path, 'reviewed-source'])),
    unit: {
      success: true, numTotalTests: 1, numPassedTests: 1, numFailedTests: 0, numPendingTests: 0, numTodoTests: 0,
      testResults: [{ name: `${webRoot}/tests/unit/bootstrap.spec.ts`, status: 'passed',
        assertionResults: [{ status: 'passed' }] }],
    },
    coverage: Object.fromEntries(['src/main.ts', 'src/App.vue'].map(path => [`${webRoot}/${path}`,
      { lines: { ...metric }, branches: { ...metric }, functions: { ...metric }, statements: { ...metric } }])),
    browser: {
      config: { rootDir: `${webRoot}/tests/e2e` }, errors: [],
      suites: [{ title: 'inspection.spec.ts', specs: [{ file: 'inspection.spec.ts', title: 'saves a draft', ok: true,
        tests: [{ projectName: 'chromium', expectedStatus: 'passed', status: 'expected', results: [{ status: 'passed' }] }] }] }],
      stats: { expected: 1, unexpected: 0, flaky: 0, skipped: 0 },
    },
  }
}

test('document and permission changes require reviewed frontend tests as well as independent acceptance', () => {
  for (const task of ['existing-document-change', 'permission-correction']) assert.ok(requiresFrontendAcceptance(task))
  for (const task of ['new-catalog', 'report-extension']) assert.equal(requiresFrontendAcceptance(task), false)
  const changed = ['EquipmentInspection.Tests/ContractTests.cs', 'web/src/bootstrap.ts',
    'web/tests/e2e/inspection.spec.ts', 'web/tests/unit/bootstrap.spec.ts']
  const reviewed = changed.slice(2)
  reviewFrontendTests(changed, reviewed)
  reviewFrontendTests(changed.slice(0, 2), [])
  assert.throws(() => reviewFrontendTests(changed, reviewed.slice(1)), /Review every/)
  assert.throws(() => reviewFrontendTests(changed, [...reviewed, reviewed[0]]), /Review every/)
  assert.throws(() => reviewFrontendTests(changed, undefined), /Review every/)
})

test('authored frontend acceptance binds all source coverage and unit/browser file discovery', () => {
  const result = verifyAuthoredFrontend(fixture())
  assert.equal(result.status, 'passed')
  assert.deepEqual(result.unit, { total: 1, passed: 1, failed: 0, skipped: 0 })
  assert.deepEqual(result.browser, result.unit)
  assert.deepEqual(result.coverage, { files: 2, lines: 100, branches: 100, functions: 100, statements: 100 })
  const windows = fixture()
  windows.webRoot = '\\isolated\\application\\web\\'
  windows.unit.testResults[0].name = '\\isolated\\application\\web\\tests\\unit\\bootstrap.spec.ts'
  assert.deepEqual(verifyAuthoredFrontend(windows), result)
})

test('authored frontend acceptance refuses missing, substituted, skipped or failing evidence', () => {
  for (const mutate of [
    value => { value.unit.numPendingTests = 1 },
    value => { delete value.unit.testResults[0].name },
    value => { value.unit.testResults[0].name = '/other/application/tests/unit/bootstrap.spec.ts' },
    value => { value.unit.testResults[0].name = value.webRoot + '/../outside.spec.ts' },
    value => { value.files['web/tests/unit/missing.spec.ts'] = 'unexecuted' },
    value => { delete value.files['web/tests/unit/bootstrap.spec.ts'] },
    value => { value.files['web/tests/e2e/missing.spec.ts'] = 'unexecuted' },
    value => { value.browser.suites[0].specs[0].file = 'substituted.spec.ts' },
    value => { value.browser.suites[0].specs[0].tests[0].results[0].status = 'failed' },
    value => { value.files['web/src/uncovered.ts'] = 'unmeasured' },
    value => { value.coverage[value.webRoot + '/src/main.ts'].branches.covered = 0 },
  ]) {
    const value = fixture()
    mutate(value)
    assert.throws(() => verifyAuthoredFrontend(value))
  }
})
