import assert from 'node:assert/strict'
import test from 'node:test'
import { loadSummary } from '../../packaging/templates/content/infrastructure/ngb/performance.mjs'

const identity = { runId: 'test-resource-run', repetition: 1, profileSha256: 'a'.repeat(64) }

function summary() {
  const metrics = {}
  for (const endpoint of ['roles', 'menu']) {
    const tag = `{endpoint:${endpoint}}`
    metrics[`ngb_requests${tag}`] = { values: { count: 200 }, thresholds: { 'count==200': { ok: true } } }
    metrics[`ngb_failures${tag}`] = { values: { rate: 0 }, thresholds: { 'rate==0': { ok: true } } }
    metrics[`ngb_latency${tag}`] = {
      values: { min: 1, avg: 20, max: 200, 'p(95)': 90, 'p(99)': 150 },
      thresholds: { 'p(95)<500': { ok: true }, 'p(99)<1000': { ok: true } },
    }
  }
  return { schemaVersion: 1, profileId: 'starter-http-v1', ...identity, data: { metrics } }
}

test('load evidence binds each endpoint and repetition to the frozen workload and resource run', () => {
  const evidence = loadSummary(summary(), identity)
  assert.equal(evidence.endpoints.length, 2)
  assert.equal(evidence.endpoints[0].requests, 200)
  assert.equal(evidence.endpoints[0].latencyMilliseconds['p(95)'], 90)
  assert.throws(() => loadSummary(null, identity))
  for (const change of [
    value => { value.schemaVersion = 2 }, value => { value.profileId = 'other' },
    value => { value.profileSha256 = 'b'.repeat(64) }, value => { value.runId = 'stale-run' },
    value => { value.repetition = 2 }, value => { delete value.data },
    value => { value.data.metrics = null }, value => { value.data.metrics = 'wrong' },
    value => { delete value.data.metrics['ngb_requests{endpoint:menu}'] },
    value => { value.data.metrics['ngb_requests{endpoint:menu}'].values = null },
    value => { value.data.metrics['ngb_requests{endpoint:menu}'].thresholds = null },
    value => { value.data.metrics['ngb_requests{endpoint:menu}'].thresholds = { 'count>0': { ok: true } } },
    value => { value.data.metrics['ngb_requests{endpoint:menu}'].thresholds['count==200'].ok = false },
    value => { value.data.metrics['ngb_requests{endpoint:menu}'].thresholds['count==200'] = null },
    value => { value.data.metrics['ngb_requests{endpoint:menu}'].values.count = 199 },
    value => { value.data.metrics['ngb_failures{endpoint:menu}'].values.rate = 0.01 },
  ]) {
    const value = summary()
    change(value)
    assert.throws(() => loadSummary(value, identity))
  }
  for (const repetition of [0, 4, 1.5, '1']) {
    assert.throws(() => loadSummary(summary(), { ...identity, repetition }), /repetition/)
  }
})

test('claimed threshold success cannot mask inconsistent, invalid or over-budget latency', () => {
  for (const replacement of [
    { min: -1 }, { min: Infinity }, { max: '200' }, { avg: null }, { 'p(95)': NaN },
    { min: 30 }, { avg: 201 }, { min: 100, avg: 120 }, { 'p(95)': 160 }, { 'p(99)': 201 },
    { max: 2000, 'p(95)': 500, 'p(99)': 600 }, { max: 2000, 'p(99)': 1000 },
  ]) {
    const value = summary()
    Object.assign(value.data.metrics['ngb_latency{endpoint:roles}'].values, replacement)
    assert.throws(() => loadSummary(value, identity))
  }
})
