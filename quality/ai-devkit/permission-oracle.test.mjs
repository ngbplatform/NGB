import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'
import { acceptanceMethods, browserCases, harnessSha256, harnessSources, inspectChange, testClass,
  verifyChange, verifyHarness } from './evaluation-oracles/permission-correction.mjs'

const firstHash = 'a'.repeat(64)
const secondHash = 'b'.repeat(64)
const harnessFiles = Object.fromEntries(await Promise.all(Object.keys(harnessSha256).map(async path =>
  [path, await readFile(new URL(`./evaluation-fixtures/${harnessSources[path]}`, import.meta.url))])))

function inventories() {
  const before = {
    'EquipmentInspection.Definitions/EquipmentDefinitions.cs': firstHash,
    'EquipmentInspection.PostgreSql/db/migrations/V1__baseline.sql': firstHash,
    'EquipmentInspection.Api/EquipmentInspection.Api.csproj': firstHash,
    'NuGet.Config': firstHash,
    'EquipmentInspection.Api/ApplicationRoles.cs': firstHash,
    'ai/vendor/instructions.md': firstHash,
  }
  const after = { ...before,
    'EquipmentInspection.Api/Program.cs': secondHash,
    'web/src/main.ts': secondHash,
  }
  return { before, after }
}

function report(methods = acceptanceMethods, outcome = 'Passed') {
  const results = methods.map((method, index) => `<UnitTestResult executionId="execution-${index}" testId="test-${index}" outcome="${outcome}"
    testName="EquipmentInspection.IntegrationTests.${testClass}.${method}" />`).join('')
  return `<TestRun><Results>${results}</Results><ResultSummary><Counters total="${methods.length}"
    executed="${methods.length}" passed="${methods.length}" failed="0" /></ResultSummary></TestRun>`
}

function browserReport(names = browserCases) {
  return { suites: [{ file: 'permissions.spec.mjs', specs: names.map(title => ({
    title, ok: true, tests: [{ expectedStatus: 'passed', status: 'expected', results: [{ status: 'passed' }] }],
  })) }], errors: [], stats: { expected: names.length, unexpected: 0, skipped: 0, flaky: 0 } }
}

test('permission oracle freezes shared harness sources and requires all independent HTTP cases', () => {
  verifyHarness(harnessFiles)
  const result = verifyChange({ ...inventories(), harnessFiles, browserReport: browserReport(), trx: report() })
  assert.equal(result.changed.length, 2)
  assert.equal(result.checks.length, 6)
  assert.match(result.producedTreeSha256, /^[a-f0-9]{64}$/)
  assert.ok(result.checks.every(check => check.state === 'passed'))
  const nested = inventories()
  delete nested.after['EquipmentInspection.Api/Program.cs']
  nested.after['EquipmentInspection.Api/Configuration/AuthorizationRegistration.cs'] = secondHash
  assert.equal(inspectChange(nested.before, nested.after).changed.length, 2)
  assert.throws(() => verifyHarness({}), /harness changed/)
  assert.throws(() => verifyHarness({ ...harnessFiles, 'PermissionAcceptance.cs': 'agent replacement' }), /harness changed/)
  assert.throws(() => verifyChange({ ...inventories(), harnessFiles, browserReport: browserReport(), trx: report(acceptanceMethods.slice(1)) }), /discovery/)
  assert.throws(() => verifyChange({ ...inventories(), harnessFiles, browserReport: browserReport(), trx: report(undefined, 'Failed') }), /must pass/)
})

test('permission task requires application and frontend corrections while preserving SDK inputs and instructions', () => {
  for (const change of [
    value => { value.before = {} },
    value => { value.after['../outside.cs'] = secondHash },
    value => { value.after['new.cs'] = 'invalid' },
    value => { delete value.after['NuGet.Config'] },
    value => { value.after['EquipmentInspection.PostgreSql/db/migrations/V1__baseline.sql'] = secondHash },
    value => { value.after['EquipmentInspection.Api/EquipmentInspection.Api.csproj'] = secondHash },
    value => { value.after['ai/vendor/instructions.md'] = secondHash },
    value => { value.after = { ...value.before } },
    value => { value.after['Directory.Build.targets'] = secondHash },
    value => { value.after['new.dll'] = secondHash },
    value => { value.after['EquipmentInspection.Api/ApplicationRoles.cs'] = secondHash },
    value => { value.after['AGENTS.md'] = secondHash },
    value => { value.after['.claude/new.md'] = secondHash },
    value => { delete value.after['EquipmentInspection.Api/Program.cs'] },
    value => { delete value.after['web/src/main.ts'] },
  ]) {
    const value = inventories()
    change(value)
    assert.throws(() => inspectChange(value.before, value.after))
  }
})

test('permission oracle requires the exact independent browser cases in addition to API checks', () => {
  for (const change of [
    value => { value.errors.push({ message: 'startup failed' }) },
    value => { value.suites[0].specs.pop(); value.stats.expected -= 1 },
    value => { value.suites.push({ file: 'extra.mjs', specs: [] }) },
    value => { value.suites[0].file = 'agent-tests.mjs' },
    value => { value.suites[0].specs[0].title = 'different workflow' },
    value => { value.suites[0].specs[0].tests[0].status = 'skipped' },
  ]) {
    const value = browserReport()
    change(value)
    assert.throws(() => verifyChange({ ...inventories(), harnessFiles, trx: report(), browserReport: value }))
  }
})
