import assert from 'node:assert/strict'
import { digest, portablePath } from '../../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { passingTestCases } from '../example-evidence.mjs'

// These sources run in a sibling harness outside the evaluated agent's writable tree.
// Changing a harness source requires a new oracle identity before inference.
export const harnessSha256 = {
  'AgentAcceptance.csproj': 'c0b7eddbdcb56c5eb351956338f8c1259e71cdf7a54c403ad31cbde2e4a5a04b',
  'Controllers.cs': '81ce3d09eeae2a1db236373792ce45afd49e43e8aa02aa80b4ea26ed977cf7b9',
  'DocumentChangeAcceptance.cs': '640d5a170323af4bea1c52afc4819a18051031af352ccc58fbe9a153e9f598b4',
  'InspectionHost.cs': '5883a1caaf1fecaa7a1997f8eeed6cde376241d1afc1d23e3cc77579170e95f4',
  'InspectionPermissionSource.cs': '0f1355c5f82d4d9f549ec47cb54a170ba26e98864a6f63ac1d49d088b453f115',
  'InspectionRequests.cs': '6d29f0826cdb6be7b7c87fb7732ef078e93038cd124d81cb07b38764cbcd1d59',
  'TestIdentity.cs': '1ea714b3727d846b9a455ab1f0a49eb1931b8bda6fde54cac2b696173fa76466',
  'xunit.runner.json': 'ebeeae03ee3082c11aeb2729d9d0c4bd489a1c86a2a45078c63016c3da3bb66c',
}

export const acceptanceMethods = {
  'draft-preserved': 'Draft_preserves_missing_empty_and_whitespace_completion_notes',
  'post-validation': 'Post_rejects_missing_empty_and_whitespace_completion_notes_without_changing_the_draft',
  'stale-write-refused': 'Stale_action_version_is_rejected_after_posting_with_a_completion_note',
  'missing-handler-refused': 'Missing_posting_handler_remains_refused_even_with_a_valid_note',
  'empty-handler-supported': 'Valid_note_round_trips_and_posts_with_the_existing_empty_handler_without_effects',
}

export const additionalMethods = [
  'Completion_note_does_not_replace_existing_equipment_and_result_requirements',
  'Completion_note_length_limit_rejects_create_and_update_without_truncating_valid_text',
]

export const testClass = 'DocumentChangeAcceptance'
export const harnessSources = Object.fromEntries(Object.keys(harnessSha256).map(path => [path, `document-change/${path}`]))

export function verifyDocumentHarness(files) {
  const actual = Object.fromEntries(Object.entries(files).map(([path, bytes]) => [path, digest(bytes)]))
  assert.deepEqual(actual, harnessSha256, 'The independent document-change harness changed.')
}

export function inspectDocumentChange(before, after) {
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
    assert.ok(/\.(?:cs|sql|md)$/.test(path) || /^web\/tests\/(?:unit|e2e)\/.+\.ts$/.test(path),
      'This task permits application C#/SQL, frontend tests and documentation, preserving build/dependency configuration.')
    assert.doesNotMatch(path, /(^|\/)(?:ai|\.agents|\.claude|\.codex)\/|(?:^|\/)(?:AGENTS|CLAUDE)\.md$/i,
      'The document task must not rewrite agent instructions.')
  }
  assert.ok(changed.some(path => /^EquipmentInspection\.PostgreSql\/db\/migrations\/V[^/]+\.sql$/.test(path)),
    'A persisted document-field change needs a new immutable application migration.')
  return { changed, producedTreeSha256: digest(JSON.stringify(Object.entries(after).sort())) }
}

// The trusted runner must review changed source before building it, restore only
// the supplied candidate feed, and collect this TRX from the frozen sibling harness.
// An agent-written report, test project or self-assessed answer is never an input.
export function verifyDocumentChange({ trx, before, after, harnessFiles }) {
  verifyDocumentHarness(harnessFiles)
  const tree = inspectDocumentChange(before, after)
  const expected = [...Object.values(acceptanceMethods), ...additionalMethods].map(method =>
    `EquipmentInspection.IntegrationTests.DocumentChangeAcceptance.${method}`).sort()
  assert.deepEqual(passingTestCases(trx), expected, 'Independent document-change test discovery is incomplete.')
  return {
    ...tree,
    checks: [...Object.keys(acceptanceMethods), 'no-sdk-business-changes'].map(id => ({ id, state: 'passed' })),
  }
}

export { inspectDocumentChange as inspectChange, verifyDocumentChange as verifyChange, verifyDocumentHarness as verifyHarness }
