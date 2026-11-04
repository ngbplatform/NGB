import assert from 'node:assert/strict'
import test from 'node:test'
import { argumentsFor } from '../../packaging/templates/content/infrastructure/ngb/contracts.mjs'

test('inspection commands cannot acquire execution or mutation flags', () => {
  for (const command of ['doctor', 'validate']) {
    assert.equal(argumentsFor([command, '--format', 'json']).format, 'json')
    assert.throws(() => argumentsFor([command, '--apply']))
  }
  assert.equal(argumentsFor(['ai', 'status', '--format', 'text']).name, 'status')
  for (const args of [['ai', 'unknown'], ['ai', 'status', '--apply'], ['ai', 'status', '--to', '3.4.0'],
    ['ai', 'status', '--resolutions', 'review.json'], ['ai', 'sync', '--to', 'latest'],
    ['validate', '--format', 'yaml'], ['validate', '--profile', 'unit'],
    ['test', '--profile', 'production'], ['certify'], ['ai', 'sync', '--resolutions', 'review.json']]) {
    assert.throws(() => argumentsFor(args))
  }
})

test('execution profiles and reviewed sync choices require explicit supported syntax', () => {
  assert.equal(argumentsFor(['validate', '--profile', 'build']).profile, 'build')
  for (const profile of ['unit', 'integration', 'e2e', 'all']) {
    assert.equal(argumentsFor(['test', '--profile', profile]).profile, profile)
  }
  assert.equal(argumentsFor(['certify', '--profile', 'external-application']).profile, 'external-application')
  assert.equal(argumentsFor(['recover', '--resources', '--apply']).resources, true)
  assert.throws(() => argumentsFor(['test', '--resources']))
  assert.equal(argumentsFor(['ai', 'sync']).to, undefined)
  const sync = argumentsFor(['ai', 'sync', '--to', '3.4.0', '--apply', '--resolutions', 'reviewed plan.json'])
  assert.equal(sync.to, '3.4.0')
  assert.equal(sync.resolutions, 'reviewed plan.json')
})
