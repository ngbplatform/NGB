import assert from 'node:assert/strict'
import test from 'node:test'
import { digest } from './release-contracts.mjs'
import {
  assertBuildManifest, assertBuildPublication, buildProvenance, buildScope, buildWorkflow,
  packagePaths, publicationOptions,
} from './build-release-contracts.mjs'

const commit = 'a'.repeat(40)
const tree = 'b'.repeat(40)
const inventory = {
  target: '3.4.0', packages: [{ id: 'NGB.Platform.Tools', kind: 'runtime' }, { id: 'NGB.Platform.Templates', kind: 'template' }],
}

function fixture() {
  const env = {
    GITHUB_ACTIONS: 'true', GITHUB_REF: 'refs/heads/main', GITHUB_EVENT_NAME: 'push',
    GITHUB_SHA: commit, GITHUB_WORKFLOW_REF: `owner/NGB/${buildWorkflow}@refs/heads/main`,
    GITHUB_REPOSITORY: 'owner/NGB', GITHUB_RUN_ID: '42', GITHUB_RUN_ATTEMPT: '2',
  }
  const expected = { commit, tree, repository: 'owner/NGB', runId: '42', runAttempt: '2' }
  const manifest = {
    schemaVersion: 1, scope: buildScope, version: inventory.target,
    sourceCommit: commit, sourceGitTree: tree, build: buildProvenance(env, commit),
    artifacts: packagePaths(inventory).map(path => ({ path, sha256: 'c'.repeat(64),
      ...(path.endsWith('.tgz') ? { integrity: 'sha512-fixture' } : { payloadSha256: 'd'.repeat(64) }) })),
  }
  return { env, expected, manifest }
}

test('package builds carry a complete runtime, symbols, template, UI and CLI inventory', () => {
  const { manifest, expected } = fixture()
  assertBuildManifest(manifest, inventory, expected)
  assertBuildManifest(manifest, inventory, { commit, tree, repository: expected.repository })
  assert.equal(manifest.artifacts.length, 5)
  assert.equal(packagePaths({ ...inventory, target: '3.3.0' }).length, 4)
  assert.equal(buildProvenance({ ...fixture().env, GITHUB_EVENT_NAME: 'workflow_dispatch' }, commit).runAttempt, '2')
})

test('local execution, PRs and mismatched build provenance cannot authorize publication', () => {
  for (const [key, value] of Object.entries({
    GITHUB_ACTIONS: 'false', GITHUB_REF: 'refs/heads/feature', GITHUB_EVENT_NAME: 'pull_request',
    GITHUB_SHA: tree, GITHUB_WORKFLOW_REF: 'other', GITHUB_REPOSITORY: 'invalid',
    GITHUB_RUN_ID: '0', GITHUB_RUN_ATTEMPT: '-1',
  })) {
    assert.throws(() => buildProvenance({ ...fixture().env, [key]: value }, commit))
  }
  const invalid = fixture().env
  invalid.GITHUB_REPOSITORY = 'invalid'
  invalid.GITHUB_WORKFLOW_REF = `invalid/${buildWorkflow}@refs/heads/main`
  assert.throws(() => buildProvenance(invalid, commit))
  for (const change of [
    item => { item.manifest.scope = 'local-certification' },
    item => { item.manifest.artifacts.pop() },
    item => { item.manifest.sourceCommit = tree },
    item => { item.manifest.sourceCommit = 'invalid' },
    item => { item.manifest.sourceGitTree = commit },
    item => { item.manifest.build.repository = 'fork/NGB' },
    item => { item.manifest.build.workflow = 'other' },
    item => { item.manifest.build.runId = '41' },
    item => { item.manifest.build.runAttempt = '1' },
    item => { item.manifest.build.runId = '0' },
    item => { item.manifest.build.runAttempt = '0' },
  ]) {
    const item = fixture()
    change(item)
    assert.throws(() => assertBuildManifest(item.manifest, inventory, item.expected))
  }
})

