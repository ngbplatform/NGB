import assert from 'node:assert/strict'
import { digest, portablePath } from '../../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { browserTestCounts } from '../../../packaging/templates/content/infrastructure/ngb/evidence.mjs'
import { passingTestCases } from '../example-evidence.mjs'

// Shared host sources are frozen independently from all agent-writable files.
export const harnessSha256 = {
  'Controllers.cs': '81ce3d09eeae2a1db236373792ce45afd49e43e8aa02aa80b4ea26ed977cf7b9',
  'InspectionHost.cs': '5883a1caaf1fecaa7a1997f8eeed6cde376241d1afc1d23e3cc77579170e95f4',
  'InspectionPermissionSource.cs': '0f1355c5f82d4d9f549ec47cb54a170ba26e98864a6f63ac1d49d088b453f115',
  'InspectionRequests.cs': '6d29f0826cdb6be7b7c87fb7732ef078e93038cd124d81cb07b38764cbcd1d59',
  'TestIdentity.cs': '1ea714b3727d846b9a455ab1f0a49eb1931b8bda6fde54cac2b696173fa76466',
  'xunit.runner.json': 'ebeeae03ee3082c11aeb2729d9d0c4bd489a1c86a2a45078c63016c3da3bb66c',
  'AgentAcceptance.csproj': '47ed23951fe71e6fcc52d07cca1e34feb71b74add07fbac8d5270ed6e71378a6',
  'PermissionAcceptance.cs': '5c3d943f42eb7b9d9bcfb4f16adc877bb694e3f7b681fb7b657d6a496f579aa5',
  'PermissionBrowserHost.cs': 'd50996aab8aa3a739401b63f2898730006fdb93ffbfda548d682194848b3c69e',
  'browser-setup.mjs': '63a3f44ecc62ae4c0067916aa5a077dc335cbcee8ecdc92536fea4dcef38fdef',
  'permissions.spec.mjs': '925b4a65c63c8bdce7794f1b184dee95210846b2174b2f1bd29fa8c2b3f7bb28',
  'playwright.config.mjs': 'bcfc48f09d61e64bb98781407bd6b4bc084de4eab9bd5cf7e6edff34312c98c0',
}

export const testClass = 'PermissionAcceptance'
export const harnessSources = {
  'Controllers.cs': 'document-change/Controllers.cs',
  'InspectionHost.cs': 'document-change/InspectionHost.cs',
  'InspectionPermissionSource.cs': 'document-change/InspectionPermissionSource.cs',
  'InspectionRequests.cs': 'document-change/InspectionRequests.cs',
  'TestIdentity.cs': 'document-change/TestIdentity.cs',
  'xunit.runner.json': 'document-change/xunit.runner.json',
  'AgentAcceptance.csproj': 'permission-correction/AgentAcceptance.csproj',
  'PermissionAcceptance.cs': 'permission-correction/PermissionAcceptance.cs',
  'PermissionBrowserHost.cs': 'permission-correction/PermissionBrowserHost.cs',
  'browser-setup.mjs': 'permission-correction/browser-setup.mjs',
  'permissions.spec.mjs': 'permission-correction/permissions.spec.mjs',
  'playwright.config.mjs': 'permission-correction/playwright.config.mjs',
}

export const acceptanceMethods = [
  'Active_application_administrator_receives_new_capabilities_and_equipment_edit_without_manual_grants',
  'Trusted_keycloak_administrator_without_a_platform_record_can_edit_equipment',
  'View_only_reader_receives_read_only_metadata_and_cannot_mutate_equipment',
  'Inactive_and_display_name_only_administrators_remain_blocked',
  'Administrator_permissions_do_not_enable_a_disabled_deployment_feature',
]

export const browserCases = [
  'administrator can edit equipment through the published UI',
  'application-administrator can edit equipment through the published UI',
  'view-only reader can inspect equipment while Save remains disabled',
]

export function verifyHarness(files) {
  const actual = Object.fromEntries(Object.entries(files).map(([path, bytes]) => [path, digest(bytes)]))
  assert.deepEqual(actual, harnessSha256, 'The independent permission harness changed.')
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
    if (/\.(?:sql|csproj|props|targets|slnx|json|mjs|lock)$|(^|\/)(?:ai|\.agents|\.claude|\.codex)\/|(?:^|\/)NuGet\.Config$|(?:^|\/)ApplicationRoles\.cs$/i.test(path)) {
      assert.equal(after[path], before[path], `The task must preserve existing migrations, guidance and build inputs: ${path}`)
    }
  }
  const changed = Object.keys(after).filter(path => after[path] !== before[path]).sort()
  assert.ok(changed.length > 0, 'The agent made no application change.')
  for (const path of changed) {
    assert.match(path, /\.(?:cs|ts|vue|md)$/, 'This task does not require new executable/build/dependency configuration.')
    assert.doesNotMatch(path, /(^|\/)(?:ai|\.agents|\.claude|\.codex)\/|(?:^|\/)(?:AGENTS|CLAUDE)\.md$/i,
      'The permission task must not rewrite agent instructions.')
  }
  assert.ok(changed.some(path => /^EquipmentInspection\.Api\/.+\.cs$/.test(path)),
    'The incorrect application administrator registration must be corrected.')
  assert.ok(changed.some(path => /^web\/src\/.+\.(?:ts|vue)$/.test(path)),
    'The frontend must preserve the current user capabilities returned by the API.')
  return { changed, producedTreeSha256: digest(JSON.stringify(Object.entries(after).sort())) }
}

// The runner separately requires source review, isolated dependency resolution
// and a fresh build. Agent-written tests and reports are not acceptance inputs.
export function verifyChange({ trx, before, after, harnessFiles, browserReport }) {
  verifyHarness(harnessFiles)
  const tree = inspectChange(before, after)
  const expected = acceptanceMethods.map(method => `EquipmentInspection.IntegrationTests.${testClass}.${method}`).sort()
  assert.deepEqual(passingTestCases(trx), expected, 'Independent permission test discovery is incomplete.')
  assert.equal(browserTestCounts(browserReport).total, browserCases.length, 'Independent browser discovery differs.')
  assert.equal(browserReport.suites.length, 1, 'Unexpected independent browser suite.')
  const suite = browserReport.suites[0]
  assert.equal(suite.file, 'permissions.spec.mjs', 'Browser report belongs to a different fixture.')
  assert.deepEqual(suite.specs.map(spec => spec.title).sort(), [...browserCases].sort(), 'Browser case identities differ.')
  return {
    ...tree,
    checks: ['active-admin-new-capability', 'trusted-keycloak-admin', 'reader-api-denied',
      'reader-ui-denied', 'inactive-blocked', 'feature-flags-independent'].map(id => ({ id, state: 'passed' })),
  }
}
