import assert from 'node:assert/strict'
import { objectKeys } from './kit-contracts.mjs'

export const resourceOwnerLabel = 'com.ngb.devkit.run'
export const resourceApplicationLabel = 'com.ngb.devkit.application'

export function validateResourceOwner(value, { runId, applicationSha256, host }) {
  objectKeys(value, ['schemaVersion', 'runId', 'applicationSha256', 'host', 'ownerPid', 'provisioning', 'provisionerPid'])
  assert.equal(value.schemaVersion, 1, 'Unsupported resource ownership schema.')
  assert.match(value.runId, /^[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/)
  assert.match(value.applicationSha256, /^[a-f0-9]{64}$/)
  assert.equal(value.runId, runId, 'Resource receipt belongs to another run.')
  assert.equal(value.applicationSha256, applicationSha256, 'Resource receipt belongs to another application path.')
  assert.equal(value.host, host, 'Resource recovery must run on the original host.')
  assert.ok(Number.isSafeInteger(value.ownerPid) && value.ownerPid > 0, 'Invalid resource owner process.')
  assert.equal(typeof value.provisioning, 'boolean', 'Invalid provisioning state.')
  assert.ok(value.provisionerPid === null || Number.isSafeInteger(value.provisionerPid) && value.provisionerPid > 0,
    'Invalid provisioner process.')
  assert.ok(value.provisioning || value.provisionerPid === null, 'Inconsistent provisioner state.')
  return value
}

export function resourceIdentity(kind, value, owner) {
  assert.ok(['container', 'network', 'volume'].includes(kind), 'Unsupported test resource kind.')
  assert.ok(Array.isArray(value) && value.length === 1, 'Expected one inspected test resource.')
  const resource = value[0]
  const labels = kind === 'container' ? resource.Config?.Labels : resource.Labels
  assert.ok(labels?.[resourceOwnerLabel] === owner.runId
    && labels?.[resourceApplicationLabel] === owner.applicationSha256
    && labels?.['com.docker.compose.project'] === `ngb-kit-${owner.runId}`,
  'Refusing to remove a resource without matching run, application and Compose ownership.')
  const id = kind === 'volume' ? resource.Name : resource.Id
  if (kind === 'volume') assert.match(id, /^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,255}$/)
  else assert.match(id, /^[a-f0-9]{64}$/)
  return { kind, id }
}
