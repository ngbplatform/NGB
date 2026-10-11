import assert from 'node:assert/strict'
import test from 'node:test'
import { assertLayering } from './layering.mjs'

const project = (layer, projects = [], packages = []) => ({ name: `Example.${layer}`, projects, packages })

test('definition, orchestration and provider layers compose only at hosts', () => {
  assertLayering([
    project('Definitions', [], ['NGB.Platform.Definitions', 'NGB.Platform.Metadata']),
    project('Runtime', ['Example.Definitions'], ['NGB.Platform.Runtime', 'NGB.Platform.Persistence']),
    project('PostgreSql', ['Example.Definitions'], ['NGB.Platform.PostgreSql', 'Dapper', 'Npgsql']),
    project('Api', ['Example.Runtime', 'Example.PostgreSql'], ['NGB.Platform.Api']),
  ])
})

test('rejects layer inversion and hosting or provider leakage', () => {
  for (const [layer, dependency] of [
    ['Definitions', 'Runtime'], ['Definitions', 'PostgreSql'],
    ['Runtime', 'PostgreSql'], ['PostgreSql', 'Runtime'], ['Runtime', 'Api'],
  ]) {
    assert.throws(() => assertLayering([project(layer, [`Example.${dependency}`])]), /must not reference/)
  }
  for (const layer of ['Definitions', 'Runtime']) {
    for (const provider of ['NGB.Platform.PostgreSql', 'Dapper', 'Npgsql', 'Microsoft.EntityFrameworkCore']) {
      assert.throws(() => assertLayering([project(layer, [], [provider])]), /provider package/)
    }
  }
  for (const layer of ['Definitions', 'Runtime', 'PostgreSql']) {
    assert.throws(() => assertLayering([project(layer, [], ['NGB.Platform.Hosting.AspNetCore'])]), /hosting package/)
  }
  for (const layer of ['Definitions', 'PostgreSql']) {
    assert.throws(() => assertLayering([project(layer, [], ['NGB.Platform.Runtime'])]), /runtime package/)
    assert.throws(() => assertLayering([project(layer, [], ['NGB.Platform.BackgroundJobs'])]), /runtime package/)
  }
})
