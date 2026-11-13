import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { atomicJson, checkpointRunner } from './checkpoints.mjs'
import { command } from './execution.mjs'
import { digest } from './release-contracts.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const artifacts = join(repository, 'artifacts')
const args = process.argv.slice(2)
const paths = args.filter(arg => !arg.startsWith('--'))
const options = args.filter(arg => arg.startsWith('--'))
assert.ok(paths.length <= 1 && options.every(option => ['--certification', '--preflight'].includes(option)),
  'Usage: quality-suite.mjs [--certification] [--preflight] [identity.json]')
const certification = options.includes('--certification')
const preflight = options.includes('--preflight')
const identityFile = paths[0]
const identity = identityFile ? JSON.parse(await readFile(identityFile, 'utf8')) : null
const backendTestFilter = identity?.excludeNativeOllama
  ? 'FullyQualifiedName!~NGB.AiAssistant.AspNetCore.IntegrationTests.AssistantHttpTests.NativeOllama'
  : undefined
const scope = certification ? 'external-app-certification' : 'full-platform-quality'
const stage = identityFile
  ? await checkpointRunner(artifacts, { ...identity, scope })
  : async (name, operation) => {
    console.log(`[start] ${name}`)
    await operation()
  }
const run = (executable, args, env) => command(executable, args, { cwd: repository, env })

// Direct shell runs need the same early prerequisite check as certification.
await run('k6', ['version'])

await stage('tooling', () => run('node', ['quality/external-consumer/test-tooling.mjs']), ['certification/tooling.json'])
await stage('performance', async () => {
  await run('npm', ['--prefix', 'performance-tests', 'ci'])
  for (const script of ['typecheck', 'test:tooling', 'test:metric-grouping']) {
    await run('npm', ['--prefix', 'performance-tests', 'run', script])
  }
  await atomicJson(join(artifacts, 'certification/performance.json'), { status: 'passed' })
}, ['certification/performance.json'])

if (!preflight) {
  await stage('cross-browser', async () => {
    await run('npm', ['--prefix', 'ui', 'run', 'test:browser:framework:cross-browser', '--', '--allowOnly=false', '--retry=0'])
    await atomicJson(join(artifacts, 'certification/cross-browser.json'), { status: 'passed' })
  }, ['certification/cross-browser.json'])
  if (!certification) {
    await stage('backend', async () => {
      if (backendTestFilter) console.log(`Release backend test filter: ${backendTestFilter}`)
      await run('bash', ['run-backend-full-coverage.sh'],
        backendTestFilter ? { ...process.env, VSTestTestCaseFilter: backendTestFilter } : process.env)
    }, ['coverage/backend-full/coverage-summary.json'])
    await stage('frontend', () => run('bash', ['run-frontend-full-coverage.sh']), ['coverage/frontend-full/coverage-summary.json'])
  }
}

const reports = {}
const reportPaths = {
  tooling: 'artifacts/certification/tooling.json',
  performance: 'artifacts/certification/performance.json',
}
if (!preflight) reportPaths.browsers = 'artifacts/certification/cross-browser.json'
if (!preflight && !certification) {
  reportPaths.backend = 'artifacts/coverage/backend-full/coverage-summary.json'
  reportPaths.frontend = 'artifacts/coverage/frontend-full/coverage-summary.json'
}
for (const [gate, path] of Object.entries(reportPaths)) {
  reports[gate] = { path, sha256: digest(await readFile(join(repository, path))) }
}
const reportName = preflight ? 'quality-preflight.json' : certification ? 'certification-quality.json' : 'full-quality.json'
await atomicJson(join(artifacts, 'certification', reportName), {
  schemaVersion: 1, status: 'passed', scope: preflight ? 'quality-preflight' : scope,
  reports, artifactManifestSha256: identity?.artifactManifestSha256 ?? null,
  backendTestFilter,
  performance: preflight || certification
    ? 'Offline diagnostic/metric contracts passed; volume regression tests and live load profiles were not run.'
    : 'Existing volume regression tests and offline diagnostic/metric contracts passed.',
})
