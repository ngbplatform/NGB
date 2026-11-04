import assert from 'node:assert/strict'
import { execFile } from 'node:child_process'
import { createHash } from 'node:crypto'
import { createReadStream } from 'node:fs'
import { lstat, mkdir, mkdtemp, readFile, readdir, realpath, rm, writeFile } from 'node:fs/promises'
import { homedir, tmpdir } from 'node:os'
import { basename, dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { promisify } from 'node:util'
import { loadCandidate } from '../../packaging/templates/content/infrastructure/ngb/application.mjs'
import { verifyCliArchive } from '../../packaging/templates/content/infrastructure/ngb/archive.mjs'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { assetPaths, readBytes } from '../../packaging/templates/content/infrastructure/ngb/kit-files.mjs'
import { executionEnvironment, runTool } from '../../packaging/templates/content/infrastructure/ngb/process.mjs'
import { assertConsumerIsolation } from '../external-consumer/contracts.mjs'
import { responseSchema, verifySkillDiscovery } from './evaluation-oracles/skill-discovery.mjs'
import { redactAgentOutput } from './agent-output.mjs'

assert.equal(process.argv.length, 5,
  'Usage: node evaluate-claude-discovery.mjs <sealed-candidate> <new-evidence-directory> <installed-claude-binary>')
const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const candidate = await loadCandidate(resolve(process.argv[2]))
const output = resolve(process.argv[3])
const executable = await realpath(resolve(process.argv[4]))
const matrixBytes = await readFile(new URL('./agent-evaluations.json', import.meta.url))
const profileBytes = await readFile(new URL('./claude-discovery-profile.json', import.meta.url))
const oracleBytes = await readFile(new URL('./evaluation-oracles/skill-discovery.mjs', import.meta.url))
const runnerBytes = await readFile(fileURLToPath(import.meta.url))
const redactorBytes = await readFile(new URL('./agent-output.mjs', import.meta.url))
const matrix = JSON.parse(matrixBytes)
const profile = JSON.parse(profileBytes)
const task = matrix.tasks.find(value => value.id === 'skill-discovery')
assert.equal(process.platform, matrix.platform)
assert.equal(process.arch, matrix.architecture)
assert.equal(candidate.version, matrix.version)
assert.ok(task && profile.maximumSecondsPerAttempt <= matrix.maximumSecondsPerAttempt)
await mkdir(output, { mode: 0o700 })
const stage = await realpath(await mkdtemp(join(tmpdir(), 'ngb-claude-discovery-')))
const buildEnvironment = executionEnvironment(join(stage, 'build-home'))
const agentEnvironment = Object.fromEntries(['HOME', 'USER', 'LOGNAME', 'PATH', 'TMPDIR', 'LANG', 'LC_ALL', 'TERM']
  .filter(name => process.env[name] !== undefined).map(name => [name, process.env[name]]))
Object.assign(agentEnvironment, profile.environment)
const abort = new AbortController()
const cancel = () => abort.abort()
process.once('SIGINT', cancel)
process.once('SIGTERM', cancel)
const exec = promisify(execFile)
const cli = verifyCliArchive(candidate.files[candidate.cli], candidate.version)
const inputs = {
  version: candidate.version,
  matrixSha256: digest(matrixBytes),
  profileSha256: digest(profileBytes),
  oracleSha256: digest(oracleBytes),
  runnerSha256: digest(runnerBytes),
  outputRedactorSha256: digest(redactorBytes),
  templateSha256: digest(candidate.files[candidate.template]),
  kitSha256: digest(cli.files.get('package/infrastructure/ngb/kit-payload.json')),
  artifactManifestSha256: candidate.manifestSha256,
  artifactsSha256: Object.fromEntries(Object.entries(candidate.files).map(([path, bytes]) => [path, digest(bytes)])),
}
const report = { schemaVersion: 1, scope: 'real-agent-skill-discovery', status: 'not-ready',
  profile: profile.id, inputs, stage, startedAt: new Date().toISOString(), attempts: [] }
const save = async (path, value) => {
  const bytes = Buffer.isBuffer(value) ? value : typeof value === 'string' ? value : `${JSON.stringify(value, null, 2)}\n`
  await mkdir(dirname(join(output, path)), { recursive: true })
  await writeFile(join(output, path), bytes, { mode: 0o600, flag: 'wx' })
  return { path, sha256: digest(bytes) }
}
const snapshot = async root => {
  const files = {}
  for (const path of await assetPaths(dirname(root), basename(root))) {
    const bytes = await readBytes(dirname(root), path, 16 * 1024 * 1024)
    assert.ok(bytes, 'A discovery fixture file disappeared.')
    files[path.slice(basename(root).length + 1)] = digest(bytes)
  }
  return files
}
const dotnet = async (id, args) => {
  const result = await runTool('dotnet', args, { cwd: stage, env: buildEnvironment,
    signal: abort.signal, timeoutMs: 120000, maxBytes: 4 * 1024 * 1024 })
  await save(`${id}.log`, redactAgentOutput(result.output))
  assert.equal(result.state, 'passed', `Fixture ${id} failed; see its saved log.`)
}
try {
  // Personal instructions would contaminate this profile. Detect their presence
  // without reading their contents or copying any credentials into the fixture.
  for (const name of ['CLAUDE.md', 'skills', 'commands', 'agents']) {
    const path = join(homedir(), '.claude', name)
    try {
      const details = await lstat(path)
      assert.ok(details.isDirectory() && (await readdir(path)).length === 0,
        `Personal .claude/${name} is present; define an isolated evaluation profile before proceeding.`)
    } catch (error) {
      if (error.code !== 'ENOENT') throw error
    }
  }
  const options = { cwd: stage, env: agentEnvironment, encoding: 'utf8', signal: abort.signal,
    timeout: 15000, maxBuffer: 1024 * 1024 }
  const version = (await exec(executable, ['--version'], options)).stdout.trim()
  assert.equal(version, `${profile.clientVersion} (Claude Code)`, 'The frozen client version changed.')
  const authentication = JSON.parse((await exec(executable, ['auth', 'status'], options)).stdout)
  assert.ok(authentication.loggedIn && authentication.authMethod === 'claude.ai',
    'This profile requires an existing Claude subscription login, not a separate API billing credential.')
  report.authentication = { method: authentication.authMethod, subscriptionType: authentication.subscriptionType }
  const binaryHash = createHash('sha256')
  for await (const chunk of createReadStream(executable)) binaryHash.update(chunk)
  report.client = { version: profile.clientVersion, sha256: binaryHash.digest('hex') }
  const template = join(stage, `NGB.Platform.Templates.${candidate.version}.nupkg`)
  const hive = join(stage, 'template-hive')
  await writeFile(template, candidate.files[candidate.template], { flag: 'wx', mode: 0o400 })
  await save('inputs.json', { ...inputs, task, profile, client: report.client })
  await save('frozen/profile.json', profileBytes.toString('utf8'))
  await save('frozen/oracle.mjs', oracleBytes.toString('utf8'))
  await save('frozen/runner.mjs', runnerBytes.toString('utf8'))
  await save('frozen/matrix.json', matrixBytes.toString('utf8'))
  await dotnet('install', ['new', 'install', template, '--debug:custom-hive', hive])
  for (const workingDirectory of matrix.workingDirectories) {
    abort.signal.throwIfAborted()
    const root = join(stage, workingDirectory)
    await dotnet(`create-${workingDirectory}`, ['new', 'ngb', '-n', 'KitDiscovery', '-o', root, '--debug:custom-hive', hive])
    await assertConsumerIsolation(root, repository)
    const before = await snapshot(root)
    assert.ok(!Object.keys(before).some(path => /(^|\/)\.env(?:\.|$)/.test(path)), 'A discovery fixture must not contain credentials.')
    for (const path of ['.claude/settings.json', '.claude/settings.local.json', '.mcp.json']) {
      assert.ok(!Object.hasOwn(before, path), 'The frozen fixture must not introduce agent settings or hooks.')
    }
    const cwd = workingDirectory === 'root' ? root : join(root, 'KitDiscovery.Api')
    const args = ['--print', '--verbose', '--output-format', 'stream-json',
      '--json-schema', JSON.stringify(responseSchema), '--max-turns', String(profile.maximumTurnsPerAttempt),
      '--max-budget-usd', String(profile.maximumEstimatedCostUsdPerAttempt),
      '--tools', profile.tools.join(','), '--allowedTools', profile.tools.join(','),
      '--permission-mode', profile.permissionMode, '--permission-prompts', 'none',
      '--strict-mcp-config', '--mcp-config', '{"mcpServers":{}}', '--setting-sources', profile.settingSources.join(','),
      '--settings', JSON.stringify(profile.settings), '--no-chrome', '--no-session-persistence',
      '--add-dir', root, '--', task.prompt]
    const configuration = { args, cwd, environmentKeys: Object.keys(agentEnvironment).sort(),
      profileSha256: inputs.profileSha256, clientSha256: report.client.sha256 }
    const prefix = `attempts/claude/${workingDirectory}/${task.id}`
    const configurationArtifact = await save(`${prefix}/configuration.json`, configuration)
    const attempt = { client: 'claude', clientVersion: profile.clientVersion, provider: 'anthropic',
      workingDirectory, task: task.id, attempt: 1, state: 'not-run', platform: process.platform,
      architecture: process.arch, humanAssistance: matrix.humanAssistance,
      configurationSha256: digest(JSON.stringify(configuration)), oracleSha256: inputs.oracleSha256,
      profileSha256: inputs.profileSha256, runnerSha256: inputs.runnerSha256, configuration: configurationArtifact,
      outputRedactorSha256: inputs.outputRedactorSha256,
      redactorSource: await save(`${prefix}/redactor.mjs`, redactorBytes),
      profileSource: await save(`${prefix}/profile.json`, profileBytes.toString('utf8')),
      runnerSource: await save(`${prefix}/runner.mjs`, runnerBytes.toString('utf8')) }
    report.attempts.push(attempt)
    let stdout = ''
    let stderr = ''
    const started = performance.now()
    console.log(`Evaluating Claude skill discovery from ${workingDirectory}; one bounded attempt.`)
    try {
      try {
        const running = exec(executable, args, { ...options, cwd,
          timeout: profile.maximumSecondsPerAttempt * 1000, maxBuffer: 16 * 1024 * 1024 })
        running.child.stdin.end()
        const result = await running
        stdout = redactAgentOutput(result.stdout)
        stderr = redactAgentOutput(result.stderr)
        attempt.exitCode = 0
      } catch (error) {
        stdout = redactAgentOutput(error.stdout ?? '')
        stderr = redactAgentOutput(error.stderr ?? '')
        attempt.exitCode = error.code ?? null
        throw new Error(`Claude did not complete successfully (${error.signal ?? error.code ?? 'cancelled'}).`)
      }
      const events = stdout.trim().split('\n').map(line => JSON.parse(line))
      const initialized = events.find(event => event.type === 'system' && event.subtype === 'init')
      const result = events.findLast(event => event.type === 'result')
      assert.ok(initialized && result && result.subtype === 'success' && !result.is_error, 'Missing successful client output.')
      attempt.model = initialized.model
      attempt.estimatedCostUsd = result.total_cost_usd
      attempt.usage = result.usage
      attempt.discoveredSkills = initialized.skills
      attempt.answer = result.structured_output
      assert.deepEqual(initialized.mcp_servers, [], 'Unexpected MCP servers contaminated the attempt.')
      assert.ok(initialized.skills.includes('ngb-documents'), 'Claude did not natively discover the document skill.')
      const calls = events.flatMap(event => event.type === 'assistant' ? event.message.content : [])
        .filter(item => item.type === 'tool_use')
      assert.ok(calls.every(call => [...profile.tools, 'StructuredOutput'].includes(call.name)), 'An unexpected tool was invoked.')
      assert.ok(Number.isFinite(result.total_cost_usd) && result.total_cost_usd <= profile.maximumEstimatedCostUsdPerAttempt,
        'The reported client-side cost estimate exceeded the frozen budget.')
      attempt.toolNames = [...new Set(calls.map(call => call.name))]
      const after = await snapshot(root)
      attempt.producedTreeSha256 = digest(JSON.stringify(after))
      attempt.acceptance = verifySkillDiscovery({ answer: attempt.answer, before, after, version: candidate.version, client: 'claude' })
      attempt.state = 'passed'
    } catch (error) {
      attempt.state = 'failed'
      attempt.failure = error.message
    } finally {
      attempt.elapsedSeconds = (performance.now() - started) / 1000
      const after = await snapshot(root)
      attempt.producedTreeSha256 = digest(JSON.stringify(after))
      attempt.fixtureManifest = await save(`${prefix}/fixture.json`, { inputs, before, after })
      attempt.diff = await save(`${prefix}/diff.json`, {
        added: Object.keys(after).filter(path => !Object.hasOwn(before, path)),
        removed: Object.keys(before).filter(path => !Object.hasOwn(after, path)),
        modified: Object.keys(before).filter(path => Object.hasOwn(after, path) && before[path] !== after[path]),
      })
      attempt.transcript = await save(`${prefix}/transcript.jsonl`, stdout)
      await save(`${prefix}/stderr.log`, stderr)
      attempt.acceptanceReport = await save(`${prefix}/acceptance.json`, { status: attempt.state,
        checks: attempt.acceptance ?? [], failure: attempt.failure, producedTreeSha256: attempt.producedTreeSha256,
        oracleSha256: inputs.oracleSha256, runnerSha256: inputs.runnerSha256 })
      await save(`${prefix}/attempt.json`, attempt)
      console.log(`Claude discovery ${workingDirectory}: ${attempt.state}.`)
    }
  }
  assert.ok(report.attempts.length === matrix.workingDirectories.length
    && report.attempts.every(attempt => attempt.state === 'passed'), 'One or more discovery attempts failed.')
  report.status = 'passed'
} catch (error) {
  report.failure = error.message
  process.exitCode = 1
} finally {
  process.removeListener('SIGINT', cancel)
  process.removeListener('SIGTERM', cancel)
  await rm(buildEnvironment.HOME, { recursive: true, force: true })
  report.completedAt = new Date().toISOString()
  await save('discovery.json', report)
  console.log(`Real Claude discovery: ${report.status}. Full agent evaluations remain a separate gate.`)
}
