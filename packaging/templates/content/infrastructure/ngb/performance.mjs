import assert from 'node:assert/strict'
import profile from './load-profile.json' with { type: 'json' }

export function loadSummary(value, { runId, repetition, profileSha256 }) {
  assert.ok(value && value.schemaVersion === 1 && value.profileId === profile.id, 'Invalid load profile report.')
  assert.equal(value.profileSha256, profileSha256, 'Load evidence belongs to another workload revision.')
  assert.equal(value.runId, runId, 'Load evidence belongs to another resource run.')
  assert.ok(Number.isSafeInteger(repetition) && repetition >= 1 && repetition <= profile.repetitions,
    'Invalid load repetition.')
  assert.equal(value.repetition, repetition, 'Load evidence belongs to another repetition.')
  assert.ok(value.data?.metrics && typeof value.data.metrics === 'object', 'Missing load measurements.')
  const iterations = profile.virtualUsers * profile.iterationsPerUser
  const endpoints = []
  for (const endpoint of profile.endpoints) {
    const tag = `{endpoint:${endpoint.name}}`
    const read = (metric, expected) => {
      const entry = value.data.metrics[`${metric}${tag}`]
      assert.ok(entry?.values && entry.thresholds, `Missing endpoint measurements: ${endpoint.name}/${metric}`)
      assert.deepEqual(Object.keys(entry.thresholds).sort(), [...expected].sort(), 'Load thresholds differ from the versioned profile.')
      assert.ok(Object.values(entry.thresholds).every(threshold => threshold?.ok === true), 'A required load threshold failed.')
      return entry.values
    }
    const requests = read('ngb_requests', [`count==${iterations}`])
    const failures = read('ngb_failures', ['rate==0'])
    const latency = read('ngb_latency', [`p(95)<${profile.budgets.p95Milliseconds}`, `p(99)<${profile.budgets.p99Milliseconds}`])
    assert.equal(requests.count, iterations, 'Missing or additional measured requests.')
    assert.equal(failures.rate, 0, 'A measured request failed its HTTP/JSON contract.')
    for (const metric of ['min', 'max', 'avg', 'p(95)', 'p(99)']) {
      assert.ok(Number.isFinite(latency[metric]) && latency[metric] >= 0, 'Invalid latency measurement.')
    }
    assert.ok(latency.min <= latency.avg && latency.avg <= latency.max
      && latency.min <= latency['p(95)'] && latency['p(95)'] <= latency['p(99)'] && latency['p(99)'] <= latency.max,
    'Inconsistent latency measurements.')
    assert.ok(latency['p(95)'] < profile.budgets.p95Milliseconds && latency['p(99)'] < profile.budgets.p99Milliseconds,
      'Measured latency exceeds the versioned profile budget.')
    endpoints.push({ name: endpoint.name, requests: requests.count, failureRate: failures.rate, latencyMilliseconds: latency })
  }
  return { profileId: profile.id, runId, repetition, endpoints }
}
