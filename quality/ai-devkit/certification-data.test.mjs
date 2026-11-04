import assert from 'node:assert/strict'
import test from 'node:test'
import { certificationChecks, validateCertification } from './certification-data.mjs'

const hash = 'a'.repeat(64)
const report = profile => ({ schemaVersion: 1, profile, artifactManifestSha256: hash, status: 'passed',
  checks: ['candidate.before', ...certificationChecks[profile], 'candidate.after']
    .map(id => ({ id, state: 'passed', exitCode: 0 })), files: { 'run/check.json': hash } })

test('every Kit release profile requires its exact check inventory and candidate identity', () => {
  for (const profile of Object.keys(certificationChecks)) {
    assert.equal(validateCertification(report(profile), profile, hash).status, 'passed')
  }
  assert.throws(() => validateCertification(report('artifacts'), 'optional', hash))
  assert.throws(() => validateCertification(null, 'artifacts', hash))
  assert.throws(() => validateCertification(report('artifacts'), 'artifacts', 'bad'))
  for (const change of [
    value => { value.schemaVersion = 2 }, value => { value.profile = 'contracts' },
    value => { value.artifactManifestSha256 = 'b'.repeat(64) }, value => { value.status = 'not-ready' },
    value => { value.checks = null }, value => { value.checks.pop() }, value => { value.checks.push(value.checks[0]) },
    value => { value.checks[0].state = 'not-run' }, value => { value.checks[0].exitCode = 1 },
    value => { value.files = null }, value => { value.files = [] }, value => { value.files = {} },
    value => { value.files['run/check.json'] = 'bad' }, value => { value.files['../outside'] = hash },
  ]) {
    const value = report('artifacts')
    change(value)
    assert.throws(() => validateCertification(value, 'artifacts', hash))
  }
})
