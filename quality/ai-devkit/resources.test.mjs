import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { mkdir, mkdtemp, readFile, realpath, rm, symlink, writeFile } from 'node:fs/promises'
import { hostname, tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { executionEnvironment } from '../../packaging/templates/content/infrastructure/ngb/process.mjs'
import { recoverTestRuns, registerTestRun, testResourceLabels } from '../../packaging/templates/content/infrastructure/ngb/resources.mjs'

const runId = '7bca10f4-3b9e-4c4d-8b22-d67d645b2e78'

async function workspace(t) {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'ngb-resource-contract-')))
  t.after(() => rm(root, { recursive: true, force: true }))
  return root
}

test('resource ownership is persisted before provisioning and detects concurrent edits', async t => {
  const root = await workspace(t)
  const run = await registerTestRun(root, runId)
  const path = join(root, '.ngb/test-runs', runId, 'owner.json')
  assert.equal(JSON.parse(await readFile(path)).ownerPid, process.pid)
  assert.deepEqual(testResourceLabels(run.owner), { 'com.ngb.devkit.run': runId, 'com.ngb.devkit.application': digest(root) })
  await run.launching()
  assert.equal(JSON.parse(await readFile(path)).provisioning, true)
  await run.spawned(process.pid)
  assert.equal(JSON.parse(await readFile(path)).provisionerPid, process.pid)
  await run.settled()
  await assert.rejects(recoverTestRuns(root, { env: executionEnvironment(root) }), /still running/)
  const original = await readFile(path)
  await writeFile(path, JSON.stringify({ ...run.owner, host: 'edited' }))
  await assert.rejects(run.launching(), /changed/)
  await assert.rejects(run.finish(), /changed/)
  await writeFile(path, original)
  await run.finish()
  assert.equal((await recoverTestRuns(root, { env: executionEnvironment(root) })).state, 'not-needed')
})

test('recovery refuses moved receipts, unfinished launch windows, live provisioners and symlinks before Docker access', async t => {
  const root = await workspace(t)
  const env = executionEnvironment(root, { PATH: '' })
  assert.equal((await recoverTestRuns(root, { env })).state, 'not-needed')
  const run = await registerTestRun(root, runId)
  const path = join(root, '.ngb/test-runs', runId, 'owner.json')
  const exitedPid = Number(execFileSync(process.execPath, ['-e', 'process.stdout.write(String(process.pid))'], { encoding: 'utf8' }))
  const owner = { ...run.owner, ownerPid: exitedPid }
  for (const [replacement, expected] of [
    [{ applicationSha256: 'b'.repeat(64) }, /application path/],
    [{ host: `${hostname()}-other` }, /original host/],
    [{ provisioning: true, provisionerPid: null }, /before its process identity/],
    [{ provisioning: true, provisionerPid: process.pid }, /still running/],
  ]) {
    await writeFile(path, JSON.stringify({ ...owner, ...replacement }))
    await assert.rejects(recoverTestRuns(root, { apply: true, env }), expected)
  }
  await rm(path)
  await assert.rejects(recoverTestRuns(root, { env }), /no ownership/)
  await mkdir(join(root, 'outside'))
  await symlink(join(root, 'outside'), path)
  await assert.rejects(recoverTestRuns(root, { env }), /Symbolic/)
})
