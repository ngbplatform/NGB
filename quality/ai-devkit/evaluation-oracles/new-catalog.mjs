import assert from 'node:assert/strict'
import { digest, portablePath } from '../../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { passingTestCases } from '../example-evidence.mjs'

// Shared host sources are frozen independently from all agent-writable files.
export const harnessSha256 = {
  'AgentAcceptance.csproj': '278434490d506d1be70eab74a6944d4a1614ea51661e00bc8ca5140e4be42c6d',
  'Controllers.cs': '81ce3d09eeae2a1db236373792ce45afd49e43e8aa02aa80b4ea26ed977cf7b9',
  'CatalogAcceptance.cs': '85f0dc8374bb9e1516d416ebdde3d5e16e1b246e86b86e9818080d74daf56c76',
  'InspectionHost.cs': '5883a1caaf1fecaa7a1997f8eeed6cde376241d1afc1d23e3cc77579170e95f4',
  'InspectionPermissionSource.cs': '0f1355c5f82d4d9f549ec47cb54a170ba26e98864a6f63ac1d49d088b453f115',
  'InspectionRequests.cs': '6d29f0826cdb6be7b7c87fb7732ef078e93038cd124d81cb07b38764cbcd1d59',
  'TestIdentity.cs': '1ea714b3727d846b9a455ab1f0a49eb1931b8bda6fde54cac2b696173fa76466',
  'xunit.runner.json': 'ebeeae03ee3082c11aeb2729d9d0c4bd489a1c86a2a45078c63016c3da3bb66c',
}

export const testClass = 'CatalogAcceptance'
export const harnessSources = Object.fromEntries(Object.keys(harnessSha256).map(path =>
  [path, `${path === 'CatalogAcceptance.cs' ? 'new-catalog' : 'document-change'}/${path}`]))
export const acceptanceMethods = [
  'Both_administrator_kinds_create_read_and_update_the_new_catalog_without_manual_grants',
  'Required_fields_and_length_limits_reject_invalid_create_and_partial_update',
  'Duplicate_code_is_refused_on_create_and_update_without_changing_existing_records',
  'Concurrent_creates_cannot_commit_the_same_code_twice',
  'Reader_inactive_and_display_name_only_users_cannot_read_or_mutate_the_catalog',
  'Repeated_public_migration_execution_preserves_saved_category_and_uniqueness',
]

export function verifyHarness(files) {
  const actual = Object.fromEntries(Object.entries(files).map(([path, bytes]) => [path, digest(bytes)]))
  assert.deepEqual(actual, harnessSha256, 'The independent catalog harness changed.')
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
      'The catalog task must not rewrite agent instructions.')
  }
  assert.ok(changed.some(path => /^EquipmentInspection\.Definitions\/.+\.cs$/.test(path)),
    'Catalog metadata belongs in the application Definitions project.')
  assert.ok(changed.some(path => /^EquipmentInspection\.PostgreSql\/db\/migrations\/V[^/]+\.sql$/.test(path)),
    'The new catalog needs an immutable application migration in the provider project.')
  return { changed, producedTreeSha256: digest(JSON.stringify(Object.entries(after).sort())) }
}

// The runner separately requires source review, isolated dependency resolution
// and a fresh build. Agent-written tests and reports are not acceptance inputs.
export function verifyChange({ trx, before, after, harnessFiles }) {
  verifyHarness(harnessFiles)
  const tree = inspectChange(before, after)
  const expected = acceptanceMethods.map(method => `EquipmentInspection.IntegrationTests.${testClass}.${method}`).sort()
  assert.deepEqual(passingTestCases(trx), expected, 'Independent catalog test discovery is incomplete.')
  return {
    ...tree,
    checks: ['public-sdk-only', 'layering', 'real-create-read-update', 'duplicate-code-refused',
      'unauthorized-refused', 'migration-idempotent'].map(id => ({ id, state: 'passed' })),
  }
}
