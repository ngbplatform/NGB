import assert from 'node:assert/strict'
import { portablePath } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { browserTestCounts, frontendCoverage, frontendTestCounts } from '../../packaging/templates/content/infrastructure/ngb/evidence.mjs'
import { passingBrowserCases } from './example-evidence.mjs'

export function requiresFrontendAcceptance(task) {
  return ['existing-document-change', 'permission-correction'].includes(task)
}

export function reviewFrontendTests(changed, reviewed) {
  const tests = changed.filter(path => /^web\/tests\/.+\.ts$/.test(path)).sort()
  assert.deepEqual(reviewed, tests, 'Review every changed frontend test source before executing it.')
}

export function frontendUnitFailure(report) {
  const failures = (report?.testResults ?? []).flatMap(file => {
    const assertions = (file.assertionResults ?? []).filter(result => result.status === 'failed')
    return assertions.map(result => result.fullName || result.title || file.name)
  }).filter(name => typeof name === 'string' && name.length > 0)
  return failures.length
    ? `${failures.length} frontend test(s) failed: ${failures.slice(0, 3).map(name => name.replace(/\s+/g, ' ').slice(0, 240)).join('; ')}`
    : null
}

export function verifyAuthoredFrontend({ files, webRoot, unit, coverage, browser }) {
  const prefix = webRoot.replaceAll('\\', '/').replace(/\/$/, '') + '/'
  const paths = Object.keys(files).filter(path => path.startsWith('web/')).map(path => path.slice(4)).sort()
  const sources = paths.filter(path => path.startsWith('src/')
    && /\.(?:[cm]?[jt]sx?|vue)$/.test(path) && !/\.d\.[cm]?ts$/.test(path))
  const unitCounts = frontendTestCounts(unit)
  const unitFiles = unit.testResults.map(file => {
    assert.equal(typeof file.name, 'string', 'Missing frontend test source identity.')
    const normalized = file.name.replaceAll('\\', '/')
    assert.ok(normalized.startsWith(prefix), 'Frontend tests ran outside the reviewed application.')
    return portablePath(normalized.slice(prefix.length))
  }).sort()
  assert.deepEqual(unitFiles, paths.filter(path => /^tests\/unit\/.+\.spec\.ts$/.test(path)),
    'Frontend unit discovery must include every application test file exactly once.')
  const browserFiles = [...passingBrowserCases(browser, webRoot).keys()].sort()
  assert.deepEqual(browserFiles, paths.filter(path => /^tests\/e2e\/.+\.spec\.ts$/.test(path)),
    'Browser discovery must include every application test file exactly once.')
  return {
    status: 'passed',
    unit: unitCounts,
    browser: browserTestCounts(browser),
    coverage: frontendCoverage(coverage, sources, webRoot),
  }
}
