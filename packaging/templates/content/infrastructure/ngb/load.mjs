import http from 'k6/http'
import { Counter, Rate, Trend } from 'k6/metrics'

const profile = JSON.parse(open('./load-profile.json'))
const requests = new Counter('ngb_requests')
const failures = new Rate('ngb_failures')
const latency = new Trend('ngb_latency', true)
const iterations = profile.virtualUsers * profile.iterationsPerUser
const thresholds = {}

for (const endpoint of profile.endpoints) {
  const tag = `{endpoint:${endpoint.name}}`
  thresholds[`ngb_requests${tag}`] = [`count==${iterations}`]
  thresholds[`ngb_failures${tag}`] = ['rate==0']
  thresholds[`ngb_latency${tag}`] = [
    `p(95)<${profile.budgets.p95Milliseconds}`,
    `p(99)<${profile.budgets.p99Milliseconds}`,
  ]
}

export const options = {
  scenarios: {
    measured: {
      executor: 'per-vu-iterations',
      vus: profile.virtualUsers,
      iterations: profile.iterationsPerUser,
      maxDuration: profile.maximumDuration,
      gracefulStop: '0s',
    },
  },
  thresholds,
  summaryTrendStats: ['min', 'max', 'avg', 'p(95)', 'p(99)'],
  setupTimeout: '2m',
  maxRedirects: 0,
  userAgent: 'NGB-Development-Kit/starter-http-v1',
}

function request(endpoint) {
  const response = http.get(`${__ENV.NGB_LOAD_ORIGIN}${endpoint.path}`, {
    headers: { Authorization: `Bearer ${__ENV.NGB_LOAD_TOKEN}` },
    timeout: profile.requestTimeout,
  })
  let successful = response.status === 200
  try {
    const body = response.json()
    successful = successful && body !== null && typeof body === 'object'
  } catch {
    successful = false
  }
  return { successful, duration: response.timings.duration }
}

export function setup() {
  if (!/^http:\/\/127\.0\.0\.1:[1-9][0-9]{3,4}$/.test(__ENV.NGB_LOAD_ORIGIN)) {
    throw new Error('The starter load profile requires its disposable loopback API.')
  }
  if (!__ENV.NGB_LOAD_TOKEN) throw new Error('The starter load profile requires a test-only access token.')
  for (let iteration = 0; iteration < profile.warmupIterations; iteration += 1) {
    for (const endpoint of profile.endpoints) {
      if (!request(endpoint).successful) throw new Error(`Warmup failed: ${endpoint.name}`)
    }
  }
}

export default function () {
  for (const endpoint of profile.endpoints) {
    const result = request(endpoint)
    const tags = { endpoint: endpoint.name }
    requests.add(1, tags)
    failures.add(!result.successful, tags)
    latency.add(result.duration, tags)
  }
}

export function handleSummary(data) {
  return {
    [__ENV.NGB_LOAD_OUTPUT]: JSON.stringify({
      schemaVersion: 1,
      profileId: profile.id,
      profileSha256: __ENV.NGB_LOAD_PROFILE_SHA256,
      runId: __ENV.NGB_LOAD_RUN_ID,
      repetition: Number(__ENV.NGB_LOAD_REPETITION),
      data,
    }),
  }
}
