import assert from 'node:assert/strict'
import { digest, portablePath } from '../../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { passingTestCases } from '../example-evidence.mjs'

// Shared host sources are frozen independently from all agent-writable files.
export const harnessSha256 = {
  'AgentAcceptance.csproj': '278434490d506d1be70eab74a6944d4a1614ea51661e00bc8ca5140e4be42c6d',
  'Controllers.cs': '81ce3d09eeae2a1db236373792ce45afd49e43e8aa02aa80b4ea26ed977cf7b9',
  'ReportAcceptance.cs': 'c10fb2e382200a2e194e3932df00a0f2908f56e5178c2508d228afbb18920fe3',
  'InspectionHost.cs': '308d11ba72002e3fe2e48a916a017f8276bdc20369ed06342ca4081481043929',
  'InspectionPermissionSource.cs': '0f1355c5f82d4d9f549ec47cb54a170ba26e98864a6f63ac1d49d088b453f115',
  'InspectionRequests.cs': '6d29f0826cdb6be7b7c87fb7732ef078e93038cd124d81cb07b38764cbcd1d59',
  'TestInfrastructure.cs': '7888db0e65288a191869e051d93e5c2f80bf2b949394d09596398a283230e311',
  'InspectionTestFramework.cs': 'a3886e817857ecbf234eb1bd67c7436a2e967a3d316825f1a9326e507ee973d4',
  'TestIdentity.cs': 'fbe9350164e3d66abb762bc4ad087435baa553f52a7b237804da4d3e2464fe2b',
  'xunit.runner.json': 'ebeeae03ee3082c11aeb2729d9d0c4bd489a1c86a2a45078c63016c3da3bb66c',
}

export const testClass = 'ReportAcceptance'
export const harnessSources = Object.fromEntries(Object.keys(harnessSha256).map(path =>
  [path, `${path === 'ReportAcceptance.cs' ? 'report-extension' : 'document-change'}/${path}`]))
export const acceptanceMethods = [
  'Category_report_pages_known_totals_and_filters_without_counting_drafts_or_uncategorized_equipment',
  'Category_report_rejects_unbounded_requests_and_changed_or_tampered_continuations',
  'Public_provider_returns_bounded_aggregated_category_rows_before_sheet_rendering',
  'Report_definition_and_execution_obey_both_administrator_contracts_and_deny_other_actors',
]

export function verifyHarness(files) {
  const actual = Object.fromEntries(Object.entries(files).map(([path, bytes]) => [path, digest(bytes)]))
  assert.deepEqual(actual, harnessSha256, 'The independent report harness changed.')
}

export function inspectChange(before, after) {
  assert.ok(Object.keys(before).length > 0, 'The seed application inventory is empty.')
  for (const inventory of [before, after]) {
    for (const [path, hash] of Object.entries(inventory)) {
      portablePath(path)
      assert.match(hash, /^[a-f0-9]{64}$/, 'Invalid application file digest.')
    }
  }
  for (const path of Object.keys(before)) {
    assert.ok(Object.hasOwn(after, path), `The agent removed a seed file: ${path}`)
    if (/\.(?:sql|csproj|props|targets|slnx|json|mjs|lock)$|(^|\/)(?:ai|\.agents|\.claude|\.codex)\/|(?:^|\/)NuGet\.Config$/i.test(path)) {
      assert.equal(after[path], before[path], `The task must preserve existing migrations, guidance and build inputs: ${path}`)
    }
  }
  const changed = Object.keys(after).filter(path => after[path] !== before[path]).sort()
  assert.ok(changed.length > 0, 'The agent made no application change.')
  for (const path of changed) {
    assert.match(path, /\.(?:cs|sql|md)$/, 'This task does not require new executable/build/dependency configuration.')
    assert.doesNotMatch(path, /(^|\/)(?:ai|\.agents|\.claude|\.codex)\/|(?:^|\/)(?:AGENTS|CLAUDE)\.md$/i,
      'The report task must not rewrite agent instructions.')
  }
  assert.ok(changed.some(path => /^EquipmentInspection\.Runtime\/.+\.cs$/.test(path)),
    'Provider-neutral report definitions belong in the application Runtime project.')
  assert.ok(changed.some(path => /^EquipmentInspection\.PostgreSql\/.+\.cs$/.test(path)),
    'The report needs a provider-owned dataset binding.')
  return { changed, producedTreeSha256: digest(JSON.stringify(Object.entries(after).sort())) }
}

// The runner separately requires source review, isolated dependency resolution
// and a fresh build. Agent-written tests and reports are not acceptance inputs.
export function verifyChange({ trx, before, after, harnessFiles }) {
  verifyHarness(harnessFiles)
  const tree = inspectChange(before, after)
  const expected = acceptanceMethods.map(method => `EquipmentInspection.IntegrationTests.${testClass}.${method}`).sort()
  assert.deepEqual(passingTestCases(trx), expected, 'Independent report test discovery is incomplete.')
  return {
    ...tree,
    checks: ['public-report-contracts', 'known-totals', 'paging', 'filters',
      'unauthorized-refused', 'bounded-provider-query'].map(id => ({ id, state: 'passed' })),
  }
}
