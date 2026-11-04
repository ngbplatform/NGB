import assert from 'node:assert/strict'
import { digest, objectKeys, portablePath } from '../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'

function identifiers(values, label) {
  assert.ok(Array.isArray(values) && values.length > 0 && values.length <= 4096, `Invalid ${label} inventory.`)
  assert.equal(new Set(values).size, values.length, `Duplicate ${label}.`)
  for (const value of values) assert.match(value, /^[a-z][a-z0-9.-]{1,127}$/, `Invalid ${label}: ${value}`)
}

function text(value, label) {
  assert.ok(typeof value === 'string' && value.trim().length > 0 && value.length <= 2048, `Invalid ${label}.`)
}

export function verifyCapabilities(registry, snapshots, files, evidence = null) {
  objectKeys(registry, ['schemaVersion', 'version', 'surfaceSha256', 'capabilities', 'classifications'])
  assert.equal(registry.schemaVersion, 1)
  assert.match(registry.version, /^\d+\.\d+\.\d+$/)
  const surface = new Map()
  const packages = new Set()
  for (const snapshot of snapshots) {
    assert.equal(snapshot.version, registry.version, 'Capability and public snapshot versions differ.')
    assert.ok(!packages.has(snapshot.package), 'Duplicate public package inventory.')
    packages.add(snapshot.package)
    if (snapshot.types) {
      for (const type of snapshot.types) {
        const id = `${snapshot.package}:${type.name}`
        assert.ok(!surface.has(id), `Duplicate public type: ${id}`)
        surface.set(id, { sha256: type.contractSha256, members: type.methods })
      }
    } else {
      for (const entry of snapshot.exports) {
        const id = `${snapshot.package}:${entry.subpath}`
        assert.ok(!surface.has(id), `Duplicate public export: ${id}`)
        surface.set(id, { sha256: digest(JSON.stringify(entry)), members: [] })
      }
    }
  }
  assert.ok(surface.size > 0, 'The public artifact inventory is empty.')
  const surfaceSha256 = digest(JSON.stringify([...surface].map(([id, value]) => [id, value.sha256]).sort()))
  assert.equal(registry.surfaceSha256, surfaceSha256, 'Public surface drift requires capability review.')
  assert.ok(Array.isArray(registry.capabilities) && registry.capabilities.length <= 4096)
  identifiers(registry.capabilities.map(item => item.id), 'capability')
  const capabilities = new Map(registry.capabilities.map(item => [item.id, item]))
  const acceptanceIds = new Set()
  for (const capability of registry.capabilities) {
    objectKeys(capability, ['id', 'name', 'category', 'operation', 'support', 'layer', 'constraints',
      'prerequisites', 'contracts', 'skills', 'references', 'validators', 'acceptance'], ['limitation'])
    for (const field of ['name', 'category', 'layer']) text(capability[field], field)
    assert.ok(['create', 'extend', 'configure', 'operate', 'inspect', 'test', 'upgrade'].includes(capability.operation))
    assert.ok(['supported', 'unsupported'].includes(capability.support))
    for (const field of ['constraints', 'prerequisites']) {
      assert.ok(Array.isArray(capability[field]) && capability[field].length > 0)
      capability[field].forEach(value => text(value, field))
    }
    identifiers(capability.validators, 'validator')
    identifiers(capability.skills, 'skill')
    for (const skill of capability.skills) assert.ok(files.has(`skills/${skill}/SKILL.md`), `Missing capability skill: ${skill}`)
    assert.ok(Array.isArray(capability.references) && capability.references.length > 0)
    for (const reference of capability.references) {
      portablePath(reference)
      assert.ok(files.has(reference), `Missing capability reference: ${reference}`)
    }
    assert.ok(Array.isArray(capability.contracts) && capability.contracts.length > 0)
    const contracts = new Set()
    for (const contract of capability.contracts) {
      objectKeys(contract, ['id', 'sha256', 'members'])
      const fact = surface.get(contract.id)
      assert.ok(fact && !contracts.has(contract.id), `Missing or duplicated capability contract: ${contract.id}`)
      contracts.add(contract.id)
      assert.equal(contract.sha256, fact.sha256, `Capability contract changed: ${contract.id}`)
      assert.ok(Array.isArray(contract.members) && new Set(contract.members).size === contract.members.length)
      for (const member of contract.members) assert.ok(fact.members.includes(member), `Missing public member: ${contract.id}.${member}`)
    }
    identifiers(capability.acceptance.map(item => item.id), 'acceptance')
    for (const acceptance of capability.acceptance) {
      objectKeys(acceptance, ['id', 'outcome', 'kind'], ['source', 'test', 'cases'])
      text(acceptance.outcome, 'expected outcome')
      assert.ok(['compile', 'unit', 'integration', 'browser', 'process', 'performance'].includes(acceptance.kind))
      assert.ok(!acceptanceIds.has(acceptance.id), `Ambiguous acceptance ID: ${acceptance.id}`)
      acceptanceIds.add(acceptance.id)
      if (acceptance.source) {
        portablePath(acceptance.source)
        assert.ok(files.has(acceptance.source), `Missing acceptance source: ${acceptance.source}`)
        text(acceptance.test, 'test identifier')
        assert.ok(files.get(acceptance.source).includes(acceptance.test), `Missing acceptance test: ${acceptance.test}`)
        assert.ok(Array.isArray(acceptance.cases) && acceptance.cases.length > 0 && acceptance.cases.length <= 4096,
          'Executable acceptance requires exact discovered test cases.')
        assert.equal(new Set(acceptance.cases).size, acceptance.cases.length, 'Duplicate acceptance case.')
        for (const name of acceptance.cases) {
          text(name, 'test case')
          if (acceptance.kind === 'browser') {
            const identity = JSON.parse(name)
            assert.ok(Array.isArray(identity) && identity.length >= 2 && identity.length <= 34
              && identity.every(part => typeof part === 'string') && identity.at(-1) === acceptance.test
              && JSON.stringify(identity) === name, 'Browser identity must contain its project, describe titles and declared test title.')
          } else {
            assert.ok(name.endsWith(`.${acceptance.test}`) || name.includes(`.${acceptance.test}(`),
              'Acceptance cases must identify the declared test method.')
          }
        }
      } else {
        assert.equal(acceptance.test, undefined, 'An acceptance test needs a source.')
        assert.equal(acceptance.cases, undefined, 'Acceptance cases need a source.')
      }
    }
    if (capability.support === 'unsupported') {
      assert.ok(capability.limitation, 'Unsupported capabilities require a documented limitation.')
      const limitations = JSON.parse(files.get('known-limitations.json')).limitations
      assert.ok(limitations.some(item => item.id === capability.limitation), 'Unknown capability limitation.')
      assert.ok(capability.acceptance.every(item => item.kind !== 'compile'), 'Unsupported behavior requires executable negative acceptance.')
    } else {
      assert.equal(capability.limitation, undefined, 'Supported behavior cannot hide behind a limitation.')
    }
  }
  assert.ok(Array.isArray(registry.classifications) && registry.classifications.length <= 100000)
  const classified = new Set()
  for (const item of registry.classifications) {
    objectKeys(item, ['contract', 'sha256', 'kind', 'reason', 'capabilities'])
    assert.ok(surface.has(item.contract) && !classified.has(item.contract), `Unknown or duplicated classification: ${item.contract}`)
    classified.add(item.contract)
    assert.equal(item.sha256, surface.get(item.contract).sha256, 'Classification is stale.')
    assert.ok(['extension', 'data-contract', 'supporting-contract', 'implementation-visible'].includes(item.kind))
    text(item.reason, 'classification reason')
    assert.ok(Array.isArray(item.capabilities))
    for (const id of item.capabilities) assert.ok(capabilities.has(id), `Unknown classification capability: ${id}`)
    if (item.kind !== 'implementation-visible') assert.ok(item.capabilities.length > 0, 'Supported contracts require capability mapping.')
  }
  const inputsSha256 = digest(JSON.stringify({ registry, files: [...files].map(([path, value]) => [path, digest(value)]).sort() }))
  const outcomes = new Map()
  if (evidence) {
    objectKeys(evidence, ['schemaVersion', 'inputsSha256', 'results'])
    assert.equal(evidence.schemaVersion, 1)
    assert.equal(evidence.inputsSha256, inputsSha256, 'Capability acceptance evidence is stale.')
    assert.ok(Array.isArray(evidence.results))
    for (const result of evidence.results) {
      objectKeys(result, ['id', 'state', 'reportSha256'])
      assert.ok(acceptanceIds.has(result.id) && !outcomes.has(result.id), 'Unknown or duplicated acceptance result.')
      assert.ok(['passed', 'failed', 'not-run'].includes(result.state))
      assert.match(result.reportSha256, /^[a-f0-9]{64}$/)
      outcomes.set(result.id, result.state)
    }
  }
  const results = registry.capabilities.map(capability => {
    const checks = capability.acceptance.map(item => ({ id: item.id, state: item.source ? outcomes.get(item.id) ?? 'not-run' : 'not-run' }))
    return { id: capability.id, support: capability.support,
      state: checks.every(item => item.state === 'passed') ? 'passed' : checks.some(item => item.state === 'failed') ? 'failed' : 'not-run',
      checks }
  })
  const unclassified = [...surface.keys()].filter(id => !classified.has(id)).sort()
  return { schemaVersion: 1, version: registry.version, inputsSha256, surfaceSha256,
    status: unclassified.length === 0 && results.every(item => item.state === 'passed') ? 'passed' : 'not-ready',
    publicContracts: surface.size, classifiedContracts: classified.size, unclassified,
    capabilities: results.length, passedCapabilities: results.filter(item => item.state === 'passed').length, results }
}
