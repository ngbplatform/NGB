import assert from 'node:assert/strict'
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { parseArgs } from 'node:util'
import { inspectCapabilities, preparePayload } from '../../ai-devkit/prepare.mjs'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { readBytes } from '../../packaging/templates/content/infrastructure/ngb/kit-files.mjs'
import { bindExampleAcceptance } from './example-evidence.mjs'

const { values } = parseArgs({ options: {
  output: { type: 'string' }, packages: { type: 'string' }, examples: { type: 'string' },
} })
const output = resolve(values.output ?? 'artifacts/ai-devkit/capabilities.json')
const payload = await preparePayload()
assert.equal(Boolean(values.packages), Boolean(values.examples), 'Use --packages and --examples together to bind executable acceptance.')
let acceptance = null
if (values.examples) {
  const root = resolve(values.examples)
  const report = JSON.parse(await readBytes(root, 'examples.json', 8 * 1024 * 1024))
  assert.equal(report.status, 'passed', 'Independent examples did not pass; inspect their saved test reports before binding acceptance.')
  const artifacts = new Map()
  for (const example of report.examples) {
    for (const suite of example.suites) {
      for (const artifact of [suite.trx, suite.coverage]) {
        artifacts.set(artifact.path, await readBytes(root, artifact.path, 16 * 1024 * 1024))
      }
    }
    for (const artifact of Object.values(example.frontend?.reports ?? {})) {
      artifacts.set(artifact.path, await readBytes(root, artifact.path, 16 * 1024 * 1024))
    }
  }
  acceptance = bindExampleAcceptance({ payload, report, artifacts,
    candidateSha256: digest(await readFile(join(resolve(values.packages), 'release-manifest.json'))),
    runnerSha256: digest(await readFile(new URL('./verify-examples.mjs', import.meta.url))) })
}
const result = inspectCapabilities(payload, acceptance)
await mkdir(dirname(output), { recursive: true })
await writeFile(output, `${JSON.stringify(result, null, 2)}\n`)
console.log(`Capabilities: ${result.passedCapabilities}/${result.capabilities} accepted; `
  + `${result.classifiedContracts}/${result.publicContracts} public contracts classified. ${result.status.toUpperCase()}.`)
assert.equal(result.status, 'passed', 'Capability classification and bound executable acceptance must be complete before certification.')
