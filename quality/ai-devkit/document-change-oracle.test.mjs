import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'
import { acceptanceMethods, additionalMethods, harnessSha256, inspectDocumentChange, verifyDocumentChange,
  verifyDocumentHarness } from './evaluation-oracles/existing-document-change.mjs'

const firstHash = 'a'.repeat(64)
const secondHash = 'b'.repeat(64)
const harnessFiles = Object.fromEntries(await Promise.all(Object.keys(harnessSha256).map(async path =>
  [path, await readFile(new URL(`./evaluation-fixtures/document-change/${path}`, import.meta.url))])))

function inventories() {
  const before = {
    'EquipmentInspection.Runtime/InspectionPostValidator.cs': firstHash,
    'EquipmentInspection.PostgreSql/db/migrations/V1__baseline.sql': firstHash,
    'EquipmentInspection.Api/EquipmentInspection.Api.csproj': firstHash,
    'NuGet.Config': firstHash,
    'ai/vendor/instructions.md': firstHash,
  }
  const after = { ...before,
    'EquipmentInspection.Runtime/InspectionPostValidator.cs': secondHash,
    'EquipmentInspection.PostgreSql/db/migrations/V2__completion_note.sql': secondHash,
  }
  return { before, after }
}

function report(methods = [...Object.values(acceptanceMethods), ...additionalMethods], outcome = 'Passed') {
  const results = methods.map((method, index) => `<UnitTestResult executionId="execution-${index}" testId="test-${index}" outcome="${outcome}"
    testName="EquipmentInspection.IntegrationTests.DocumentChangeAcceptance.${method}" />`).join('')
  return `<TestRun><Results>${results}</Results><ResultSummary><Counters total="${methods.length}"
    executed="${methods.length}" passed="${methods.length}" failed="0" /></ResultSummary></TestRun>`
}

test('document oracle freezes its independent harness and requires exact successful executable cases', () => {
  verifyDocumentHarness(harnessFiles)
  const result = verifyDocumentChange({ ...inventories(), harnessFiles, trx: report() })
  assert.equal(result.changed.length, 2)
  assert.equal(result.checks.length, 6)
  assert.match(result.producedTreeSha256, /^[a-f0-9]{64}$/)
  assert.ok(result.checks.every(check => check.state === 'passed'))
  assert.throws(() => verifyDocumentHarness({}), /harness changed/)
  assert.throws(() => verifyDocumentHarness({ ...harnessFiles, 'DocumentChangeAcceptance.cs': 'agent replacement' }), /harness changed/)
  assert.throws(() => verifyDocumentChange({ ...inventories(), harnessFiles, trx: report(Object.values(acceptanceMethods).slice(1)) }), /discovery/)
  assert.throws(() => verifyDocumentChange({ ...inventories(), harnessFiles, trx: report(undefined, 'Failed') }), /must pass/)
})

test('document changes may update existing frontend tests and add matching test sources', () => {
  const value = inventories()
  value.before['web/tests/e2e/inspection.spec.ts'] = firstHash
  value.after['web/tests/e2e/inspection.spec.ts'] = secondHash
  value.after['web/tests/unit/completion.spec.ts'] = secondHash
  const result = inspectDocumentChange(value.before, value.after)
  assert.equal(result.changed.length, 4)
})

test('document task preserves SDK build inputs, existing migrations and local instructions', () => {
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
    value => { value.after['web/src/bootstrap.ts'] = secondHash },
    value => { value.after['web/vitest.config.ts'] = secondHash },
    value => { value.after['web/tests/e2e/setup.mjs'] = secondHash },
    value => { value.after['new.ts'] = secondHash },
    value => { value.after['AGENTS.md'] = secondHash },
    value => { value.after['.claude/new.md'] = secondHash },
    value => { delete value.after['EquipmentInspection.PostgreSql/db/migrations/V2__completion_note.sql'] },
  ]) {
    const value = inventories()
    change(value)
    assert.throws(() => inspectDocumentChange(value.before, value.after))
  }
})
