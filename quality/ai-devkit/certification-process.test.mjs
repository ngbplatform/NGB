import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { cp, mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import test from 'node:test'

const repository = resolve(import.meta.dirname, '../..')

test('application certification binds capabilities only after successful examples and still verifies the candidate', async t => {
  for (const fail of [true, false]) {
    const root = await mkdtemp(join(tmpdir(), 'ngb-kit-certification-process-'))
    t.after(() => rm(root, { recursive: true, force: true }))
    const save = async (path, text) => {
      await mkdir(dirname(join(root, path)), { recursive: true })
      await writeFile(join(root, path), text)
    }
    for (const path of ['quality/ai-devkit/certify.mjs', 'quality/ai-devkit/certification-data.mjs',
      'quality/ai-devkit/agent-output.mjs', 'quality/external-consumer/checkpoints.mjs', 'quality/external-consumer/execution.mjs']) {
      await save(path, await readFile(join(repository, path)))
    }
    const modules = 'packaging/templates/content/infrastructure/ngb'
    await mkdir(join(root, modules), { recursive: true })
    await cp(join(repository, modules), join(root, modules), { recursive: true })
    await save('quality/external-consumer/release-contracts.mjs',
      `export { digest } from '../../${modules}/kit-contracts.mjs'\n`)
    await save(`${modules}/process.mjs`, `
import { appendFile } from 'node:fs/promises'
export const executionEnvironment = () => ({})
export async function runTool(tool, args) {
  await appendFile(${JSON.stringify(join(root, 'calls.jsonl'))}, JSON.stringify(args) + '\\n')
  const failed = ${fail} && args[0].endsWith('verify-examples.mjs')
  return { state: failed ? 'failed' : 'passed', exitCode: failed ? 1 : 0,
    reason: failed ? 'Example fixture failed' : null, elapsedMs: 1, output: 'Saved diagnostic output' }
}
`)
    await save('candidate/release-manifest.json', '{}')
    const result = spawnSync(process.execPath, ['quality/ai-devkit/certify.mjs', 'application', 'candidate'], {
      cwd: root, encoding: 'utf8', timeout: 15_000,
    })
    assert.equal(result.status, fail ? 2 : 0, result.stderr)
    const report = JSON.parse(await readFile(join(root, 'candidate/certification/ai-kit/application.json')))
    assert.equal(report.status, fail ? 'not-ready' : 'passed')
    assert.equal(report.checks.find(check => check.id === 'examples').state, fail ? 'failed' : 'passed')
    assert.equal(report.checks.find(check => check.id === 'capabilities').state, fail ? 'not-run' : 'passed')
    assert.equal(report.checks.find(check => check.id === 'candidate.after').state, 'passed')
    const calls = (await readFile(join(root, 'calls.jsonl'), 'utf8')).trim().split('\n').map(JSON.parse)
    assert.equal(calls.filter(args => args[0].endsWith('verify-capabilities.mjs')).length, fail ? 0 : 1)
    assert.deepEqual(calls.at(-1).slice(0, 2), ['quality/external-consumer/release.mjs', 'verify'])
  }
})
