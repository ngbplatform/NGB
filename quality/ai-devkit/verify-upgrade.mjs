import assert from 'node:assert/strict'
import { mkdir, mkdtemp, readFile, realpath, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { acquireCli } from '../../packaging/templates/content/infrastructure/ngb/acquisition.mjs'
import { applicationFiles, loadCandidate } from '../../packaging/templates/content/infrastructure/ngb/application.mjs'
import { stageTestSources } from '../../packaging/templates/content/infrastructure/ngb/environment.mjs'
import { blockEnd, blockStart, digest, managedBlock } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { validateInstalled } from '../../packaging/templates/content/infrastructure/ngb/kit-plan.mjs'
import { executionEnvironment, runTool } from '../../packaging/templates/content/infrastructure/ngb/process.mjs'
import { assertConsumerIsolation, verifyFrozenFixture } from '../external-consumer/contracts.mjs'

assert.equal(process.argv.length, 4, 'Usage: node verify-upgrade.mjs <sealed-candidate> <new-evidence-directory>')
const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const candidatePath = await realpath(resolve(process.argv[2]))
const output = resolve(process.argv[3])
await mkdir(output)
const manifestBytes = await readFile(join(candidatePath, 'release-manifest.json'))
const candidate = await loadCandidate(candidatePath)
const sourceBytes = await readFile(join(repository, 'quality/upgrade-certification/source-3.3.0.json'))
const source = JSON.parse(sourceBytes)
const fixture = join(repository, 'quality/upgrade-certification/fixtures/3.3.0')
await verifyFrozenFixture(fixture, source)
const stage = await realpath(await mkdtemp(join(tmpdir(), 'ngb-kit-upgrade-acceptance-')))
const root = join(stage, 'application')
const env = executionEnvironment(join(stage, 'home'))
const abort = new AbortController()
const cancel = () => abort.abort()
const deadline = setTimeout(cancel, 3600000)
process.once('SIGINT', cancel)
process.once('SIGTERM', cancel)
const evidence = { schemaVersion: 1, status: 'not-ready', artifactManifestSha256: digest(manifestBytes),
  sourceFixtureSha256: digest(sourceBytes), stage, checks: [], startedAt: new Date().toISOString() }
const invoke = async (id, args) => {
  const result = await runTool('node', args, { cwd: root, env, signal: abort.signal, timeoutMs: 3600000, maxBytes: 16 * 1024 * 1024 })
  await writeFile(join(output, `${id}.log`), result.output, { mode: 0o600 })
  assert.equal(result.state, 'passed', `Upgrade ${id} failed (${result.reason ?? result.exitCode}); see its saved log.`)
  return JSON.parse(result.output)
}
try {
  await stageTestSources(fixture, root)
  await mkdir(join(root, 'ai/custom/skills/app-upgrade-proof'), { recursive: true })
  const skill = '---\nname: app-upgrade-proof\ndescription: Review application-owned upgrade decisions and their recorded acceptance outcomes.\n---\n\nPreserve this application guidance during an official upgrade.\n'
  await writeFile(join(root, 'ai/custom/skills/app-upgrade-proof/SKILL.md'), skill)
  const original = await applicationFiles(root)
  const acquired = await acquireCli(candidate.version, { candidate, applicationRoot: root, signal: abort.signal })
  evidence.cliIntegrity = acquired.integrity
  const args = [acquired.entry, 'upgrade', '--app', root, '--to', candidate.version, '--packages', candidatePath, '--format', 'json']
  const preview = await invoke('preview', args)
  assert.equal(preview.status, 'passed')
  assert.equal(preview.applied, false)
  assert.deepEqual(await applicationFiles(root), original, 'Upgrade preview mutated application sources.')
  evidence.checks.push('preview-read-only')
  const applied = await invoke('apply', [...args, '--apply'])
  assert.equal(applied.status, 'passed')
  assert.equal(applied.applied, true)
  const updated = await applicationFiles(root)
  const planned = new Set([...preview.dependencies, ...preview.plan.entries.map(entry => entry.path),
    '.gitignore', 'web/package-lock.json'])
  const preserved = []
  for (const [path, bytes] of Object.entries(original)) {
    if (planned.has(path) || path.endsWith('/packages.lock.json')) continue
    assert.deepEqual(updated[path], bytes, `Upgrade changed an application-owned file: ${path}`)
    preserved.push(path)
  }
  for (const path of ['AGENTS.md', 'CLAUDE.md']) {
    const current = updated[path].toString('utf8')
    const block = managedBlock(current)
    assert.ok(block, `Missing official managed block: ${path}`)
    assert.ok(current.includes(original[path].toString('utf8').trim()), `Root application instructions were replaced: ${path}`)
    assert.equal(current.split(blockStart).length, 2)
    assert.equal(current.split(blockEnd).length, 2)
  }
  assert.equal(updated['ai/custom/skills/app-upgrade-proof/SKILL.md'].toString('utf8'), skill)
  for (const agent of ['agents', 'claude']) {
    assert.match(updated[`.${agent}/skills/app-upgrade-proof/SKILL.md`].toString('utf8'), /canonical application skill/)
  }
  const payload = JSON.parse(updated['infrastructure/ngb/kit-payload.json'])
  validateInstalled(JSON.parse(updated['ai/kit.json']), payload)
  const status = await invoke('status', [join(root, 'infrastructure/ngb.mjs'), 'ai', 'status', '--format', 'json'])
  assert.equal(status.status, 'passed')
  await assertConsumerIsolation(root, repository)
  const frozenAfter = JSON.parse(await readFile(join(repository, 'quality/upgrade-certification/source-3.3.0.json')))
  assert.deepEqual(frozenAfter, source)
  await verifyFrozenFixture(fixture, source)
  evidence.checks.push('isolated-builds', 'source-free-target-cli', 'coordinated-sdk-cli-kit',
    'root-instructions-preserved', 'custom-skill-preserved', 'both-custom-adapters', 'source-fixture-unchanged')
  evidence.preservedFiles = preserved
  evidence.preview = preview
  evidence.applied = applied
  evidence.persistedProfiles = {}
  for (const profile of ['core-upgrade', 'notes-upgrade', 'attachments-upgrade']) {
    const bytes = await readFile(join(candidatePath, 'certification', `${profile}.json`))
    const report = JSON.parse(bytes)
    assert.equal(report.status, 'passed', `Persisted application profile has not passed: ${profile}`)
    assert.equal(report.profile, profile)
    assert.equal(report.source, '3.3.0')
    assert.equal(report.target, candidate.version)
    assert.equal(report.artifactManifestSha256, evidence.artifactManifestSha256, 'Persisted upgrade evidence belongs to another candidate.')
    assert.equal(report.upgrade?.manifestSha256, evidence.artifactManifestSha256)
    for (const assertion of ['coordinatedCliKitUpgrade', 'preservationAndContinuation', 'pendingJobAndIdempotentEffect',
      'targetBrowserSmoke', 'repeatedMigrationPreservesState', 'failedMigrationBlocksStartup']) {
      assert.equal(report.assertions[assertion], 'passed', `Missing persisted upgrade acceptance: ${profile}/${assertion}`)
    }
    if (profile !== 'core-upgrade') assert.equal(report.assertions.contentPreserved, 'passed')
    await writeFile(join(output, `${profile}.json`), bytes, { mode: 0o600 })
    evidence.persistedProfiles[profile] = digest(bytes)
  }
  evidence.checks.push('persisted-core-notes-attachments-upgrades')
  evidence.status = 'passed'
} catch (error) {
  evidence.failure = error.message
  process.exitCode = 1
} finally {
  clearTimeout(deadline)
  process.removeListener('SIGINT', cancel)
  process.removeListener('SIGTERM', cancel)
  await rm(env.HOME, { recursive: true, force: true })
  evidence.completedAt = new Date().toISOString()
  await writeFile(join(output, 'upgrade.json'), `${JSON.stringify(evidence, null, 2)}\n`, { mode: 0o600 })
}
console.log(`Coordinated CLI/Kit upgrade: ${evidence.status}. Evidence: ${join(output, 'upgrade.json')}`)
