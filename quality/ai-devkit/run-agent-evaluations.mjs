import assert from 'node:assert/strict'
import { randomUUID } from 'node:crypto'
import { mkdir, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises'
import { basename, dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { command } from '../external-consumer/execution.mjs'
import { atomicJson, lockRun, readOptionalJson } from '../external-consumer/checkpoints.mjs'
import { applicationFiles, loadCandidate } from '../../packaging/templates/content/infrastructure/ngb/application.mjs'
import { assetPaths, readBytes } from '../../packaging/templates/content/infrastructure/ngb/kit-files.mjs'
import { digest, distinctPaths } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { evaluationInputs } from './evaluation-inputs.mjs'
import { evaluationFamily, validateAgentEvaluations } from './evaluation-data.mjs'
import { evaluationState } from './evaluation-state.mjs'
import { discoverAgentClients } from './agent-clients.mjs'
import { redactAgentOutput } from './agent-output.mjs'
import { sourceTree } from './review-contracts.mjs'
import { providerUnavailable } from './agent-availability.mjs'

const scenario = process.argv[3] === '--scenario' ? process.argv[4] : null
assert.ok(process.argv[3] === '--scenario' ? process.argv.length === 5 : process.argv.length === 4,
  'Usage: run-agent-evaluations.mjs <sealed-candidate> <new-verification-output|--check|--scenario client/context/task>')
const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const candidatePath = resolve(process.argv[2])
const checkOnly = process.argv[3] === '--check'
const verificationOutput = scenario ? null : checkOnly
  ? join(candidatePath, 'certification/agent-preflight', randomUUID()) : resolve(process.argv[3])
const candidate = await loadCandidate(candidatePath)
const { matrix, inputs } = await evaluationInputs(repository, candidate)
if (scenario !== null) {
  const [client, context, task, ...extra] = scenario.split('/')
  assert.ok(matrix.clients.includes(client) && matrix.workingDirectories.includes(context)
    && matrix.tasks.some(item => item.id === task) && extra.length === 0, 'Unknown agent scenario.')
}
if (process.env.NGB_AI_EVALUATION_EVIDENCE) {
  assert.equal(scenario, null, 'A live scenario cannot use an imported matrix.')
  await mkdir(dirname(verificationOutput), { recursive: true })
  await command('node', ['quality/ai-devkit/verify-agents.mjs', candidatePath, verificationOutput,
    resolve(process.env.NGB_AI_EVALUATION_EVIDENCE)], { cwd: repository })
  process.exit(0)
}
const profiles = {}
for (const client of matrix.clients) {
  profiles[client] = {}
  for (const family of ['discovery', 'development', 'upgrade']) {
    profiles[client][family] = JSON.parse(await readFile(new URL(`./${client}-${family}-profile.json`, import.meta.url)))
  }
}
const clients = await discoverAgentClients(repository, matrix, profiles)
const root = join(repository, 'artifacts/ai-devkit/evaluation-runs', candidate.manifestSha256)
const identity = { candidateSha256: candidate.manifestSha256, inputs, clients }
const previous = await readOptionalJson(join(root, 'state.json'))
if (previous) assert.equal(previous.identitySha256, digest(JSON.stringify(identity)), 'Saved agent run uses different inputs or client binaries.')
for (const [name, history] of Object.entries(previous?.steps ?? {})) {
  if (scenario && !name.startsWith(scenario + '/')) continue
  assert.notEqual(history.at(-1)?.status, 'failed',
    `Agent step ${name} previously failed. Inspect ${join(root, history.at(-1).directory)}. No automatic model retry is permitted.`)
}
if (checkOnly) {
  if (previous) {
    const check = await evaluationState(root, identity, { log: () => {} })
    for (const [name, history] of Object.entries(previous.steps)) {
      if (history.at(-1)?.status === 'passed') await check(name, () => assert.fail('Preflight must not execute an evaluation.'))
    }
  }
  console.log(`Agent prerequisites: passed; ${matrix.clients.length * matrix.workingDirectories.length * matrix.tasks.length} scenarios, authenticated pinned clients.`)
  console.log(`Agent results and resume state: ${root}`)
  process.exit(0)
}
const unlock = await lockRun(join(root, 'run.lock'))
const abort = new AbortController()
const cancel = () => abort.abort()
process.once('SIGINT', cancel)
process.once('SIGTERM', cancel)
const parent = process.ppid
const watchdog = setInterval(() => {
  if (process.ppid !== parent) process.kill(process.pid, 'SIGINT')
}, 500)
watchdog.unref()
const json = async path => JSON.parse(await readFile(path, 'utf8'))
const unavailable = async directory => {
  const descriptor = await readOptionalJson(join(directory, 'invocation.json'))
  if (descriptor) {
    const attempt = await readOptionalJson(join(directory, `evaluation/attempts/${descriptor.client}/${descriptor.context}/${descriptor.task}/attempt.json`))
    if (attempt?.state !== 'failed' || !attempt.transcript) return false
    const bytes = await readBytes(join(directory, 'evaluation'), attempt.transcript.path, 32 * 1024 * 1024)
    return bytes && providerUnavailable(descriptor.client, attempt.failure, bytes.toString('utf8'))
  }
  const review = await readOptionalJson(join(directory, 'review/review.json'))
  if (review?.status !== 'failed') return false
  const bytes = await readBytes(join(directory, 'review'), 'transcript.jsonl', 32 * 1024 * 1024)
  return bytes && providerUnavailable('claude', review.failure, bytes.toString('utf8'))
}
const worker = async (directory, name, script, args) => {
  abort.signal.throwIfAborted()
  try {
    const output = await command('node', ['quality/ai-devkit/evaluation-worker.mjs', script, ...args], {
      cwd: repository, env: { ...process.env, NGB_AI_WORKER_LEASE: join(directory, 'worker.lock') },
      capture: true, timeout: 60 * 60_000,
    })
    await writeFile(join(directory, `${name}.log`), redactAgentOutput(output), { mode: 0o600, flag: 'wx' })
  } catch (error) {
    await writeFile(join(directory, `${name}.log`), redactAgentOutput(error.message), { mode: 0o600, flag: 'wx' })
    if (abort.signal.aborted) error.code = 'ABORT_ERR'
    else if (await unavailable(directory)) {
      error.code = 'NGB_AGENT_UNAVAILABLE'
      error.message = `The provider reported unavailable usage/quota. Resume certify after access returns. Evidence: ${directory}`
    }
    throw error
  }
}
// Each native evaluation contains one attempt. Its directory prefix is metadata
// owned by the runner and does not count towards the nesting limit for app assets.
const evaluationFiles = async directory => {
  const { client, context, task } = await json(join(directory, 'invocation.json'))
  const evaluation = join(directory, 'evaluation')
  let parent = join(evaluation, 'attempts')
  for (const component of [client, context, task]) {
    assert.deepEqual(await readdir(parent), [component], 'Unexpected attempt in evaluation output.')
    parent = join(parent, component)
  }
  const paths = await assetPaths(evaluation, `attempts/${client}/${context}/${task}`)
  for (const entry of await readdir(evaluation, { withFileTypes: true })) {
    assert.ok(!entry.isSymbolicLink())
    if (entry.name === 'attempts') continue
    if (entry.isDirectory()) paths.push(...await assetPaths(evaluation, entry.name))
    else {
      assert.ok(entry.isFile())
      paths.push(entry.name)
    }
  }
  distinctPaths(paths)
  return paths.map(path => `evaluation/${path}`)
}
const persist = async (directory, value) => {
  const files = {}
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    if (entry.name === 'result.json') continue
    assert.ok(!entry.isSymbolicLink())
    const paths = entry.name === 'evaluation' && entry.isDirectory() ? await evaluationFiles(directory)
      : entry.isDirectory() ? await assetPaths(directory, entry.name) : [entry.name]
    for (const path of paths) {
      files[`${relative(root, directory)}/${path}`] = digest(await readBytes(directory, path, 64 * 1024 * 1024))
    }
  }
  const resultPath = join(directory, 'result.json')
  await atomicJson(resultPath, { value, sha256: digest(JSON.stringify(value)), files })
  return { value, files: [...Object.keys(files), relative(root, resultPath)] }
}
const recover = async directory => {
  // A dead orchestrator can leave a worker briefly alive during cancellation.
  // Never launch another paid inference while that worker still owns its lease.
  const release = await lockRun(join(directory, 'worker.lock'))
  await release()
  const receipt = await readOptionalJson(join(directory, 'result.json'))
  if (!receipt) return null
  assert.equal(receipt.sha256, digest(JSON.stringify(receipt.value)), 'Interrupted result was modified.')
  for (const [path, sha256] of Object.entries(receipt.files)) {
    const prefix = `${relative(root, directory)}/`
    assert.ok(path.startsWith(prefix))
    assert.equal(digest(await readBytes(directory, path.slice(prefix.length), 64 * 1024 * 1024)), sha256, `Interrupted evidence changed: ${path}`)
  }
  return { value: receipt.value, files: [...Object.keys(receipt.files), relative(root, join(directory, 'result.json'))] }
}
const loadInference = async (directory, descriptor) => {
  const evaluation = join(directory, 'evaluation')
  const prefix = `attempts/${descriptor.client}/${descriptor.context}/${descriptor.task}`
  const attemptPath = join(evaluation, prefix, 'attempt.json')
  const attempt = await readOptionalJson(attemptPath)
  if (!attempt) return null
  if (attempt.state === 'failed' && /cancelled|SIGINT|SIGTERM/.test(attempt.failure ?? '')) return null
  if (attempt.state === 'failed' && await unavailable(directory)) return null
  assert.ok(['passed', 'completed-awaiting-independent-acceptance'].includes(attempt.state),
    `Agent ${prefix} failed: ${attempt.failure ?? attempt.state}. Original evidence: ${attemptPath}`)
  assert.equal(attempt.client, descriptor.client)
  assert.equal(attempt.task, descriptor.task)
  assert.equal(attempt.workingDirectory, descriptor.context)
  const snapshot = join(directory, 'fixture-snapshot')
  if (evaluationFamily(descriptor.task) !== 'discovery') {
    const fixture = await json(join(evaluation, attempt.fixtureManifest.path))
    for (const [path, hash] of Object.entries(fixture.after)) {
      const bytes = await readBytes(evaluation, `${prefix}/produced/${path}`, 32 * 1024 * 1024)
        ?? await readBytes(join(directory, 'baseline/application'), path, 32 * 1024 * 1024)
      assert.ok(bytes && digest(bytes) === hash, `Produced source is missing or changed: ${path}`)
      await mkdir(dirname(join(snapshot, 'application', path)), { recursive: true })
      await writeFile(join(snapshot, 'application', path), bytes, { mode: 0o600 })
    }
    for (const path of ['before.json', 'fixture.json', 'task.json', 'preview.log']) {
      const bytes = await readBytes(join(directory, 'baseline'), path)
      if (bytes) await writeFile(join(snapshot, path), bytes, { mode: 0o600 })
    }
  }
  const result = await persist(directory, { evaluation, attemptPath, attempt, snapshot, prefix })
  assert.match(descriptor.fixture, /^\/private\/tmp\/ngb-ae-[A-Za-z0-9]+\/fixture$/)
  await rm(dirname(descriptor.fixture), { recursive: true, force: true })
  return result
}
const withFixture = async (inference, operation) => {
  const fixture = await mkdtemp('/private/tmp/ngb-ar-')
  try {
    for (const source of await assetPaths(dirname(inference.snapshot), basename(inference.snapshot))) {
      const path = relative(basename(inference.snapshot), source)
      await mkdir(dirname(join(fixture, path)), { recursive: true })
      await writeFile(join(fixture, path), await readFile(join(inference.snapshot, path)), { mode: 0o600, flag: 'wx' })
    }
    assert.equal(sourceTree(await applicationFiles(join(fixture, 'application'))), inference.attempt.producedTreeSha256)
    return await operation(fixture)
  } finally {
    await rm(fixture, { recursive: true, force: true })
  }
}
try {
  const step = await evaluationState(root, identity, { signal: abort.signal })
  const accepted = []
  for (const client of matrix.clients) {
    for (const context of matrix.workingDirectories) {
      for (const task of matrix.tasks) {
        abort.signal.throwIfAborted()
        const name = `${client}/${context}/${task.id}`
        if (scenario && scenario !== name) continue
        const family = evaluationFamily(task.id)
        const inference = await step(`${name}/inference`, async directory => {
          // A short external path is required by the native macOS compiler sandbox.
          const fixtureRoot = await mkdtemp('/private/tmp/ngb-ae-')
          const descriptor = { client, context, task: task.id, fixture: join(fixtureRoot, 'fixture') }
          await atomicJson(join(directory, 'invocation.json'), descriptor)
          if (family !== 'discovery') {
            await worker(directory, 'prepare', `quality/ai-devkit/prepare-${family === 'upgrade' ? 'upgrade-evaluation' : 'development-fixture'}.mjs`,
              [...family === 'upgrade' ? [] : [task.id], candidatePath, descriptor.fixture])
            const original = await applicationFiles(join(descriptor.fixture, 'application'))
            await atomicJson(join(directory, 'before-source.json'), Object.fromEntries(Object.entries(original)
              .filter(([path]) => /\.(?:cs|sql|ts|vue|md)$/.test(path)).map(([path, bytes]) => [path, bytes.toString('utf8')])))
            for (const [path, bytes] of Object.entries(original)) {
              const target = join(directory, 'baseline/application', path)
              await mkdir(dirname(target), { recursive: true })
              await writeFile(target, bytes, { mode: 0o600, flag: 'wx' })
            }
            for (const path of ['before.json', 'fixture.json', 'task.json', 'preview.log']) {
              const bytes = await readBytes(descriptor.fixture, path)
              if (bytes) await writeFile(join(directory, 'baseline', path), bytes, { mode: 0o600, flag: 'wx' })
            }
          }
          await worker(directory, 'evaluate', `quality/ai-devkit/evaluate-${client}-${family}.mjs`, family === 'discovery'
            ? [candidatePath, join(directory, 'evaluation'), clients[client].executable, context]
            : [task.id, context, candidatePath, descriptor.fixture, join(directory, 'evaluation'), clients[client].executable])
          const result = await loadInference(directory, descriptor)
          assert.ok(result, `Evaluator produced no final attempt for ${name}.`)
          return result
        }, async directory => {
          const result = await recover(directory)
          if (result) return result
          const descriptor = await readOptionalJson(join(directory, 'invocation.json'))
          return descriptor ? await loadInference(directory, descriptor) : null
        })
        if (family === 'discovery') {
          accepted.push({ inference })
          continue
        }
        const review = await step(`${name}/review`, async directory => {
          await withFixture(inference, fixture => worker(directory, 'review', 'quality/ai-devkit/review-agent-source.mjs',
            [candidatePath, fixture, inference.evaluation, inference.attemptPath, join(directory, 'review'), clients.claude.executable]))
          const report = await json(join(directory, 'review/review.json'))
          assert.equal(report.status, 'passed', report.failure)
          assert.equal(report.producedTreeSha256, inference.attempt.producedTreeSha256)
          return await persist(directory, { directory: join(directory, 'review'), report })
        }, async directory => {
          const result = await recover(directory)
          if (result) return result
          const report = await readOptionalJson(join(directory, 'review/review.json'))
          if (report?.status === 'failed' && await unavailable(directory)) return null
          if (report?.status === 'failed') assert.match(report.failure ?? '', /abort|cancelled|SIGINT|SIGTERM/i,
            'A completed negative source review cannot be retried automatically.')
          if (report?.status !== 'passed') return null
          assert.equal(report.producedTreeSha256, inference.attempt.producedTreeSha256)
          assert.equal(report.selectionSha256, digest(await readFile(join(directory, 'review/selection.json'))))
          return await persist(directory, { directory: join(directory, 'review'), report })
        })
        const acceptance = await step(`${name}/acceptance`, async directory => {
          const output = join(directory, 'oracle')
          await withFixture(inference, fixture => worker(directory, 'acceptance', `quality/ai-devkit/run-${family}-oracle.mjs`, family === 'upgrade'
            ? [candidatePath, join(fixture, 'application'), fixture, output, inference.attempt.producedTreeSha256]
            : [task.id, candidatePath, join(fixture, 'application'), join(fixture, 'before.json'),
              output, inference.attempt.producedTreeSha256, join(review.directory, 'selection.json')]))
          const report = await json(join(output, 'acceptance.json'))
          assert.equal(report.status, 'passed', report.failure)
          assert.equal(report.producedTreeSha256, inference.attempt.producedTreeSha256)
          return await persist(directory, { directory: output, report })
        }, recover)
        accepted.push({ inference, review, acceptance })
      }
    }
  }
  if (scenario) {
    assert.equal(accepted.length, 1)
    console.log(`Agent scenario passed: ${scenario}. Checkpoints: ${root}`)
  } else {
    await exportMatrix(accepted)
  }
} catch (error) {
  console.error(redactAgentOutput(error.message))
  console.error(`Agent state and retained evidence: ${root}`)
  process.exitCode = abort.signal.aborted ? 130 : 2
} finally {
  clearInterval(watchdog)
  process.removeListener('SIGINT', cancel)
  process.removeListener('SIGTERM', cancel)
  await unlock()
}

async function exportMatrix(accepted) {
  // Publish the inventory only after every independent result has passed.
  const evidence = join(root, 'evidence')
  await mkdir(evidence, { recursive: true })
  const save = async (path, bytes) => {
    const existing = await readBytes(evidence, path, 64 * 1024 * 1024)
    if (existing) assert.equal(digest(existing), digest(bytes), `Previously exported evidence changed: ${path}`)
    else {
      await mkdir(dirname(join(evidence, path)), { recursive: true })
      await writeFile(join(evidence, path), bytes, { flag: 'wx', mode: 0o600 })
    }
    return { path, sha256: digest(bytes) }
  }
  const attempts = []
  for (const { inference, review, acceptance } of accepted) {
    const attempt = structuredClone(inference.attempt)
    for (const path of await assetPaths(inference.evaluation, inference.prefix)) {
      await save(path, await readFile(join(inference.evaluation, path)))
    }
    if (acceptance) {
      const family = evaluationFamily(attempt.task)
      const report = structuredClone(acceptance.report)
      const prefix = `${inference.prefix}/oracle`
      for (const path of Object.keys(report.artifacts)) {
        const bytes = await readBytes(acceptance.directory, path, 32 * 1024 * 1024)
        assert.ok(bytes && digest(bytes) === report.artifacts[path], `Independent evidence changed: ${path}`)
        await save(`${prefix}/${path}`, bytes)
      }
      const extra = {
        'raw-acceptance.json': await readFile(join(acceptance.directory, 'acceptance.json')),
        'source-review.json': await readFile(join(review.directory, 'review.json')),
        'source-review-transcript.jsonl': await readFile(join(review.directory, 'transcript.jsonl')),
        'source-review-selection.json': await readFile(join(review.directory, 'selection.json')),
        'source-review-profile.json': await readFile(new URL('./source-review-profile.json', import.meta.url)),
        'source-review-prompt.txt': await readFile(join(review.directory, 'prompt.txt')),
      }
      for (const [path, bytes] of Object.entries(extra)) {
        report.artifacts[path] = (await save(`${prefix}/${path}`, bytes)).sha256
      }
      if (family === 'upgrade') {
        for (const check of ['custom-content-preserved', 'coordinated-upgrade', 'both-agent-adapters', 'exact-official-content',
          'trusted-preview-read-only', 'clean-locked-builds', 'installed-kit-integrity', 'source-fixture-unchanged']) {
          assert.ok(report.checks.includes(check), `Missing independent upgrade check: ${check}`)
        }
        const selection = await json(join(review.directory, 'selection.json'))
        assert.equal(selection.review.upgradeActionsVerified, true)
        report.checks = matrix.tasks.find(task => task.id === attempt.task).acceptance.map(id => ({ id, state: 'passed' }))
      }
      attempt.state = 'passed'
      attempt.acceptance = report.checks
      attempt.acceptanceReport = await save(`${prefix}/acceptance.json`, `${JSON.stringify(report, null, 2)}\n`)
    }
    attempts.push(attempt)
  }
  const evaluations = { schemaVersion: 1, status: 'passed', profile: matrix.profile, inputs, attempts }
  validateAgentEvaluations(evaluations, matrix, inputs)
  await atomicJson(join(evidence, 'evaluations.json'), evaluations)
  await command('node', ['quality/ai-devkit/verify-agents.mjs', candidatePath, verificationOutput, evidence], { cwd: repository })
  console.log(`Agent matrix: passed. Evidence: ${evidence}`)
}
