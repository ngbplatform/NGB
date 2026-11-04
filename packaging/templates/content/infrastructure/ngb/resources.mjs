import assert from 'node:assert/strict'
import { randomUUID } from 'node:crypto'
import { mkdir, open, readdir, realpath, rename, rm } from 'node:fs/promises'
import { hostname } from 'node:os'
import { digest } from './kit-contracts.mjs'
import { readAsset, safePath } from './file-data.mjs'
import { resourceApplicationLabel, resourceIdentity, resourceOwnerLabel, validateResourceOwner } from './resource-data.mjs'
import { runTool } from './process.mjs'

const runsPath = '.ngb/test-runs'

function assertStopped(pid, role) {
  try {
    process.kill(pid, 0)
    throw new Error(`The ${role} process ${pid} is still running; wait for it to exit before resource recovery.`)
  } catch (error) {
    if (error.code !== 'ESRCH') throw error
  }
}

async function saveOwner(root, path, owner) {
  const target = await safePath(root, path)
  const temporary = `${target}.${randomUUID()}`
  let handle
  try {
    handle = await open(temporary, 'wx', 0o600)
    await handle.writeFile(`${JSON.stringify(owner)}\n`)
    await handle.sync()
    await handle.close()
    handle = null
    await safePath(root, path)
    await rename(temporary, target)
  } finally {
    await handle?.close()
    await rm(temporary, { force: true })
  }
}

export async function registerTestRun(root, runId) {
  root = await realpath(root)
  const owner = validateResourceOwner({ schemaVersion: 1, runId, applicationSha256: digest(root), host: hostname(),
    ownerPid: process.pid, provisioning: false, provisionerPid: null },
  { runId, applicationSha256: digest(root), host: hostname() })
  const directory = `${runsPath}/${runId}`
  const ownerPath = `${directory}/owner.json`
  await mkdir(await safePath(root, runsPath), { recursive: true, mode: 0o700 })
  await mkdir(await safePath(root, directory), { mode: 0o700 })
  await saveOwner(root, ownerPath, owner)
  const update = async changes => {
    assert.deepEqual(JSON.parse(await readAsset(root, ownerPath, 4096)), owner, 'Test ownership receipt changed during execution.')
    const next = { ...owner, ...changes }
    await saveOwner(root, ownerPath, next)
    Object.assign(owner, next)
  }
  return {
    owner,
    stage: await safePath(root, `${directory}/stage`),
    launching: () => update({ provisioning: true, provisionerPid: null }),
    spawned: pid => update({ provisioning: true, provisionerPid: pid }),
    settled: () => update({ provisioning: false, provisionerPid: null }),
    finish: async () => {
      assert.deepEqual(JSON.parse(await readAsset(root, ownerPath, 4096)), owner, 'Test ownership receipt changed before cleanup.')
      await rm(await safePath(root, directory), { recursive: true })
    },
  }
}

async function docker(args, env, cwd) {
  const result = await runTool('docker', args, { cwd, env, timeoutMs: 60000, maxBytes: 1024 * 1024 })
  assert.equal(result.state, 'passed', `Cannot inspect or clean owned test resources: ${result.reason ?? result.exitCode}`)
  return result.output.trim()
}

async function inspectResource(kind, id, owner, env, cwd) {
  const format = kind === 'container'
    ? '{"Id":{{json .Id}},"Config":{"Labels":{{json .Config.Labels}}}}'
    : kind === 'network' ? '{"Id":{{json .Id}},"Labels":{{json .Labels}}}'
      : '{"Name":{{json .Name}},"Labels":{{json .Labels}}}'
  const args = kind === 'container' ? ['inspect'] : [kind, 'inspect']
  return resourceIdentity(kind, [JSON.parse(await docker([...args, '--format', format, id], env, cwd))], owner)
}

