import assert from 'node:assert/strict'
import { posix } from 'node:path'
import { digest, portablePath } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { readTestXml } from '../../packaging/templates/content/infrastructure/ngb/project-data.mjs'
import { testCounts } from '../../packaging/templates/content/infrastructure/ngb/profiles.mjs'
import { browserTestCounts, frontendCoverage, frontendTestCounts } from '../../packaging/templates/content/infrastructure/ngb/evidence.mjs'
import { inspectCapabilities } from '../../ai-devkit/prepare.mjs'

export function assertUiConsumerLock(lock, artifact, version) {
  assert.match(artifact.integrity, /^sha512-[A-Za-z0-9+/]{86}==$/, 'Missing UI artifact integrity.')
  assert.equal(lock.packages[''].dependencies['@ngbplatform/ui'], version, 'Consumer must pin the candidate UI version.')
  const dependency = lock.packages['node_modules/@ngbplatform/ui']
  assert.equal(dependency.version, version, 'Consumer locked a different UI version.')
  assert.equal(dependency.resolved, `https://registry.npmjs.org/@ngbplatform/ui/-/ui-${version}.tgz`,
    'Consumer must retain the public registry URL.')
  assert.equal(dependency.integrity, artifact.integrity, 'Consumer locked different UI bytes; regenerate candidate locks.')
}

export function exampleSources(payload) {
  const prefix = 'ai/vendor/examples/'
  return Object.fromEntries(payload.files.filter(file => file.path.startsWith(prefix))
    .map(file => [file.path.slice(prefix.length), digest(file.content)]).sort(([left], [right]) => left.localeCompare(right, 'en')))
}

export function exampleFrontendSources(inputs, name) {
  const prefix = `${name}/web/`
  return Object.keys(inputs).filter(path => path.startsWith(`${prefix}src/`)
    && /\.(?:[cm]?[jt]sx?|vue)$/.test(path) && !/\.d\.[cm]?ts$/.test(path))
    .map(path => path.slice(prefix.length)).sort()
}

export function passingTestCases(xml) {
  const counts = testCounts(xml)
  const root = readTestXml(xml)
  const groups = root.children.filter(node => node.name === 'Results')
  assert.equal(groups.length, 1, 'Expected one TRX result collection.')
  const cases = groups[0].children
  assert.equal(cases.length, counts.total, 'TRX counters do not match executed cases.')
  const names = []
  // Parameterized xUnit cases may share a test definition ID; each execution must be unique.
  const ids = new Set()
  for (const item of cases) {
    assert.equal(item.name, 'UnitTestResult', 'Unsupported TRX result kind.')
    assert.equal(item.attributes.outcome, 'Passed', 'Every required case must pass.')
    const { executionId, testId, testName } = item.attributes
    assert.ok(executionId && testId && testName && !ids.has(executionId) && !names.includes(testName),
      'Missing or duplicate TRX execution identity or case name.')
    ids.add(executionId)
    names.push(testName)
  }
  return names.sort()
}

export function passingBrowserCases(report, webRoot) {
  browserTestCounts(report)
  const root = webRoot.replaceAll('\\', '/').replace(/\/$/, '') + '/'
  const testRoot = report.config?.rootDir?.replaceAll('\\', '/')
  assert.ok(testRoot?.startsWith(root), 'Browser test directory is outside the example frontend.')
  const directory = portablePath(testRoot.slice(root.length))
  const files = new Map()
  function visit(suites, titles) {
    for (const suite of suites) {
      assert.ok(typeof suite.title === 'string' && suite.title.length > 0, 'Missing browser suite identity.')
      const parents = titles === null ? [] : [...titles, suite.title]
      for (const spec of suite.specs) {
        const source = posix.join(directory, portablePath(spec.file))
        assert.ok(typeof spec.title === 'string' && spec.title.length > 0, 'Missing browser test title.')
        const cases = files.get(source) ?? []
        for (const test of spec.tests) {
          assert.ok(typeof test.projectName === 'string', 'Missing browser project identity.')
          const identity = JSON.stringify([test.projectName, ...parents, spec.title])
          assert.ok(!cases.includes(identity), 'Duplicate browser case identity.')
          cases.push(identity)
        }
        files.set(source, cases)
      }
      if (suite.suites) visit(suite.suites, parents)
    }
  }
  visit(report.suites, null)
  return files
}

