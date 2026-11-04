import assert from 'node:assert/strict'
import test from 'node:test'
import { resourceApplicationLabel, resourceIdentity, resourceOwnerLabel, validateResourceOwner } from '../../packaging/templates/content/infrastructure/ngb/resource-data.mjs'

const identity = { runId: '7bca10f4-3b9e-4c4d-8b22-d67d645b2e78', applicationSha256: 'a'.repeat(64), host: 'test-host' }
const owner = () => ({ schemaVersion: 1, ...identity, ownerPid: 123, provisioning: false, provisionerPid: null })

test('resource receipts require exact application, run, host and consistent process ownership', () => {
  assert.deepEqual(validateResourceOwner(owner(), identity), owner())
  assert.equal(validateResourceOwner({ ...owner(), provisioning: true, provisionerPid: 234 }, identity).provisionerPid, 234)
  for (const replacement of [
    { schemaVersion: 2 }, { runId: '../other' }, { runId: '8bca10f4-3b9e-4c4d-8b22-d67d645b2e78' },
    { applicationSha256: 'bad' }, { applicationSha256: 'b'.repeat(64) }, { host: 'other-host' },
    { ownerPid: 0 }, { ownerPid: '123' }, { provisionerPid: 0 }, { provisionerPid: '234' },
    { provisioning: 'false' }, { provisioning: false, provisionerPid: 234 }, { unknown: true },
  ]) {
    assert.throws(() => validateResourceOwner({ ...owner(), ...replacement }, identity))
  }
})

test('every removable resource needs matching run, application and Compose labels', () => {
  const labels = { [resourceOwnerLabel]: identity.runId, [resourceApplicationLabel]: identity.applicationSha256,
    'com.docker.compose.project': `ngb-kit-${identity.runId}` }
  for (const kind of ['container', 'network', 'volume']) {
    const data = { Id: 'b'.repeat(64), Name: 'ngb-test-volume', Labels: labels, Config: { Labels: labels } }
    assert.equal(resourceIdentity(kind, [data], owner()).id, kind === 'volume' ? data.Name : data.Id)
    assert.throws(() => resourceIdentity(kind, [], owner()))
    assert.throws(() => resourceIdentity(kind, null, owner()))
    assert.throws(() => resourceIdentity(kind, [{ ...data, Id: 'bad', Name: '../production' }], owner()))
    for (const key of Object.keys(labels)) {
      const other = { ...labels, [key]: 'another-run' }
      assert.throws(() => resourceIdentity(kind, [{ ...data, Labels: other, Config: { Labels: other } }], owner()), /Refusing/)
    }
    assert.throws(() => resourceIdentity(kind, [{}], owner()), /Refusing/)
  }
  assert.throws(() => resourceIdentity('image', [], owner()), /Unsupported/)
})
