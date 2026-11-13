import assert from 'node:assert/strict'
import { execFile, spawn } from 'node:child_process'
import { createHash } from 'node:crypto'
import { closeSync, createReadStream, openSync, writeSync } from 'node:fs'
import { lstat, mkdir, readFile, readdir, realpath, writeFile } from 'node:fs/promises'
import { homedir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { promisify } from 'node:util'
import { applicationFiles, loadCandidate } from '../../packaging/templates/content/infrastructure/ngb/application.mjs'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { verifyCliArchive } from '../../packaging/templates/content/infrastructure/ngb/archive.mjs'
import { executionEnvironment, runTool } from '../../packaging/templates/content/infrastructure/ngb/process.mjs'
import { assertConsumerIsolation, assertOutsideRepository } from '../external-consumer/contracts.mjs'
import { createAgentOutputRedactor, redactAgentOutput } from './agent-output.mjs'
import { upgradeLauncher } from './upgrade-launcher.mjs'

assert.ok(process.argv.length === 8 || (process.argv.length === 9 && process.argv[8] === '--preflight-only'),
  'Usage: node evaluate-claude-upgrade.mjs <task> <root|subdirectory> <candidate> <prepared-fixture> <new-output> <claude-binary> [--preflight-only]')
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
assert.equal(taskId, 'customized-kit-upgrade')
assert.ok(['root', 'subdirectory'].includes(workingDirectory))
assert.equal(process.version, 'v24.19.0', 'Use the frozen Node toolchain.')
const matrixBytes = await readFile(join(repository, 'quality/ai-devkit/agent-evaluations.json'))
const profileBytes = await readFile(new URL('./claude-upgrade-profile.json', import.meta.url))
const oracleBytes = await readFile(join(repository, `quality/ai-devkit/evaluation-oracles/${taskId}.mjs`))
const runnerBytes = await readFile(fileURLToPath(import.meta.url))
const redactorBytes = await readFile(new URL('./agent-output.mjs', import.meta.url))
const launcherBytes = await readFile(new URL('./upgrade-launcher.mjs', import.meta.url))
const matrix = JSON.parse(matrixBytes)
const profile = JSON.parse(profileBytes)
assert.equal(digest(launcherBytes), profile.installer.sourceSha256, 'The frozen installer source changed.')
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
assert.equal(prepared.runnerSha256, digest(await readFile(join(repository, 'quality/ai-devkit/prepare-upgrade-evaluation.mjs'))))
assert.equal(prepared.application, application)
const targetCli = await realpath(prepared.cliEntry)
assert.ok(targetCli.startsWith(join(fixture, 'target-cli') + '/'), 'Target CLI must remain inside the supplied fixture.')
const targetRoot = dirname(targetCli)
const targetFiles = await applicationFiles(targetRoot)
const expectedCli = verifyCliArchive(candidate.files[candidate.cli], candidate.version)
const expectedTarget = Object.fromEntries([...expectedCli.files]
  .filter(([path]) => path.startsWith('package/infrastructure/'))
  .map(([path, bytes]) => [path.slice('package/infrastructure/'.length), bytes]))
assert.deepEqual(targetFiles, expectedTarget, 'Supplied target CLI differs from the sealed archive.')
assert.equal(prepared.oracleSha256, digest(oracleBytes))
assert.equal(prepared.contractSha256, digest(await readFile(join(repository,
  'quality/ai-devkit/evaluation-fixtures/customized-kit-upgrade.contract.json'))))
assert.equal(prepared.previewSha256, digest(await readFile(join(fixture, 'preview.log'))))
const immutableInputs = async () => {
  assert.equal(digest(await readFile(join(fixture, 'fixture.json'))), digest(fixtureBytes))
  assert.equal(digest(await readFile(join(fixture, 'before.json'))), digest(beforeBytes))
  assert.deepEqual(await applicationFiles(targetRoot), targetFiles, 'Supplied target CLI changed.')
  assert.deepEqual((await loadCandidate(join(fixture, 'candidate'))).files, candidate.files,
    'The supplied candidate differs from the sealed package bytes.')
}
await immutableInputs()
const kitSha256 = digest(await readFile(join(targetRoot, 'ngb/kit-payload.json')))
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
Object.assign(agentEnvironment, profile.environment)
agentEnvironment.CLAUDE_CODE_TMPDIR = join(scratch, 'tmp')
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
  kitSha256,
  matrixSha256: digest(matrixBytes),
  profileSha256: digest(profileBytes),
  oracleSha256: digest(oracleBytes),
  runnerSha256: digest(runnerBytes),
  outputRedactorSha256: digest(redactorBytes),
  fixtureSha256: digest(fixtureBytes),
  fixturePreparerSha256: prepared.runnerSha256,
  beforeSha256: digest(beforeBytes),
  targetCliIntegrity: prepared.cliIntegrity,
  previewSha256: prepared.previewSha256,
}
const prefix = `attempts/claude/${workingDirectory}/${taskId}`
const report = { schemaVersion: 1, scope: 'real-agent-upgrade', status: 'not-ready',
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
  for (const name of ['CLAUDE.md', 'skills', 'commands', 'agents']) {
    try {
      const path = join(homedir(), '.claude', name)
      const details = await lstat(path)
      assert.ok(details.isDirectory() && (await readdir(path)).length === 0, 'Unexpected personal Claude guidance.')
    } catch (error) {
      if (error.code !== 'ENOENT') throw error
    }
  }
  for (let parent = fixture; ; parent = dirname(parent)) {
    for (const name of ['CLAUDE.md', 'CLAUDE.local.md', 'AGENTS.md', '.claude']) {
      await assert.rejects(lstat(join(parent, name)), { code: 'ENOENT' }, 'Unexpected ancestor instructions.')
    }
    if (dirname(parent) === parent) break
  }
  const version = (await exec(executable, ['--version'], options)).stdout.trim()
  assert.equal(version, `${profile.clientVersion} (Claude Code)`, 'The frozen client version changed.')
  const authentication = JSON.parse((await exec(executable, ['auth', 'status'], options)).stdout)
  assert.ok(authentication.loggedIn && authentication.authMethod === 'claude.ai',
    'Use the existing subscription login; do not inherit separate API credentials.')
  const binaryHash = createHash('sha256')
  for await (const chunk of createReadStream(executable)) binaryHash.update(chunk)
  report.client = { version: profile.clientVersion, sha256: binaryHash.digest('hex') }
  report.authentication = { method: authentication.authMethod, subscriptionType: authentication.subscriptionType }
  await save('frozen/profile.json', profileBytes)
  await save('frozen/matrix.json', matrixBytes)
  await save('frozen/runner.mjs', runnerBytes)
  await save('frozen/oracle.mjs', oracleBytes)
  await save('frozen/fixture.json', fixtureBytes)
  await save('frozen/before.json', beforeBytes)
  assert.equal((await invoke('dotnet-version', 'dotnet', ['--version'])).trim(), '10.0.401')
  assert.deepEqual(await inventory(), before, 'Preparation changed application source.')
  const dotnetRoot = await realpath(dirname((await exec('/usr/bin/which', ['dotnet'], options)).stdout.trim()))
  const nodeRoot = dirname(dirname(await realpath(process.execPath)))
  // Seatbelt protects missing denied files by locking their existing ancestors.
  // Create only the empty client directory before protecting settings beneath it;
  // no instructions, settings or skill content are added by preparation.
  await mkdir(join(application, '.claude'), { recursive: true })
  const preflightPath = join(fixture, 'sandbox-preflight.mjs')
  await writeFile(preflightPath, `import assert from 'node:assert/strict'
import { closeSync, existsSync, openSync, readFileSync, unlinkSync, writeFileSync, mkdirSync, rmdirSync } from 'node:fs'
import { execFileSync, spawnSync } from 'node:child_process'
const [application, scratch, denied, repository, cli] = process.argv.slice(2)
const { executionEnvironment, runTool } = await import(new URL('./ngb/process.mjs', 'file://' + cli))
assert.ok(readFileSync(application + '/CertificationApp.slnx').length > 0)
writeFileSync(application + '/.evaluation-write-probe', 'synthetic')
unlinkSync(application + '/.evaluation-write-probe')
const createdDirectories = []
for (const adapter of ['.agents', '.claude']) {
  for (const directory of [application + '/' + adapter, application + '/' + adapter + '/skills']) {
    if (!existsSync(directory)) {
      mkdirSync(directory)
      createdDirectories.push(directory)
    }
  }
  const adapterProbe = application + '/' + adapter + '/skills/.evaluation-directory-probe'
  mkdirSync(adapterProbe)
  writeFileSync(adapterProbe + '/probe', 'synthetic')
  unlinkSync(adapterProbe + '/probe')
  rmdirSync(adapterProbe)
}
for (const path of ['.claude/settings.json', '.claude/settings.local.json', '.mcp.json']) {
  assert.throws(() => writeFileSync(application + '/' + path, '{}'), 'Client settings must stay write-denied.')
}
for (const directory of createdDirectories.reverse()) rmdirSync(directory)
for (const path of [denied, repository + '/AGENTS.md']) assert.throws(() => readFileSync(path))
assert.throws(() => { const fd = openSync(cli, 'r+'); closeSync(fd) }, 'Target CLI must be read-only.')
for (const path of ['AGENTS.md', 'ai/vendor/instructions.md']) {
  const fd = openSync(application + '/' + path, 'r+')
  closeSync(fd)
}
const environment = executionEnvironment(scratch + '/probe-home', { TMPDIR: scratch + '/tmp' })
const allowed = execFileSync('/usr/bin/curl', ['--silent', '--show-error', '--fail', '--max-time', '20',
  'https://api.nuget.org/v3/index.json'], { env: environment, encoding: 'utf8', timeout: 25000 })
assert.ok(JSON.parse(allowed).resources.length)
const forbidden = spawnSync('/usr/bin/curl', ['--silent', '--show-error', '--fail', '--max-time', '5',
  'https://example.com'], { env: environment, encoding: 'utf8', timeout: 10000 })
assert.notEqual(forbidden.status, 0)
const direct = spawnSync('/usr/bin/curl', ['--silent', '--show-error', '--fail', '--max-time', '5',
  '--noproxy', '*', 'https://api.nuget.org/v3/index.json'], { env: environment, encoding: 'utf8', timeout: 10000 })
assert.notEqual(direct.status, 0, 'Direct egress must not bypass the allowlisted proxy.')
const project = scratch + '/probe-project'
mkdirSync(project)
writeFileSync(project + '/Probe.csproj', '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include="Newtonsoft.Json" Version="13.0.3" /></ItemGroup></Project>')
writeFileSync(project + '/NuGet.Config', '<configuration><packageSources><clear/><add key="public" value="https://api.nuget.org/v3/index.json"/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders></configuration>')
const restored = await runTool('dotnet', ['restore', 'Probe.csproj', '--configfile', 'NuGet.Config', '-m:1', '-nodeReuse:false'],
  { cwd: project, env: environment, timeoutMs: 120000 })
assert.equal(restored.state, 'passed', restored.output)
const npm = await runTool('npm', ['view', 'typescript@5.9.3', 'version', '--json', '--ignore-scripts'],
  { cwd: project, env: environment, timeoutMs: 60000 })
assert.equal(npm.state, 'passed', npm.output)
assert.equal(JSON.parse(npm.output), '5.9.3')
console.log('Installer filesystem boundary, both skill adapters, immutable target CLI and restricted transport passed.')
`, { flag: 'wx', mode: 0o400 })
  const protectedPaths = ['.git', '.codex', '.claude/settings.json', '.claude/settings.local.json', '.mcp.json']
    .map(path => join(application, path))
  const filesystem = {
    denyRead: ['/Users', '/Volumes', '/private/tmp', '/private/var/folders'],
    allowRead: [application, scratch, nodeRoot, dotnetRoot, preflightPath,
      join(fixture, 'target-cli'), join(fixture, 'candidate'), ...profile.runtimeDirectories],
    allowWrite: [application, scratch, ...profile.runtimeDirectories],
    denyWrite: [...protectedPaths, join(fixture, 'target-cli'), join(fixture, 'candidate')],
  }
  const settings = structuredClone(profile.settings)
  settings.sandbox.filesystem = { ...settings.sandbox.filesystem, ...filesystem }
  const automaticTools = profile.tools.filter(tool => !['Edit', 'Write'].includes(tool))
  settings.permissions.allow = [...automaticTools]
  for (const path of [application, scratch]) {
    for (const tool of ['Edit', 'Write']) settings.permissions.allow.push(`${tool}(/${path}/**)`)
  }
  for (const path of [...protectedPaths, join(fixture, 'target-cli'), join(fixture, 'candidate')]) {
    for (const tool of ['Edit', 'Write']) {
      settings.permissions.deny.push(`${tool}(/${path})`, `${tool}(/${path}/**)`)
    }
  }
  const cwd = workingDirectory === 'root' ? application : join(application, 'CertificationApp.Api')
  const sandboxRoot = join(repository, 'artifacts/certification-tools/anthropic-sandbox-0.0.81')
  const sandboxPackage = join(sandboxRoot, 'node_modules/@anthropic-ai/sandbox-runtime')
  const sandboxMetadata = JSON.parse(await readFile(join(sandboxPackage, 'package.json')))
  const sandboxLock = await readFile(join(sandboxRoot, 'package-lock.json'))
  const locked = JSON.parse(sandboxLock).packages['node_modules/@anthropic-ai/sandbox-runtime']
  assert.equal(sandboxMetadata.version, profile.sandboxPreflight.version)
  assert.equal(locked.version, profile.sandboxPreflight.version)
  assert.equal(locked.integrity, profile.sandboxPreflight.integrity)
  const sandboxCli = join(sandboxPackage, sandboxMetadata.bin.srt)
  report.sandboxPreflight = { version: sandboxMetadata.version, lockSha256: digest(sandboxLock),
    cliSha256: digest(await readFile(sandboxCli)) }
  const sandboxSettingsPath = join(fixture, 'sandbox-preflight.json')
  await writeFile(sandboxSettingsPath, JSON.stringify({ filesystem, network: settings.sandbox.network }, null, 2),
    { flag: 'wx', mode: 0o400 })
  const installerPath = join(fixture, 'apply-reviewed-upgrade')
  const resolutionPath = join(scratch, profile.installer.resolutionFile)
  const installerEnvironment = executionEnvironment(scratch, {
    TMPDIR: join(scratch, 'tmp'), CLAUDE_CODE_TMPDIR: join(scratch, 'tmp'),
  }, { PATH: environment.PATH })
  const installerSource = upgradeLauncher({ node: process.execPath, sandboxCli, settings: sandboxSettingsPath,
    cli: targetCli, application, candidate: join(fixture, 'candidate'), resolution: resolutionPath,
    environment: installerEnvironment })
  await writeFile(installerPath, installerSource, { flag: 'wx', mode: 0o500 })
  // An exact command only: no wildcard, interpreter exclusion or arbitrary args.
  // The launcher's own OS sandbox enforces the declared installation boundary.
  settings.sandbox.excludedCommands = [installerPath]
  settings.permissions.allow.push(`Bash(${installerPath})`)
  const boundedInstaller = { path: installerPath, resolutionPath, source: installerSource,
    generator: launcherBytes.toString('utf8'), generatorSha256: digest(launcherBytes),
    settings: JSON.parse(await readFile(sandboxSettingsPath)), environment: installerEnvironment,
    sandboxRuntime: report.sandboxPreflight }
  const verifyInstaller = async () => {
    assert.equal(await readFile(installerPath, 'utf8'), installerSource, 'The fixed installer changed.')
    assert.deepEqual(JSON.parse(await readFile(sandboxSettingsPath)), boundedInstaller.settings,
      'The installer sandbox policy changed.')
  }
  const preflightArgs = [sandboxCli, '--settings', sandboxSettingsPath, '--', process.execPath, preflightPath,
    application, scratch, join(fixture, 'before.json'), repository, targetCli]
  await save('preparation/sandbox-configuration.json', { args: preflightArgs, settings, filesystem, boundedInstaller })
  try {
    const preflight = await exec(process.execPath, preflightArgs, { ...options, cwd, timeout: 240000 })
    await save('preparation/sandbox.log', redactAgentOutput(preflight.stdout + preflight.stderr))
  } catch (error) {
    await save('preparation/sandbox.log', redactAgentOutput((error.stdout ?? '') + (error.stderr ?? '')))
    throw new Error(`Native sandbox preflight failed (${error.code ?? error.signal}); see the saved log.`)
  }
  assert.deepEqual(await inventory(), before, 'Sandbox preparation changed application source.')
  await verifyInstaller()
  if (preflightOnly) {
    report.status = 'preflight-passed-not-evaluated'
  } else {
    const context = `Evaluation environment: the application root is ${application}; the read-only target CLI is ${targetCli}; the read-only candidate directory is ${join(fixture, 'candidate')}. Keep resolution files and command logs in ${scratch}. Use the supplied PATH, DOTNET_CLI_HOME, NUGET_PACKAGES and TMPDIR. Preview with the target CLI normally. For apply, save your preview-bound resolution JSON at ${resolutionPath} and invoke exactly ${installerPath} with no arguments, shell prefix, directory change or redirection. This fixed launcher runs the same target CLI upgrade --apply for the supplied application and candidate in a separate restricted OS sandbox; it permits the authorized skill-adapter writes that the native Bash sandbox protects. It accepts no arbitrary command or environment. Both sandboxes permit only public NuGet/npm registry hosts; access to other destinations, Docker and the platform checkout is unavailable. Preserve the proxy transport for ordinary build commands. Independent acceptance runs afterward. Work without questions or human assistance.`
    const diagnostics = 'Keep diagnostics in agent-scratch. Inspect status, exit codes and the logs you write there. Leave .env files, client-private logs, credential locations and environment dumps out of the investigation. For build configuration use the supplied project files, package locks and CLI JSON. A directory outside the preview belongs to the application unless your own commands created it.'
    const prompt = `${task.prompt}\n\n${context} ${diagnostics}`
    const args = ['--print', '--verbose', '--output-format', 'stream-json',
      '--max-turns', String(profile.maximumTurnsPerAttempt),
      '--max-budget-usd', String(profile.maximumEstimatedCostUsdPerAttempt),
      '--tools', profile.tools.join(','), '--allowedTools', automaticTools.join(','),
      '--permission-mode', profile.permissionMode, '--permission-prompts', 'none',
      '--strict-mcp-config', '--mcp-config', '{"mcpServers":{}}', '--setting-sources', profile.settingSources.join(','),
      '--settings', JSON.stringify(settings), '--no-chrome', '--no-session-persistence',
      '--add-dir', application, '--add-dir', scratch, '--add-dir', join(fixture, 'target-cli'),
      '--add-dir', join(fixture, 'candidate'), '--', prompt]
    const configuration = { args, cwd, boundedInstaller,
      environment: JSON.parse(redactAgentOutput(JSON.stringify(agentEnvironment))), profileSha256: inputs.profileSha256,
      clientSha256: report.client.sha256, promptSha256: digest(prompt) }
    const configurationArtifact = await save(`${prefix}/configuration.json`, configuration)
    await save(`${prefix}/prompt.txt`, prompt)
    const attempt = { client: 'claude', clientVersion: profile.clientVersion, provider: 'anthropic',
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
    console.log(`Evaluating Claude ${taskId} from ${workingDirectory}; one bounded attempt.`)
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
      const initialized = events.find(event => event.type === 'system' && event.subtype === 'init')
      const completed = events.findLast(event => event.type === 'result')
      assert.ok(initialized && completed && completed.subtype === 'success' && !completed.is_error,
        'Missing successful client output.')
      attempt.model = initialized.model
      attempt.usage = completed.usage
      attempt.estimatedCostUsd = completed.total_cost_usd
      attempt.discoveredSkills = initialized.skills
      assert.deepEqual(initialized.mcp_servers, [], 'Unexpected MCP servers contaminated the attempt.')
      assert.ok(Number.isFinite(completed.total_cost_usd) && completed.total_cost_usd >= 0
        && completed.total_cost_usd <= profile.maximumEstimatedCostUsdPerAttempt,
      'Reported estimated cost exceeded the frozen acceptance ceiling.')
      const calls = events.flatMap(event => event.type === 'assistant' ? event.message.content : [])
        .filter(item => item.type === 'tool_use')
      assert.ok(calls.every(call => profile.tools.includes(call.name)), 'An unexpected external tool was invoked.')
      attempt.toolNames = [...new Set(calls.map(call => call.name))]
      await save(`${prefix}/answer.md`, completed.result ?? '')
      await immutableInputs()
      await verifyInstaller()
      attempt.state = 'completed-awaiting-independent-acceptance'
    } catch (error) {
      attempt.state = 'failed'
      attempt.failure = error.message
    }
    await save(`${prefix}/attempt.json`, attempt)
    console.log(`Claude ${taskId}: ${attempt.state}.`)
    if (attempt.state === 'failed') process.exitCode = 2
  }
} catch (error) {
  report.failure = error.message
  process.exitCode = 1
} finally {
  process.removeListener('SIGINT', cancel)
  process.removeListener('SIGTERM', cancel)
  report.completedAt = new Date().toISOString()
  await save('upgrade.json', report)
  console.log(`Claude upgrade evaluation: ${report.status}. ${report.failure ?? ''}`)
}