export function bindExampleAcceptance({ payload, report, candidateSha256, runnerSha256, artifacts }) {
  assert.equal(report.schemaVersion, 2, 'Unsupported example evidence schema.')
  assert.equal(report.status, 'passed', 'Independent examples did not pass.')
  assert.equal(report.version, payload.version, 'Example evidence has a different version.')
  assert.equal(report.candidateSha256, candidateSha256, 'Example evidence belongs to another candidate.')
  assert.equal(report.runnerSha256, runnerSha256, 'Example acceptance runner changed.')
  const inspected = inspectCapabilities(payload)
  assert.equal(report.capabilityInputsSha256, inspected.inputsSha256, 'Example capability inputs changed.')
  const inputs = exampleSources(payload)
  assert.deepEqual(report.sources, inputs, 'Packaged example sources differ from the tested sources.')
  const registry = JSON.parse(payload.files.find(file => file.path === 'ai/vendor/capability-registry.json').content)
  const checks = registry.capabilities.flatMap(capability => capability.acceptance).filter(check => check.source)
  const results = []
  const names = new Set()
  for (const example of report.examples) {
    assert.match(example.name, /^[a-z][a-z0-9-]+$/)
    assert.ok(!names.has(example.name), 'Duplicate example evidence.')
    names.add(example.name)
    assert.equal(example.status, 'passed', 'Example did not pass.')
    assert.ok(example.coverage.passed && example.coverage.sourceFiles.required > 0
      && example.coverage.sourceFiles.missingFromReport === 0 && example.coverage.failingFileCount === 0,
    'Example source coverage is incomplete.')
    const coveredSources = new Map()
    if (Object.hasOwn(inputs, `${example.name}/web/package.json`)) {
      const frontend = example.frontend
      assert.equal(frontend?.status, 'passed', 'Example frontend verification is missing or failed.')
      const raw = {}
      for (const key of ['unit', 'coverage', 'browser']) {
        const artifact = frontend.reports[key]
        portablePath(artifact.path)
        const bytes = artifacts.get(artifact.path)
        assert.equal(digest(bytes), artifact.sha256, 'A raw frontend report is missing or changed.')
        raw[key] = JSON.parse(bytes.toString('utf8'))
      }
      assert.deepEqual(frontend.unit, frontendTestCounts(raw.unit), 'Frontend test counts differ from the raw report.')
      assert.deepEqual(frontend.browser, browserTestCounts(raw.browser), 'Browser counts differ from the raw report.')
      assert.deepEqual(frontend.coverage, frontendCoverage(raw.coverage,
        exampleFrontendSources(inputs, example.name), posix.join(report.stage, example.name, 'web')),
      'Frontend coverage differs from its raw report or source inventory.')
      const browser = passingBrowserCases(raw.browser, posix.join(report.stage, example.name, 'web'))
      for (const [file, cases] of browser) {
        const source = `${example.name}/web/${file}`
        assert.ok(Object.hasOwn(inputs, source), 'Browser report names an unpackaged test source.')
        coveredSources.set(source, { kind: 'browser', cases, sha256: frontend.reports.browser.sha256 })
      }
    }
    const projects = new Set()
    for (const suite of example.suites) {
      portablePath(suite.project)
      assert.ok(!projects.has(suite.project), 'Duplicate test suite evidence.')
      projects.add(suite.project)
      for (const artifact of [suite.trx, suite.coverage]) {
        portablePath(artifact.path)
        assert.equal(digest(artifacts.get(artifact.path)), artifact.sha256, 'A raw example report is missing or changed.')
      }
      const trx = artifacts.get(suite.trx.path).toString('utf8')
      assert.deepEqual(testCounts(trx), suite.counts, 'Recorded test counts differ from the raw report.')
      const cases = passingTestCases(trx)
      assert.deepEqual(cases, suite.cases, 'Recorded cases differ from the raw report.')
      for (const source of suite.sources) {
        assert.ok(Object.hasOwn(inputs, source) && source.startsWith(`${example.name}/`), 'Unknown compiled test source.')
        assert.ok(!coveredSources.has(source), 'Ambiguous compiled test source.')
        assert.ok(source.endsWith('.cs'), 'Backend evidence requires a C# source.')
        coveredSources.set(source, { kind: 'backend', cases, sha256: suite.trx.sha256 })
      }
    }
    for (const check of checks.filter(item => item.source.startsWith(`examples/${example.name}/`))) {
      const executed = coveredSources.get(check.source.slice('examples/'.length))
      assert.ok(executed, `Acceptance source was not executed by a matching test runner: ${check.source}`)
      assert.equal(executed.kind, check.kind === 'browser' ? 'browser' : 'backend', 'Acceptance runner kind differs.')
      for (const name of check.cases) {
        assert.ok(executed.cases.includes(name), `Required acceptance case was not executed: ${name}`)
      }
      results.push({ id: check.id, state: 'passed', reportSha256: executed.sha256 })
    }
  }
  assert.ok(names.size > 0, 'No independent examples were executed.')
  const expected = Object.keys(inputs).filter(path => path.endsWith('.slnx')).map(path => posix.dirname(path))
  assert.deepEqual([...names].sort(), expected.sort(), 'Independent example discovery is incomplete.')
  return { schemaVersion: 1, inputsSha256: inspected.inputsSha256, results }
}
