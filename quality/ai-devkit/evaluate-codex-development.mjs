import assert from 'node:assert/strict'
import { execFile, spawn } from 'node:child_process'
import { createHash } from 'node:crypto'
import { closeSync, createReadStream, openSync, writeSync } from 'node:fs'
import { lstat, mkdir, readFile, readdir, realpath, writeFile } from 'node:fs/promises'
import { homedir } from 'node:os'
import { basename, dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { promisify } from 'node:util'
import { applicationFiles, loadCandidate, managedPackageFiles } from '../../packaging/templates/content/infrastructure/ngb/application.mjs'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { executionEnvironment, runTool } from '../../packaging/templates/content/infrastructure/ngb/process.mjs'
import { assertConsumerIsolation, assertOutsideRepository } from '../external-consumer/contracts.mjs'
import { requiresFrontendAcceptance } from './development-evidence.mjs'
import { codexFinalAnswer, createAgentOutputRedactor, redactAgentOutput } from './agent-output.mjs'

assert.ok(process.argv.length === 8 || (process.argv.length === 9 && process.argv[8] === '--preflight-only'),
  'Usage: node evaluate-codex-development.mjs <task> <root|subdirectory> <candidate> <prepared-fixture> <new-output> <codex-binary> [--preflight-only]')
const taskId = process.argv[2]
const workingDirectory = process.argv[3]
const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const candidate = await loadCandidate(resolve(process.argv[4]))
const fixture = await realpath(resolve(process.argv[5]))
const application = await realpath(join(fixture, 'application'))
const output = resolve(process.argv[6])
const executable = await realpath(resolve(process.argv[7]))
const preflightOnly = process.argv[8] === '--preflight-only'
assertOutsideRepository(repository, fixture)
assertOutsideRepository(application, output)
assert.ok(['existing-document-change', 'new-catalog', 'report-extension', 'permission-correction'].includes(taskId))
assert.ok(['root', 'subdirectory'].includes(workingDirectory))
assert.equal(process.version, 'v24.19.0', 'Use the frozen Node toolchain.')
const matrixBytes = await readFile(new URL('./agent-evaluations.json', import.meta.url))
const profileBytes = await readFile(new URL('./codex-development-profile.json', import.meta.url))
const oracleBytes = await readFile(new URL(`./evaluation-oracles/${taskId}.mjs`, import.meta.url))
const runnerBytes = await readFile(fileURLToPath(import.meta.url))
const redactorBytes = await readFile(new URL('./agent-output.mjs', import.meta.url))
const matrix = JSON.parse(matrixBytes)
const profile = JSON.parse(profileBytes)
const task = matrix.tasks.find(value => value.id === taskId)
assert.equal(process.platform, matrix.platform)
assert.equal(process.arch, matrix.architecture)
assert.equal(candidate.version, matrix.version)
assert.ok(task && profile.maximumSecondsPerAttempt <= matrix.maximumSecondsPerAttempt)
const fixtureBytes = await readFile(join(fixture, 'fixture.json'))
const prepared = JSON.parse(fixtureBytes)
const beforeBytes = await readFile(join(fixture, 'before.json'))
const before = JSON.parse(beforeBytes)
assert.equal(prepared.task, taskId)
assert.equal(prepared.candidateSha256, candidate.manifestSha256)
assert.equal(prepared.matrixSha256, digest(matrixBytes))
assert.equal(prepared.beforeSha256, digest(beforeBytes))
assert.equal(prepared.runnerSha256, digest(await readFile(new URL('./prepare-development-fixture.mjs', import.meta.url))))
assert.equal(prepared.application, application)
assert.equal(digest(await readFile(join(application, '.ngb/packages.json'))), prepared.managedPackagesSha256)
const managedBefore = await managedPackageFiles(application)
const inventory = async () => Object.fromEntries(Object.entries(await applicationFiles(application))
  .map(([path, bytes]) => [path, digest(bytes)]).sort(([left], [right]) => left.localeCompare(right, 'en')))
assert.deepEqual(await inventory(), before, 'Start each attempt from a fresh prepared application.')
await assertConsumerIsolation(application, repository)
for (const path of Object.keys(before)) {
  assert.ok(!/(^|\/)\.env(?:\.|$)|(^|\/)\.codex\/|(^|\/)\.mcp\.json$|^\.claude\/settings/.test(path),
    'The application must not contain credentials or client settings.')
}
await mkdir(output, { mode: 0o700 })
const scratch = join(fixture, 'agent-scratch')
await mkdir(scratch, { mode: 0o700 })
await mkdir(join(scratch, 'tmp'))
const environment = executionEnvironment(scratch, { TMPDIR: join(scratch, 'tmp') })
const agentEnvironment = Object.fromEntries(['HOME', 'USER', 'LOGNAME', 'LANG', 'LC_ALL', 'TERM']
  .filter(name => process.env[name] !== undefined).map(name => [name, process.env[name]]))
for (const [name, value] of Object.entries(environment)) {
  if (!['HOME', 'USERPROFILE'].includes(name)) agentEnvironment[name] = value
}
const abort = new AbortController()
const cancel = () => abort.abort()
process.once('SIGINT', cancel)
process.once('SIGTERM', cancel)
const exec = promisify(execFile)
const options = { cwd: application, env: agentEnvironment, encoding: 'utf8', signal: abort.signal,
  timeout: 15000, maxBuffer: 1024 * 1024 }
const inputs = {
  version: candidate.version,
  artifactManifestSha256: candidate.manifestSha256,
  artifactsSha256: Object.fromEntries(Object.entries(candidate.files).map(([path, bytes]) => [path, digest(bytes)])),
  kitSha256: prepared.kitSha256,
  matrixSha256: digest(matrixBytes),
  profileSha256: digest(profileBytes),
  oracleSha256: digest(oracleBytes),
  runnerSha256: digest(runnerBytes),
  outputRedactorSha256: digest(redactorBytes),
  fixtureSha256: digest(fixtureBytes),
  fixturePreparerSha256: prepared.runnerSha256,
  beforeSha256: digest(beforeBytes),
  managedPackagesSha256: prepared.managedPackagesSha256,
  validationSha256: prepared.validationSha256,
}
const prefix = `attempts/codex/${workingDirectory}/${taskId}`
const report = { schemaVersion: 1, scope: 'real-agent-development', status: 'not-ready',
  profile: profile.id, inputs, application, startedAt: new Date().toISOString(), attempts: [] }
const save = async (path, value) => {
  const bytes = Buffer.isBuffer(value) ? value : typeof value === 'string' ? value : `${JSON.stringify(value, null, 2)}\n`
  await mkdir(dirname(join(output, path)), { recursive: true })
  await writeFile(join(output, path), bytes, { mode: 0o600, flag: 'wx' })
  return { path, sha256: digest(bytes) }
}
const invoke = async (name, tool, args, cwd = application) => {
  const result = await runTool(tool, args, { cwd, env: environment, signal: abort.signal,
    timeoutMs: 600000, maxBytes: 8 * 1024 * 1024 })
  await save(`preparation/${name}.log`, redactAgentOutput(result.output))
  assert.equal(result.state, 'passed', `Evaluation preparation failed: ${name}.`)
  return result.output
}

// Keep stdout and stderr separate. Kill the process group on cancellation,
// deadline or excessive output, so a timed-out client cannot leave build tools.
async function runAgent(args, cwd) {
  const stdoutJournal = openSync(join(output, `${prefix}/live-transcript.jsonl`), 'wx', 0o600)
  let stderrJournal
  try {
    stderrJournal = openSync(join(output, `${prefix}/live-stderr.log`), 'wx', 0o600)
  } catch (error) {
    closeSync(stdoutJournal)
    throw error
  }
  return await new Promise(resolveResult => {
    const child = spawn(executable, args, { cwd, env: agentEnvironment, detached: true,
      shell: false, stdio: ['ignore', 'pipe', 'pipe'] })
    const stdout = []
    const stderr = []
    const stdoutRedactor = createAgentOutputRedactor()
    const stderrRedactor = createAgentOutputRedactor()
    let bytes = 0
    let failure = null
    const stop = reason => {
      failure ??= reason
      if (!child.pid) return
      try {
        process.kill(-child.pid, 'SIGKILL')
      } catch (error) {
        if (error.code !== 'ESRCH') child.kill('SIGKILL')
      }
    }
    const persist = (target, journal, text) => {
      const chunk = Buffer.from(text)
      let offset = 0
      while (offset < chunk.length) offset += writeSync(journal, chunk, offset)
      target.push(chunk)
    }
    const collect = (target, journal, redactor, chunk) => {
      bytes += chunk.length
      if (bytes > 32 * 1024 * 1024) stop('output-limit')
      else {
        try {
          persist(target, journal, redactor.write(chunk))
        } catch {
          stop('journal-write-failed')
        }
      }
    }
    const onAbort = () => stop('cancelled')
    const timer = setTimeout(() => stop('timeout'), profile.maximumSecondsPerAttempt * 1000)
    abort.signal.addEventListener('abort', onAbort, { once: true })
    child.stdout.on('data', chunk => collect(stdout, stdoutJournal, stdoutRedactor, chunk))
    child.stderr.on('data', chunk => collect(stderr, stderrJournal, stderrRedactor, chunk))
    child.on('error', error => { failure = error.code ?? 'spawn-error' })
    child.on('close', (exitCode, signal) => {
      clearTimeout(timer)
      abort.signal.removeEventListener('abort', onAbort)
      try {
        persist(stdout, stdoutJournal, stdoutRedactor.end())
        persist(stderr, stderrJournal, stderrRedactor.end())
      } catch {
        failure ??= 'journal-write-failed'
      }
      closeSync(stdoutJournal)
      closeSync(stderrJournal)
      resolveResult({ exitCode, failure: failure ?? signal,
        stdout: Buffer.concat(stdout).toString('utf8'), stderr: Buffer.concat(stderr).toString('utf8') })
    })
    if (abort.signal.aborted) onAbort()
  })
}

try {
  const personalPath = join(homedir(), '.codex/AGENTS.md')
  const personalBytes = await readFile(personalPath)
  assert.equal(digest(personalBytes), profile.personalInstructionsSha256,
    'Personal guidance changed; freeze a reviewed profile before inference.')
  for (const path of [join(homedir(), '.codex/AGENTS.override.md'), join(homedir(), '.agents/skills')]) {
    try {
      const details = await lstat(path)
      assert.ok(details.isDirectory() && (await readdir(path)).length === 0, 'Unexpected personal instructions.')
    } catch (error) {
      if (error.code !== 'ENOENT') throw error
    }
  }
  const systemRoot = join(homedir(), '.codex/skills/.system')
  const skillNames = (await readdir(systemRoot, { withFileTypes: true })).filter(entry => entry.isDirectory())
    .map(entry => entry.name).sort()
  assert.deepEqual(skillNames, [...profile.disabledPersonalSkills].sort(), 'Personal skill inventory changed.')
  assert.deepEqual((await readdir(join(homedir(), '.codex/skills'))).sort(), ['.system', 'codex-primary-runtime'])
  assert.deepEqual(await readdir(join(homedir(), '.codex/skills/codex-primary-runtime')), [])
  for (let parent = fixture; ; parent = dirname(parent)) {
    for (const name of ['AGENTS.md', 'AGENTS.override.md']) {
      await assert.rejects(lstat(join(parent, name)), { code: 'ENOENT' }, 'Unexpected ancestor instructions.')
    }
    if (dirname(parent) === parent) break
  }
  const version = (await exec(executable, ['--version'], options)).stdout.trim()
  assert.equal(version, `codex-cli ${profile.clientVersion}`, 'The frozen client version changed.')
  const authentication = await exec(executable, ['login', 'status'], options)
  assert.match(authentication.stdout + authentication.stderr, /Logged in using ChatGPT/)
  const binaryHash = createHash('sha256')
  for await (const chunk of createReadStream(executable)) binaryHash.update(chunk)
  report.client = { version: profile.clientVersion, sha256: binaryHash.digest('hex') }
  report.authentication = { method: 'chatgpt' }
  await save('frozen/profile.json', profileBytes)
  await save('frozen/matrix.json', matrixBytes)
  await save('frozen/runner.mjs', runnerBytes)
  await save('frozen/oracle.mjs', oracleBytes)
  await save('frozen/personal-instructions.md', personalBytes)
  await save('frozen/fixture.json', fixtureBytes)
  await save('frozen/before.json', beforeBytes)
  for (const [path, bytes] of Object.entries(candidate.files).filter(([path]) => path.endsWith('.nupkg'))) {
    assert.equal(digest(await readFile(join(fixture, 'feed', basename(path)))), digest(bytes),
      'The prepared feed differs from the exact candidate.')
  }
  assert.equal(digest(await readFile(join(fixture, 'ui.tgz'))), digest(candidate.files[candidate.ui]))
  assert.equal((await invoke('dotnet-version', 'dotnet', ['--version'])).trim(), '10.0.401')
  const properties = ['-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false']
  await invoke('restore', 'dotnet', ['restore', 'EquipmentInspection.slnx', '--locked-mode',
    '--configfile', 'NuGet.Local.Config', ...properties])
  const validation = JSON.parse(await invoke('validate', 'node', ['infrastructure/ngb.mjs', 'validate', '--format', 'json']))
  assert.equal(validation.status, 'passed', 'The initial CLI inspection must pass before inference.')
  if (requiresFrontendAcceptance(taskId)) {
    await invoke('frontend-cache', 'npm', ['cache', 'add', join(fixture, 'ui.tgz'), '--ignore-scripts'], join(application, 'web'))
    await invoke('frontend-install', 'npm', ['ci', '--ignore-scripts', '--no-audit', '--no-fund'], join(application, 'web'))
  }
  assert.deepEqual(await inventory(), before, 'Dependency preparation changed source inputs.')
  const dotnetRoot = await realpath(dirname((await exec('/usr/bin/which', ['dotnet'], options)).stdout.trim()))
  const nodeRoot = dirname(dirname(await realpath(process.execPath)))
  const preflightPath = join(fixture, 'sandbox-preflight.mjs')
  await writeFile(preflightPath, `import assert from 'node:assert/strict'
import { closeSync, openSync, readFileSync, unlinkSync, writeFileSync } from 'node:fs'
import { execFileSync } from 'node:child_process'
const [application, scratch, denied, repository] = process.argv.slice(2)
assert.ok(readFileSync(application + '/EquipmentInspection.slnx').length > 0)
writeFileSync(application + '/.evaluation-write-probe', 'synthetic')
unlinkSync(application + '/.evaluation-write-probe')
writeFileSync(scratch + '/write-probe', 'synthetic')
for (const path of [denied, repository + '/AGENTS.md']) {
  assert.throws(() => readFileSync(path), 'Source outside the application must be denied: ' + path)
}
assert.throws(() => {
  const file = openSync(application + '/ai/vendor/instructions.md', 'r+')
  closeSync(file)
}, 'Managed guidance must be read-only.')
assert.equal(execFileSync('dotnet', ['--version'], { encoding: 'utf8' }).trim(), '10.0.401')
assert.equal(process.version, 'v24.19.0')
execFileSync('dotnet', ['build', application + '/EquipmentInspection.slnx', '--no-restore', '-c', 'Release',
  '-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false'],
{ cwd: application, timeout: 120000, maxBuffer: 8 * 1024 * 1024 })
console.log('Native sandbox boundary and pinned toolchains passed.')
`, { flag: 'wx', mode: 0o400 })
  const filesystem = {
    ':root': 'deny', ':minimal': 'read', [homedir()]: 'deny',
    '/private/tmp': 'deny', '/private/var/folders': 'deny', '/System/Library/OpenSSL': 'read',
    [application]: 'write', [scratch]: 'write', [join(fixture, 'feed')]: 'read',
    [nodeRoot]: 'read', [dotnetRoot]: 'read', [preflightPath]: 'read',
    [join(scratch, 'nuget')]: 'read',
    [join(application, '.ngb/nuget')]: 'read', [join(application, '.ngb/packages.json')]: 'read',
    [join(application, 'NuGet.Local.Config')]: 'read', [join(application, 'web/.ngb-packages')]: 'read',
  }
  for (const path of ['ai', '.agents', '.claude', '.codex', '.git', 'AGENTS.md', 'CLAUDE.md']) {
    filesystem[join(application, path)] = 'read'
  }
  const settings = [
    'approval_policy="never"', 'default_permissions="development"', 'web_search="disabled"',
    `model_reasoning_effort=${JSON.stringify(profile.reasoningEffort)}`,
    `project_root_markers=${JSON.stringify(profile.projectRootMarkers)}`,
    `permissions.development.filesystem={${Object.entries(filesystem).map(([path, mode]) =>
      `${JSON.stringify(path)}=${JSON.stringify(mode)}`).join(',')}}`,
    'permissions.development.network.enabled=false', 'allow_login_shell=false',
    'shell_environment_policy.inherit="core"', 'shell_environment_policy.ignore_default_excludes=false',
    `shell_environment_policy.set={${Object.entries(agentEnvironment)
      .filter(([name]) => !['HOME', 'USER', 'LOGNAME', 'LANG', 'LC_ALL', 'TERM'].includes(name))
      .map(([name, value]) => `${JSON.stringify(name)}=${JSON.stringify(value)}`).join(',')}}`,
    'analytics.enabled=false', 'otel.metrics_exporter="none"',
    `skills.config=[${skillNames.map(name => `{path=${JSON.stringify(join(systemRoot, name))},enabled=false}`).join(',')}]`,
  ]
  const cwd = workingDirectory === 'root' ? application : join(application, 'EquipmentInspection.Api')
  const preflightArgs = ['sandbox', '--include-managed-config', '-P', 'development', '-C', cwd,
    ...profile.disabledFeatures.flatMap(name => ['--disable', name]),
    ...settings.flatMap(value => ['-c', value]), '--', process.execPath, preflightPath,
    application, scratch, join(fixture, 'before.json'), repository]
  await save('preparation/sandbox-configuration.json', { args: preflightArgs, filesystem })
  try {
    const preflight = await exec(executable, preflightArgs, { ...options, cwd, timeout: 180000 })
    await save('preparation/sandbox.log', redactAgentOutput(preflight.stdout + preflight.stderr))
  } catch (error) {
    await save('preparation/sandbox.log', redactAgentOutput((error.stdout ?? '') + (error.stderr ?? '')))
    throw new Error(`Native sandbox preflight failed (${error.code ?? error.signal}); see the saved log.`)
  }
  assert.deepEqual(await inventory(), before, 'Sandbox preparation changed application source.')
  if (preflightOnly) {
    report.status = 'preflight-passed-not-evaluated'
  } else {
    const context = [
      `Evaluation environment: the application root is ${application}.`,
      'Exact public candidate dependencies are already restored.',
      'Use the supplied PATH, DOTNET_CLI_HOME, NUGET_PACKAGES and TMPDIR; do not replace them.',
      'Build the complete EquipmentInspection.slnx with -c Release --no-restore -m:1 -nodeReuse:false -p:UseSharedCompilation=false.',
      'Shell network and Docker are unavailable in this coding profile.',
      'VSTest requires a local listening socket. This editing sandbox denies that operation. Build the tests here and leave execution to independent acceptance.',
      'The application CLI test profiles create fresh isolated environments and may restore packages or require Docker; they are not executable in this offline editing profile.',
      'Independent HTTP/provider/browser acceptance and the authored tests run after source review.',
      'You may inspect installed public package files in NUGET_PACKAGES.',
      'Keep installed guidance and existing build/dependency inputs unchanged.',
      'Work without questions or human assistance.',
    ].join(' ')
    const prompt = `${task.prompt}\n\n${context}`
    const args = ['exec', '--ignore-user-config', '--ignore-rules', '--ephemeral', '--skip-git-repo-check',
      '--model', profile.model,
      '--color', 'never', '--json', '--cd', cwd,
      ...profile.disabledFeatures.flatMap(name => ['--disable', name]),
      ...settings.flatMap(value => ['-c', value]), prompt]
    const configuration = { args, cwd,
      environment: JSON.parse(redactAgentOutput(JSON.stringify(agentEnvironment))), profileSha256: inputs.profileSha256,
      clientSha256: report.client.sha256, promptSha256: digest(prompt) }
    const configurationArtifact = await save(`${prefix}/configuration.json`, configuration)
    await save(`${prefix}/prompt.txt`, prompt)
    const attempt = { client: 'codex', clientVersion: profile.clientVersion, provider: 'openai', model: profile.model,
      workingDirectory, task: taskId, attempt: 1, state: 'not-run', platform: process.platform, architecture: process.arch,
      humanAssistance: matrix.humanAssistance, configurationSha256: digest(JSON.stringify(configuration)),
      oracleSha256: inputs.oracleSha256, profileSha256: inputs.profileSha256, runnerSha256: inputs.runnerSha256,
      configuration: configurationArtifact,
      outputRedactorSha256: inputs.outputRedactorSha256,
      redactorSource: await save(`${prefix}/redactor.mjs`, redactorBytes),
      profileSource: await save(`${prefix}/profile.json`, profileBytes),
      runnerSource: await save(`${prefix}/runner.mjs`, runnerBytes) }
    report.attempts.push(attempt)
    await save(`${prefix}/started.json`, { ...attempt, state: 'running', inputs, startedAt: new Date().toISOString() })
    const started = performance.now()
    console.log(`Evaluating Codex ${taskId} from ${workingDirectory}; one bounded attempt.`)
    const result = await runAgent(args, cwd)
    attempt.elapsedSeconds = (performance.now() - started) / 1000
    attempt.exitCode = result.exitCode
    attempt.transcript = await save(`${prefix}/transcript.jsonl`, result.stdout)
    await save(`${prefix}/stderr.log`, result.stderr)
    const after = await inventory()
    const producedFiles = await applicationFiles(application)
    for (const [path, bytes] of Object.entries(producedFiles)) {
      if (after[path] !== before[path]) await save(`${prefix}/produced/${path}`, bytes)
    }
    attempt.producedTreeSha256 = digest(JSON.stringify(Object.entries(after).sort()))
    attempt.fixtureManifest = await save(`${prefix}/fixture.json`, { inputs, before, after })
    attempt.diff = await save(`${prefix}/diff.json`, {
      added: Object.keys(after).filter(path => !Object.hasOwn(before, path)),
      removed: Object.keys(before).filter(path => !Object.hasOwn(after, path)),
      modified: Object.keys(before).filter(path => Object.hasOwn(after, path) && before[path] !== after[path]),
    })
    try {
      assert.ok(result.exitCode === 0 && !result.failure, `Agent process failed: ${result.failure ?? result.exitCode}.`)
      const events = result.stdout.trim().split('\n').map(line => JSON.parse(line))
      const completed = events.findLast(event => event.type === 'turn.completed')
      assert.ok(completed && !events.some(event => event.type === 'turn.failed'), 'Missing successful client output.')
      attempt.usage = completed.usage
      assert.ok(Number.isInteger(completed.usage.input_tokens) && Number.isInteger(completed.usage.output_tokens)
        && completed.usage.input_tokens + completed.usage.output_tokens <= profile.maximumReportedTokensPerAttempt,
      'Reported usage exceeded the frozen token acceptance ceiling.')
      assert.ok(Number.isInteger(completed.usage.cached_input_tokens) && completed.usage.cached_input_tokens >= 0
        && completed.usage.cached_input_tokens <= completed.usage.input_tokens
        && completed.usage.input_tokens - completed.usage.cached_input_tokens + completed.usage.output_tokens
          <= profile.maximumUncachedInputAndOutputTokensPerAttempt,
      'Uncached input and output usage exceeded the frozen acceptance ceiling.')
      const items = events.filter(event => event.type === 'item.completed').map(event => event.item)
      assert.ok(items.every(item => ['agent_message', 'reasoning', 'command_execution', 'file_change', 'todo_list'].includes(item.type)),
        'An unexpected external tool was invoked.')
      attempt.toolTypes = [...new Set(items.map(item => item.type))]
      await save(`${prefix}/answer.md`, codexFinalAnswer(events))
      assert.equal(digest(await readFile(personalPath)), profile.personalInstructionsSha256)
      assert.equal(digest(await readFile(join(application, '.ngb/packages.json'))), prepared.managedPackagesSha256)
      assert.deepEqual(await managedPackageFiles(application), managedBefore, 'Managed candidate packages changed.')
      attempt.state = 'completed-awaiting-independent-acceptance'
    } catch (error) {
      attempt.state = 'failed'
      attempt.failure = error.message
    }
    await save(`${prefix}/attempt.json`, attempt)
    console.log(`Codex ${taskId}: ${attempt.state}.`)
    if (attempt.state === 'failed') process.exitCode = 2
  }
} catch (error) {
  report.failure = error.message
  process.exitCode = 1
} finally {
  process.removeListener('SIGINT', cancel)
  process.removeListener('SIGTERM', cancel)
  report.completedAt = new Date().toISOString()
  await save('development.json', report)
  console.log(`Codex development evaluation: ${report.status}. ${report.failure ?? ''}`)
}
