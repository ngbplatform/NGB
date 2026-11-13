import assert from 'node:assert/strict'
import { assertArtifactInventory, digest, includesAiKit } from './release-contracts.mjs'

export const buildScope = 'github-package-build'
export const buildWorkflow = '.github/workflows/platform-packages.yml'

export function packagePaths(inventory) {
  const paths = inventory.packages.flatMap(item => [
    `nuget-release/${item.id}.${inventory.target}.nupkg`,
    ...(item.kind === 'runtime' ? [`nuget-release/${item.id}.${inventory.target}.snupkg`] : []),
  ])
  paths.push(`npm/ngbplatform-ui-${inventory.target}.tgz`)
  if (includesAiKit(inventory.target)) paths.push(`npm/ngbplatform-cli-${inventory.target}.tgz`)
  return paths
}

export function buildProvenance(env, commit) {
  assert.equal(env.GITHUB_ACTIONS, 'true', 'Release packages must originate in GitHub Actions.')
  assert.equal(env.GITHUB_REF, 'refs/heads/main', 'Release packages must be built on main.')
  assert.ok(['push', 'workflow_dispatch'].includes(env.GITHUB_EVENT_NAME), 'PR packages cannot authorize publication.')
  assert.equal(env.GITHUB_SHA, commit, 'Build checkout differs from its event commit.')
  assert.equal(env.GITHUB_WORKFLOW_REF, `${env.GITHUB_REPOSITORY}/${buildWorkflow}@refs/heads/main`)
  assert.match(env.GITHUB_REPOSITORY, /^[\w.-]+\/[\w.-]+$/)
  assert.match(env.GITHUB_RUN_ID, /^[1-9]\d*$/)
  assert.match(env.GITHUB_RUN_ATTEMPT, /^[1-9]\d*$/)
  return {
    repository: env.GITHUB_REPOSITORY, workflow: buildWorkflow,
    runId: env.GITHUB_RUN_ID, runAttempt: env.GITHUB_RUN_ATTEMPT,
  }
}

export function assertBuildManifest(manifest, inventory, expected) {
  assertArtifactInventory(manifest, inventory)
  assert.equal(manifest.scope, buildScope, 'Expected a GitHub package build, not local certification evidence.')
  assert.match(manifest.sourceCommit, /^[a-f0-9]{40}$/)
  assert.match(manifest.sourceGitTree, /^[a-f0-9]{40}$/)
  assert.equal(manifest.sourceCommit, expected.commit, 'Build packages belong to another commit.')
  assert.equal(manifest.sourceGitTree, expected.tree, 'Build source tree differs from the checkout.')
  assert.equal(manifest.build.repository, expected.repository, 'Build packages belong to another repository.')
  assert.equal(manifest.build.workflow, buildWorkflow)
  assert.match(manifest.build.runId, /^[1-9]\d*$/)
  assert.match(manifest.build.runAttempt, /^[1-9]\d*$/)
  if (expected.runId !== undefined) assert.equal(manifest.build.runId, expected.runId, 'Build run differs.')
  if (expected.runAttempt !== undefined) assert.equal(manifest.build.runAttempt, expected.runAttempt, 'Build attempt differs.')
}

export function publicationOptions(args) {
  const ciBuild = args.includes('--ci-build')
  const paths = args.filter(value => value !== '--ci-build')
  assert.ok(paths.length <= 1 && paths.every(value => !value.startsWith('-')),
    'Expected an optional package directory and --ci-build.')
  assert.equal(args.filter(value => value === '--ci-build').length, ciBuild ? 1 : 0)
  return {
    ciBuild, path: paths[0],
    verifier: ciBuild ? 'quality/external-consumer/build-release.mjs' : 'quality/external-consumer/release.mjs',
    command: ciBuild ? 'verify' : 'verify-candidate',
  }
}

export function assertBuildPublication(manifestBytes, receipt, evidence, identitiesBytes, stage) {
  const manifest = JSON.parse(manifestBytes)
  const identities = JSON.parse(identitiesBytes)
  assert.ok(['publication', 'promotion'].includes(stage))
  assert.equal(receipt.schemaVersion, 1)
  assert.equal(receipt.scope, buildScope)
  assert.equal(receipt.artifactManifestSha256, digest(manifestBytes))
  assert.equal(receipt.status, 'uploaded-awaiting-registry-verification')
  assert.deepEqual(receipt.artifacts, manifest.artifacts.map(({ path, sha256 }) => ({ path, sha256 })))
  assert.equal(evidence.schemaVersion, 1)
  assert.equal(evidence.scope, buildScope)
  assert.equal(evidence.stage, stage)
  assert.equal(evidence.artifactManifestSha256, digest(manifestBytes))
  const gate = stage === 'publication' ? 'publish-identity' : 'registry-verification'
  assert.deepEqual(evidence.results, {
    [gate]: { status: 'passed', path: 'certification/registry-identities.json', evidenceSha256: digest(identitiesBytes) },
  })
  assert.equal(identities.schemaVersion, 1)
  assert.equal(identities.scope, buildScope)
  assert.equal(identities.status, 'passed')
  const artifacts = manifest.artifacts.filter(item => !item.path.endsWith('.snupkg'))
  assert.deepEqual(identities.results.map(item => item.path).sort(), artifacts.map(item => item.path).sort())
  for (const artifact of artifacts) {
    const result = identities.results.find(item => item.path === artifact.path)
    assert.equal(result.status, 'passed')
    assert.match(result.registrySha256, /^[a-f0-9]{64}$/)
    if (artifact.path.endsWith('.tgz')) {
      assert.equal(result.registrySha256, artifact.sha256)
      assert.equal(result.integrity, artifact.integrity)
    } else {
      assert.equal(result.payloadSha256, artifact.payloadSha256)
    }
  }
}
