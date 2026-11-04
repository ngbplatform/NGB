import assert from 'node:assert/strict'
import { mkdir, mkdtemp, realpath, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { applicationFiles } from '../../packaging/templates/content/infrastructure/ngb/application.mjs'
import { startTestEnvironment } from '../../packaging/templates/content/infrastructure/ngb/environment.mjs'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { runLoadProfile } from '../../packaging/templates/content/infrastructure/ngb/performance-runner.mjs'
import { executionEnvironment } from '../../packaging/templates/content/infrastructure/ngb/process.mjs'

assert.equal(process.argv.length, 4, 'Usage: node quality/ai-devkit/verify-load.mjs <generated-app> <new-evidence-directory>')
const root = await realpath(resolve(process.argv[2]))
const output = resolve(process.argv[3])
await mkdir(output, { mode: 0o700 })
const home = await realpath(await mkdtemp(join(tmpdir(), 'ngb-load-probe-')))
const abort = new AbortController()
const cancel = () => abort.abort()
const deadline = setTimeout(cancel, 1200000)
process.once('SIGINT', cancel)
process.once('SIGTERM', cancel)
const snapshot = async () => Object.fromEntries(Object.entries(await applicationFiles(root))
  .map(([path, bytes]) => [path, digest(bytes)]).sort(([left], [right]) => left.localeCompare(right, 'en')))
const inputs = await snapshot()
const evidence = { schemaVersion: 1, status: 'not-ready', inputs, sourceSha256: digest(JSON.stringify(inputs)),
  scope: 'Disposable starter HTTP load development probe; not complete release certification.' }
let environment
try {
  environment = await startTestEnvironment(root, { signal: abort.signal })
  evidence.load = await runLoadProfile({ environment, output, env: executionEnvironment(home), signal: abort.signal })
  assert.ok(evidence.load.passed, 'A required load check did not pass.')
  assert.deepEqual(await snapshot(), inputs, 'Application sources changed during the load probe.')
  evidence.status = 'passed'
} catch (error) {
  evidence.failure = error.message
  process.exitCode = 1
} finally {
  if (environment) {
    try {
      await environment.stop()
      evidence.cleanup = { state: 'passed', runId: environment.runId }
    } catch (error) {
      evidence.cleanup = { state: 'failed', runId: environment.runId, message: error.message }
      evidence.status = 'not-ready'
      process.exitCode = 1
    }
  }
  clearTimeout(deadline)
  process.removeListener('SIGINT', cancel)
  process.removeListener('SIGTERM', cancel)
  await rm(home, { recursive: true, force: true })
  await writeFile(join(output, 'load.json'), `${JSON.stringify(evidence, null, 2)}\n`, { mode: 0o600 })
}
console.log(`Starter load: ${evidence.status}. Evidence: ${join(output, 'load.json')}`)
