import assert from 'node:assert/strict'
import { mkdir, mkdtemp, readFile, realpath, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { acquireCli } from '../../packaging/templates/content/infrastructure/ngb/acquisition.mjs'
import { loadCandidate } from '../../packaging/templates/content/infrastructure/ngb/application.mjs'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { assetPaths, readBytes, safePath } from '../../packaging/templates/content/infrastructure/ngb/kit-files.mjs'
import { executionEnvironment, runTool } from '../../packaging/templates/content/infrastructure/ngb/process.mjs'
import { assertConsumerIsolation } from '../external-consumer/contracts.mjs'

assert.equal(process.argv.length, 4, 'Usage: node verify-starter.mjs <sealed-candidate> <new-evidence-directory>')
const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const candidate = await realpath(resolve(process.argv[2]))
const output = resolve(process.argv[3])
await mkdir(output)
const manifestBytes = await readFile(join(candidate, 'release-manifest.json'))
const manifest = JSON.parse(manifestBytes)
const stage = await realpath(await mkdtemp(join(tmpdir(), 'ngb-starter-acceptance-')))
const root = join(stage, 'application')
const env = executionEnvironment(join(stage, 'home'))
const abort = new AbortController()
const cancel = () => abort.abort()
const deadline = setTimeout(cancel, 3600000)
process.once('SIGINT', cancel)
process.once('SIGTERM', cancel)
const evidence = { schemaVersion: 1, status: 'not-ready', artifactManifestSha256: digest(manifestBytes),
  stage, version: manifest.version, startedAt: new Date().toISOString() }
const invoke = async (id, args, cwd) => {
  const result = await runTool('node', args, { cwd, env, signal: abort.signal, timeoutMs: 3600000, maxBytes: 16 * 1024 * 1024 })
  await writeFile(join(output, `${id}.log`), result.output, { mode: 0o600 })
  assert.equal(result.state, 'passed', `Starter ${id} failed (${result.reason ?? result.exitCode}); see its saved log.`)
  return result.output
}
try {
  const acquired = await acquireCli(manifest.version, { candidate: await loadCandidate(candidate),
    // The application does not exist until create succeeds. Keep acquisition
    // outside its already-existing parent, including every future app file.
    applicationRoot: stage, signal: abort.signal })
  evidence.cliIntegrity = acquired.integrity
  await invoke('create', [acquired.entry, 'create', 'KitAcceptance', '--version', manifest.version,
    '--packages', candidate, '--output', root], stage)
  await assertConsumerIsolation(root, repository)
  const text = await invoke('certify', [join(root, 'infrastructure/ngb.mjs'), 'certify', '--profile', 'external-application', '--format', 'json'], root)
  const report = JSON.parse(text)
  assert.equal(report.status, 'passed')
  assert.equal(report.sdkVersion, manifest.version)
  assert.equal(report.profile, 'external-application')
  assert.ok(report.checks.length > 0 && report.checks.every(check => !check.required || check.state === 'passed'))
  assert.equal(report.kitSha256, JSON.parse(await readFile(join(root, 'ai/kit.json'))).payloadSha256)
  await safePath(root, report.outputDirectory)
  const saved = await readBytes(root, `${report.outputDirectory}/report.json`, 16 * 1024 * 1024)
  assert.deepEqual(JSON.parse(saved), report)
  evidence.profile = report
  evidence.reports = {}
  for (const path of await assetPaths(root, report.outputDirectory)) {
    const bytes = await readBytes(root, path, 16 * 1024 * 1024)
    const relative = path.slice(report.outputDirectory.length + 1)
    const destination = join(output, 'reports', relative)
    await mkdir(dirname(destination), { recursive: true })
    await writeFile(destination, bytes, { mode: 0o600 })
    evidence.reports[`reports/${relative}`] = digest(bytes)
  }
  evidence.status = 'passed'
} catch (error) {
  evidence.failure = error.message
  process.exitCode = 1
} finally {
  clearTimeout(deadline)
  process.removeListener('SIGINT', cancel)
  process.removeListener('SIGTERM', cancel)
  // Preserve the app and its run receipts for failure diagnosis/recovery, but
  // discard the isolated acquisition/build credentials and cache home.
  await rm(env.HOME, { recursive: true, force: true })
  evidence.completedAt = new Date().toISOString()
  await writeFile(join(output, 'starter.json'), `${JSON.stringify(evidence, null, 2)}\n`, { mode: 0o600 })
}
console.log(`Starter acceptance: ${evidence.status}. Evidence: ${join(output, 'starter.json')}`)
