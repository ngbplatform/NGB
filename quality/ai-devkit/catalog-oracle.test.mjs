import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'
import { acceptanceMethods, harnessSha256, harnessSources, inspectChange, testClass,
  verifyChange, verifyHarness } from './evaluation-oracles/new-catalog.mjs'

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
    'ai/vendor/instructions.md': firstHash,
  }
  const after = { ...before,
    'EquipmentInspection.Definitions/EquipmentCategoryDefinitions.cs': secondHash,
    'EquipmentInspection.PostgreSql/db/migrations/V2__equipment_category.sql': secondHash,
  }
  return { before, after }
}

function report(methods = acceptanceMethods, outcome = 'Passed') {
  const results = methods.map((method, index) => `<UnitTestResult executionId="execution-${index}" testId="test-${index}" outcome="${outcome}"
    testName="EquipmentInspection.IntegrationTests.${testClass}.${method}" />`).join('')
  return `<TestRun><Results>${results}</Results><ResultSummary><Counters total="${methods.length}"
    executed="${methods.length}" passed="${methods.length}" failed="0" /></ResultSummary></TestRun>`
}

test('catalog oracle freezes shared harness sources and requires all independent HTTP cases', () => {
  verifyHarness(harnessFiles)
  const result = verifyChange({ ...inventories(), harnessFiles, trx: report() })
  assert.equal(result.changed.length, 2)
  assert.equal(result.checks.length, 6)
  assert.match(result.producedTreeSha256, /^[a-f0-9]{64}$/)
  assert.ok(result.checks.every(check => check.state === 'passed'))
  const nested = inventories()
  delete nested.after['EquipmentInspection.Definitions/EquipmentCategoryDefinitions.cs']
  nested.after['EquipmentInspection.Definitions/Catalogs/EquipmentCategoryDefinitions.cs'] = secondHash
  assert.equal(inspectChange(nested.before, nested.after).changed.length, 2)
  assert.throws(() => verifyHarness({}), /harness changed/)
  assert.throws(() => verifyHarness({ ...harnessFiles, 'CatalogAcceptance.cs': 'agent replacement' }), /harness changed/)
  assert.throws(() => verifyChange({ ...inventories(), harnessFiles, trx: report(acceptanceMethods.slice(1)) }), /discovery/)
  assert.throws(() => verifyChange({ ...inventories(), harnessFiles, trx: report(undefined, 'Failed') }), /must pass/)
})

test('catalog task requires metadata and a new migration while preserving SDK inputs and instructions', () => {
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
    value => { value.after['AGENTS.md'] = secondHash },
    value => { value.after['.claude/new.md'] = secondHash },
    value => { delete value.after['EquipmentInspection.Definitions/EquipmentCategoryDefinitions.cs'] },
    value => { delete value.after['EquipmentInspection.PostgreSql/db/migrations/V2__equipment_category.sql'] },
  ]) {
    const value = inventories()
    change(value)
    assert.throws(() => inspectChange(value.before, value.after))
  }
})