export async function inspectTestResources(owner, env, cwd) {
  const resources = []
  for (const kind of ['container', 'network', 'volume']) {
    const args = kind === 'container' ? ['ps', '--all', '--quiet', '--no-trunc'] : [kind, 'ls', '--quiet']
    const listed = await docker([...args, '--filter', `label=com.docker.compose.project=ngb-kit-${owner.runId}`], env, cwd)
    const ids = listed.split(/\s+/).filter(Boolean)
    assert.ok(ids.length <= 128 && new Set(ids).size === ids.length, 'Test resource inventory exceeds its bounds.')
    for (const id of ids) {
      assert.match(id, /^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,255}$/, 'Invalid Docker resource identifier.')
      resources.push(await inspectResource(kind, id, owner, env, cwd))
    }
  }
  return resources
}

export async function removeTestResources(owner, env, cwd) {
  // Validate the complete inventory before deleting any resource, then recheck
  // each immutable container/network ID or volume name immediately before use.
  const resources = await inspectTestResources(owner, env, cwd)
  for (const resource of resources) {
    assert.deepEqual(await inspectResource(resource.kind, resource.id, owner, env, cwd), resource,
      'Test resource identity changed during cleanup.')
    const args = resource.kind === 'container' ? ['rm', '--force', resource.id] : [resource.kind, 'rm', resource.id]
    await docker(args, env, cwd)
  }
  assert.deepEqual(await inspectTestResources(owner, env, cwd), [], 'Test resources remain after cleanup; preserve the recovery receipt.')
  return resources
}

export async function recoverTestRuns(root, { apply = false, env }) {
  root = await realpath(root)
  let entries
  try {
    entries = await readdir(await safePath(root, runsPath), { withFileTypes: true })
  } catch (error) {
    if (error.code !== 'ENOENT') throw error
    return { state: 'not-needed', runs: [] }
  }
  assert.ok(entries.length <= 128, 'Too many unfinished test runs; inspect the ownership records.')
  const runs = []
  for (const entry of entries.sort((left, right) => left.name.localeCompare(right.name, 'en'))) {
    assert.ok(entry.isDirectory() && !entry.isSymbolicLink(), 'Unexpected test recovery entry.')
    const directory = `${runsPath}/${entry.name}`
    const path = `${directory}/owner.json`
    const text = await readAsset(root, path, 4096)
    assert.ok(text, 'An unfinished test directory has no ownership receipt; inspect it manually.')
    const owner = validateResourceOwner(JSON.parse(text), { runId: entry.name, applicationSha256: digest(root), host: hostname() })
    assertStopped(owner.ownerPid, 'test owner')
    assert.ok(!owner.provisioning || owner.provisionerPid !== null,
      'Provisioning was interrupted before its process identity was recorded; inspect the local provisioner before manual cleanup.')
    if (owner.provisionerPid !== null) assertStopped(owner.provisionerPid, 'test provisioner')
    runs.push({ runId: owner.runId, owner, directory, path, text, resources: await inspectTestResources(owner, env, root) })
  }
  if (apply) {
    for (const run of runs) {
      assert.equal(await readAsset(root, run.path, 4096), run.text, 'Test receipt changed during recovery inspection.')
      assertStopped(run.owner.ownerPid, 'test owner')
      if (run.owner.provisionerPid !== null) assertStopped(run.owner.provisionerPid, 'test provisioner')
      await removeTestResources(run.owner, env, root)
      assert.equal(await readAsset(root, run.path, 4096), run.text, 'Test receipt changed before removing its owned staging directory.')
      await rm(await safePath(root, run.directory), { recursive: true })
    }
  }
  return { state: runs.length === 0 ? 'not-needed' : apply ? 'recovered' : 'preview',
    runs: runs.map(run => ({ runId: run.runId, resources: run.resources })) }
}

export function testResourceLabels(owner) {
  return { [resourceOwnerLabel]: owner.runId, [resourceApplicationLabel]: owner.applicationSha256 }
}
