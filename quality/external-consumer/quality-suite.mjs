import { readFile } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { atomicJson, checkpointRunner } from './checkpoints.mjs'
import { command } from './execution.mjs'
import { digest } from './release-contracts.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const artifacts = join(repository, 'artifacts')
const identityFile = process.argv[2]
const stage = identityFile
  ? await checkpointRunner(artifacts, JSON.parse(await readFile(identityFile, 'utf8')))
  : async (name, operation) => {
    console.log(`[start] ${name}`)
    await operation()
  }
const run = (executable, args) => command(executable, args, { cwd: repository })

// Direct shell runs need the same early prerequisite check as certification.
await run('k6', ['version'])

await stage('tooling', () => run('node', ['quality/external-consumer/test-tooling.mjs']), ['certification/tooling.json'])
await stage('backend', () => run('bash', ['run-backend-full-coverage.sh']), ['coverage/backend-full/coverage-summary.json'])
await stage('frontend', () => run('bash', ['run-frontend-full-coverage.sh']), ['coverage/frontend-full/coverage-summary.json'])
await stage('cross-browser', async () => {
  await run('npm', ['--prefix', 'ui', 'run', 'test:browser:framework:cross-browser', '--', '--allowOnly=false', '--retry=0'])
  await atomicJson(join(artifacts, 'certification/cross-browser.json'), { status: 'passed' })
}, ['certification/cross-browser.json'])
await stage('performance', async () => {
  await run('npm', ['--prefix', 'performance-tests', 'ci'])
  for (const script of ['typecheck', 'test:tooling', 'test:metric-grouping']) {
    await run('npm', ['--prefix', 'performance-tests', 'run', script])
  }
  await atomicJson(join(artifacts, 'certification/performance.json'), { status: 'passed' })
}, ['certification/performance.json'])

const reports = {}
for (const [gate, path] of Object.entries({
  backend: 'artifacts/coverage/backend-full/coverage-summary.json',
  frontend: 'artifacts/coverage/frontend-full/coverage-summary.json',
  tooling: 'artifacts/certification/tooling.json',
  browsers: 'artifacts/certification/cross-browser.json',
  performance: 'artifacts/certification/performance.json',
})) {
  reports[gate] = { path, sha256: digest(await readFile(join(repository, path))) }
}
await atomicJson(join(artifacts, 'certification/full-quality.json'), {
  schemaVersion: 1, status: 'passed', reports,
  performance: 'Existing volume regression tests and offline diagnostic/metric contracts passed.',
})
