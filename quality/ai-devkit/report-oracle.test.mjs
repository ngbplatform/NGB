import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'
import { acceptanceMethods, harnessSha256, harnessSources, inspectChange, testClass,
  verifyChange, verifyHarness } from './evaluation-oracles/report-extension.mjs'

const firstHash = 'a'.repeat(64)
const secondHash = 'b'.repeat(64)
const harnessFiles = Object.fromEntries(await Promise.all(Object.keys(harnessSha256).map(async path =>
  [path, await readFile(new URL(`./evaluation-fixtures/${harnessSources[path]}`, import.meta.url))])))

test('the report seed extends shared example sources only with its category prerequisite', async () => {
  const additions = {
    'EquipmentInspection.Definitions/EquipmentDefinitions.cs': source => source.replace(
      ',\n                        new("category_id", ColumnType.Guid, UiLabel: "Category",\n'
        + '                            Lookup: new CatalogLookupSourceMetadata(EquipmentCategoryDefinitions.TypeCode))', ''),
    'EquipmentInspection.Runtime/EquipmentRuntime.cs': source => source.replace(
      '        services.AddSingleton<IDefinitionsContributor, EquipmentCategoryDefinitions>();\n'
        + '        services.AddSingleton<IDefinitionsContributor, EquipmentCategoryRuntimeDefinitions>();\n'
        + '        services.AddScoped<EquipmentCategoryValidator>();\n'
        + '        services.AddScoped<ICatalogUpsertValidator>(services =>\n'
        + '            services.GetRequiredService<EquipmentCategoryValidator>());\n', ''),
    'EquipmentInspection.PostgreSql/EquipmentPersistence.cs': source => source.replace(
      '        services.AddScoped<ICatalogTypeStorage>(provider => new PostgresHeadCatalogTypeStorage(\n'
        + '            provider.GetRequiredService<IUnitOfWork>(),\n'
        + '            EquipmentCategoryDefinitions.TypeCode,\n'
        + '            "cat_example_equipment_category",\n'
        + '            [\n'
        + '                PostgresHeadCatalogTypeStorage.Column.DraftString("code", "code"),\n'
        + '                PostgresHeadCatalogTypeStorage.Column.DraftString("name", "name")\n'
        + '            ]));\n', ''),
  }
  const manifest = JSON.parse(await readFile(new URL('./evaluation-fixtures/report-seed.json', import.meta.url)))
  const shared = []
  for (const path of Object.keys(manifest.files)) {
    const example = await readFile(new URL(`../../ai-devkit/examples/equipment-inspection/${path}`, import.meta.url), 'utf8')
      .catch(error => {
        if (error.code !== 'ENOENT') throw error
        return null
      })
    if (example === null) continue
    shared.push(path)
    assert.ok(Object.hasOwn(additions, path), `Review the new shared report-seed override: ${path}`)
    const source = await readFile(new URL(`./evaluation-fixtures/report-seed/${path}`, import.meta.url), 'utf8')
    const baseline = additions[path](source)
    assert.notEqual(baseline, source, `The category prerequisite is missing: ${path}`)
    assert.equal(baseline, example, `The report seed lost or changed base application behavior: ${path}`)
  }
  assert.deepEqual(shared.sort(), Object.keys(additions).sort())
})

function inventories() {
  const before = {
    'EquipmentInspection.Definitions/EquipmentDefinitions.cs': firstHash,
    'EquipmentInspection.PostgreSql/db/migrations/V1__baseline.sql': firstHash,
    'EquipmentInspection.Api/EquipmentInspection.Api.csproj': firstHash,
    'NuGet.Config': firstHash,
    'ai/vendor/instructions.md': firstHash,
  }
  const after = { ...before,
    'EquipmentInspection.Runtime/CategoryReportDefinitions.cs': secondHash,
    'EquipmentInspection.PostgreSql/CategoryReportDataset.cs': secondHash,
  }
  return { before, after }
}

function report(methods = acceptanceMethods, outcome = 'Passed') {
  const results = methods.map((method, index) => `<UnitTestResult executionId="execution-${index}" testId="test-${index}" outcome="${outcome}"
    testName="EquipmentInspection.IntegrationTests.${testClass}.${method}" />`).join('')
  return `<TestRun><Results>${results}</Results><ResultSummary><Counters total="${methods.length}"
    executed="${methods.length}" passed="${methods.length}" failed="0" /></ResultSummary></TestRun>`
}

test('report oracle freezes shared harness sources and requires all independent HTTP cases', () => {
  verifyHarness(harnessFiles)
  const result = verifyChange({ ...inventories(), harnessFiles, trx: report() })
  assert.equal(result.changed.length, 2)
  assert.equal(result.checks.length, 6)
  assert.match(result.producedTreeSha256, /^[a-f0-9]{64}$/)
  assert.ok(result.checks.every(check => check.state === 'passed'))
  const nested = inventories()
  delete nested.after['EquipmentInspection.Runtime/CategoryReportDefinitions.cs']
  delete nested.after['EquipmentInspection.PostgreSql/CategoryReportDataset.cs']
  nested.after['EquipmentInspection.Runtime/Reports/CategoryReportDefinitions.cs'] = secondHash
  nested.after['EquipmentInspection.PostgreSql/Reports/CategoryReportDataset.cs'] = secondHash
  assert.equal(inspectChange(nested.before, nested.after).changed.length, 2)
  assert.throws(() => verifyHarness({}), /harness changed/)
  assert.throws(() => verifyHarness({ ...harnessFiles, 'ReportAcceptance.cs': 'agent replacement' }), /harness changed/)
  assert.throws(() => verifyChange({ ...inventories(), harnessFiles, trx: report(acceptanceMethods.slice(1)) }), /discovery/)
  assert.throws(() => verifyChange({ ...inventories(), harnessFiles, trx: report(undefined, 'Failed') }), /must pass/)
})

test('report task requires provider-neutral metadata and provider-owned execution while preserving SDK inputs and instructions', () => {
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
    value => { delete value.after['EquipmentInspection.Runtime/CategoryReportDefinitions.cs'] },
    value => { delete value.after['EquipmentInspection.PostgreSql/CategoryReportDataset.cs'] },
  ]) {
    const value = inventories()
    change(value)
    assert.throws(() => inspectChange(value.before, value.after))
  }
})
