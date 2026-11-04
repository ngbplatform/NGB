import assert from 'node:assert/strict'
import { distinctPaths } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'

export const certificationChecks = {
  artifacts: ['package-parity', 'public-contracts'],
  contracts: ['tooling'],
  application: ['starter', 'examples', 'capabilities'],
  upgrade: ['source-fixture', 'coordinated-upgrade', 'failure-recovery'],
  agents: ['evaluations'],
}

export function validateCertification(value, profile, artifactManifestSha256) {
  assert.ok(Object.hasOwn(certificationChecks, profile), 'Unknown Kit certification profile.')
  assert.ok(value && value.schemaVersion === 1 && value.profile === profile, 'Invalid Kit certification report.')
  assert.match(artifactManifestSha256, /^[a-f0-9]{64}$/)
  assert.equal(value.artifactManifestSha256, artifactManifestSha256, 'Kit evidence belongs to another release candidate.')
  assert.equal(value.status, 'passed', 'Kit certification has unfinished or failed checks.')
  assert.ok(Array.isArray(value.checks), 'Missing Kit check inventory.')
  const expected = ['candidate.before', ...certificationChecks[profile], 'candidate.after']
  assert.deepEqual(value.checks.map(check => check.id).sort(), expected.sort(), 'Kit check discovery differs from the mandatory profile.')
  for (const check of value.checks) {
    assert.equal(check.state, 'passed', `Kit check did not pass: ${check.id}`)
    assert.equal(check.exitCode, 0, `Kit check did not exit successfully: ${check.id}`)
  }
  assert.ok(value.files && typeof value.files === 'object' && !Array.isArray(value.files), 'Missing Kit evidence files.')
  const paths = Object.keys(value.files)
  assert.ok(paths.length > 0 && paths.length <= 8192, 'Invalid Kit evidence inventory size.')
  distinctPaths(paths)
  for (const hash of Object.values(value.files)) assert.match(hash, /^[a-f0-9]{64}$/, 'Invalid Kit evidence file identity.')
  return value
}