test('publication requires explicit CI mode and preserves local candidate certification by default', () => {
  assert.deepEqual(publicationOptions([]), {
    ciBuild: false, path: undefined, verifier: 'quality/external-consumer/release.mjs', command: 'verify-candidate',
  })
  assert.equal(publicationOptions(['candidate']).path, 'candidate')
  for (const args of [['--ci-build', 'build'], ['build', '--ci-build']]) {
    assert.deepEqual(publicationOptions(args), {
      ciBuild: true, path: 'build', verifier: 'quality/external-consumer/build-release.mjs', command: 'verify',
    })
  }
  for (const args of [['--unknown'], ['one', 'two'], ['--ci-build', '--ci-build']]) {
    assert.throws(() => publicationOptions(args))
  }
})

function publicationFixture(stage) {
  const { manifest } = fixture()
  const manifestBytes = Buffer.from(JSON.stringify(manifest))
  const identities = {
    schemaVersion: 1, scope: buildScope, status: 'passed',
    results: manifest.artifacts.filter(item => !item.path.endsWith('.snupkg')).map(item => ({
      path: item.path, status: 'passed', registrySha256: item.sha256,
      integrity: item.integrity, payloadSha256: item.payloadSha256,
    })),
  }
  const identitiesBytes = Buffer.from(JSON.stringify(identities))
  const receipt = {
    schemaVersion: 1, scope: buildScope, artifactManifestSha256: digest(manifestBytes),
    status: 'uploaded-awaiting-registry-verification',
    artifacts: manifest.artifacts.map(({ path, sha256 }) => ({ path, sha256 })),
  }
  const evidence = {
    schemaVersion: 1, scope: buildScope, stage, artifactManifestSha256: digest(manifestBytes),
    results: { [stage === 'publication' ? 'publish-identity' : 'registry-verification']: {
      status: 'passed', path: 'certification/registry-identities.json', evidenceSha256: digest(identitiesBytes),
    } },
  }
  return { manifestBytes, identities, identitiesBytes, receipt, evidence }
}

test('registry receipts bind every published package and both stages to the built archives', () => {
  for (const stage of ['publication', 'promotion']) {
    const value = publicationFixture(stage)
    assertBuildPublication(value.manifestBytes, value.receipt, value.evidence, value.identitiesBytes, stage)
  }
})

test('missing, mismatched and failed registry evidence cannot promote packages', () => {
  for (const change of [
    item => { item.receipt.scope = 'local' },
    item => { item.receipt.artifactManifestSha256 = 'other' },
    item => { item.receipt.artifacts.pop() },
    item => { item.receipt.status = 'failed' },
    item => { item.evidence.scope = 'local' },
    item => { item.evidence.artifactManifestSha256 = 'other' },
    item => { item.evidence.stage = 'promotion' },
    item => { item.evidence.results['publish-identity'].evidenceSha256 = 'other' },
    item => { item.identities.scope = 'local' },
    item => { item.identities.status = 'failed' },
    item => { item.identities.results.pop() },
    item => { item.identities.results[0].status = 'failed' },
    item => { item.identities.results[0].registrySha256 = 'invalid' },
    item => { item.identities.results[0].payloadSha256 = 'other' },
    item => { item.identities.results.at(-1).registrySha256 = 'e'.repeat(64) },
    item => { item.identities.results.at(-1).integrity = 'other' },
  ]) {
    const value = publicationFixture('publication')
    change(value)
    const bytes = Buffer.from(JSON.stringify(value.identities))
    // Keep the hash consistent when testing the contents, not the binding itself.
    if (!bytes.equals(value.identitiesBytes)) value.evidence.results['publish-identity'].evidenceSha256 = digest(bytes)
    assert.throws(() => assertBuildPublication(value.manifestBytes, value.receipt, value.evidence, bytes, 'publication'))
  }
})
