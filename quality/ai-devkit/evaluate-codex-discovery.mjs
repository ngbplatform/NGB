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
import { codexFinalAnswer, redactAgentOutput } from './agent-output.mjs'

assert.ok([5, 6].includes(process.argv.length),
  'Usage: node evaluate-codex-discovery.mjs <sealed-candidate> <new-evidence-directory> <installed-codex-binary> [root|subdirectory]')
const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const candidate = await loadCandidate(resolve(process.argv[2]))
const output = resolve(process.argv[3])
const executable = await realpath(resolve(process.argv[4]))
const matrixBytes = await readFile(new URL('./agent-evaluations.json', import.meta.url))
const profileBytes = await readFile(new URL('./codex-discovery-profile.json', import.meta.url))
const oracleBytes = await readFile(new URL('./evaluation-oracles/skill-discovery.mjs', import.meta.url))
const runnerBytes = await readFile(fileURLToPath(import.meta.url))
const redactorBytes = await readFile(new URL('./agent-output.mjs', import.meta.url))
const matrix = JSON.parse(matrixBytes)
const workingDirectories = process.argv[5] ? [process.argv[5]] : matrix.workingDirectories
assert.ok(workingDirectories.every(value => matrix.workingDirectories.includes(value)))
const profile = JSON.parse(profileBytes)
const task = matrix.tasks.find(value => value.id === 'skill-discovery')
assert.equal(process.platform, matrix.platform)
assert.equal(process.arch, matrix.architecture)
assert.equal(candidate.version, matrix.version)
assert.ok(task && profile.maximumSecondsPerAttempt <= matrix.maximumSecondsPerAttempt)
await mkdir(output, { mode: 0o700 })
const stage = await realpath(await mkdtemp(join(tmpdir(), 'ngb-codex-discovery-')))
const buildEnvironment = executionEnvironment(join(stage, 'build-home'))
const agentEnvironment = Object.fromEntries(['HOME', 'USER', 'LOGNAME', 'PATH', 'TMPDIR', 'LANG', 'LC_ALL', 'TERM']
  .filter(name => process.env[name] !== undefined).map(name => [name, process.env[name]]))
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
  // Do not move authentication or overwrite the user's global configuration.
  // Global instructions remain a declared input because --ignore-user-config
  // does not disable them in this client version.
  const personalPath = join(homedir(), '.codex/AGENTS.md')
  const personalBytes = await readFile(personalPath)
  assert.equal(digest(personalBytes), '47cd305f489a74b44bff3f9acd325091ccef874667c19caac7ecf76665e37239',
    'Personal guidance changed; review and freeze a new profile before inference.')
  inputs.personalInstructionsSha256 = digest(personalBytes)
  await save('frozen/personal-instructions.md', personalBytes.toString('utf8'))
  for (const path of [join(homedir(), '.codex/AGENTS.override.md'), join(homedir(), '.agents/skills')]) {
    try {
      const details = await lstat(path)
      assert.ok(details.isDirectory() && (await readdir(path)).length === 0,
        'Additional personal guidance requires a new frozen profile.')
    } catch (error) {
      if (error.code !== 'ENOENT') throw error
    }
  }
  const skillsRoot = join(homedir(), '.codex/skills')
  assert.deepEqual((await readdir(skillsRoot)).sort(), ['.system', 'codex-primary-runtime'], 'Unexpected personal skill sources.')
  assert.deepEqual(await readdir(join(skillsRoot, 'codex-primary-runtime')), [], 'Unexpected personal runtime skills.')
  const systemRoot = join(skillsRoot, '.system')
  const skillNames = []
  for (const entry of await readdir(systemRoot, { withFileTypes: true })) {
    if (entry.isDirectory()) skillNames.push(entry.name)
  }
  assert.deepEqual(skillNames.sort(), [...profile.disabledPersonalSkills].sort(), 'Personal skill inventory changed.')
  const options = { cwd: stage, env: agentEnvironment, encoding: 'utf8', signal: abort.signal,
    timeout: 15000, maxBuffer: 1024 * 1024 }
  const version = (await exec(executable, ['--version'], options)).stdout.trim()
  assert.equal(version, `codex-cli ${profile.clientVersion}`, 'The frozen client version changed.')
  const authentication = await exec(executable, ['login', 'status'], options)
  assert.match(authentication.stdout + authentication.stderr, /Logged in using ChatGPT/,
    'This profile requires an existing ChatGPT subscription login.')
  report.authentication = { method: 'chatgpt' }
  const binaryHash = createHash('sha256')
  for await (const chunk of createReadStream(executable)) binaryHash.update(chunk)
  report.client = { version: profile.clientVersion, sha256: binaryHash.digest('hex') }
  const template = join(stage, `NGB.Platform.Templates.${candidate.version}.nupkg`)
  const hive = join(stage, 'template-hive')
  const schemaPath = join(output, 'frozen/response-schema.json')
  await writeFile(template, candidate.files[candidate.template], { flag: 'wx', mode: 0o400 })
  await save('inputs.json', { ...inputs, task, profile, client: report.client })
  await save('frozen/profile.json', profileBytes.toString('utf8'))
  await save('frozen/oracle.mjs', oracleBytes.toString('utf8'))
  await save('frozen/runner.mjs', runnerBytes.toString('utf8'))
  await save('frozen/matrix.json', matrixBytes.toString('utf8'))
  await save('frozen/response-schema.json', responseSchema)
  await dotnet('install', ['new', 'install', template, '--debug:custom-hive', hive])
  for (const workingDirectory of workingDirectories) {
    abort.signal.throwIfAborted()
    const root = join(stage, workingDirectory)
    await dotnet(`create-${workingDirectory}`, ['new', 'ngb', '-n', 'KitDiscovery', '-o', root, '--debug:custom-hive', hive])
    await assertConsumerIsolation(root, repository)
    const before = await snapshot(root)
    assert.ok(!Object.keys(before).some(path => /(^|\/)\.env(?:\.|$)/.test(path)), 'A discovery fixture must not contain credentials.')
    assert.ok(!Object.keys(before).some(path => path.startsWith('.codex/')), 'The fixture must not introduce client configuration.')
    const cwd = workingDirectory === 'root' ? root : join(root, 'KitDiscovery.Api')
    const settings = [
      'approval_policy="never"', 'default_permissions="discovery"', 'web_search="disabled"',
      `model_reasoning_effort=${JSON.stringify(profile.reasoningEffort)}`,
      `project_root_markers=${JSON.stringify(profile.projectRootMarkers)}`,
      `permissions.discovery.filesystem={":root"="deny",":minimal"="read","/System/Library/OpenSSL"="read",${JSON.stringify(homedir())}="deny","/private/tmp"="deny","/private/var/folders"="deny",${JSON.stringify(root)}="read"}`,
      'permissions.discovery.network.enabled=false',
      'allow_login_shell=false', 'shell_environment_policy.inherit="core"',
      'shell_environment_policy.ignore_default_excludes=false',
      'analytics.enabled=false', 'otel.metrics_exporter="none"',
      `skills.config=[${skillNames.map(name => `{path=${JSON.stringify(join(systemRoot, name))},enabled=false}`).join(',')}]`,
    ]
    const prefix = `attempts/codex/${workingDirectory}/${task.id}`
    const args = ['exec', '--ignore-user-config', '--ignore-rules', '--ephemeral', '--skip-git-repo-check',
      '--model', profile.model, '--output-schema', schemaPath,
      '--color', 'never', '--json', '--cd', cwd,
      ...profile.disabledFeatures.flatMap(name => ['--disable', name]),
      ...settings.flatMap(value => ['-c', value]), task.prompt]
    const configuration = { args, cwd, environmentKeys: Object.keys(agentEnvironment).sort(),
      profileSha256: inputs.profileSha256, clientSha256: report.client.sha256 }
    const configurationArtifact = await save(`${prefix}/configuration.json`, configuration)
    const attempt = { client: 'codex', clientVersion: profile.clientVersion, provider: 'openai',
      model: profile.model, workingDirectory, task: task.id, attempt: 1, state: 'not-run',
      platform: process.platform, architecture: process.arch, humanAssistance: matrix.humanAssistance,
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
    console.log(`Evaluating Codex skill discovery from ${workingDirectory}; one bounded attempt.`)
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
        throw new Error(`Codex did not complete successfully (${error.signal ?? error.code ?? 'cancelled'}).`)
      }
      const events = stdout.trim().split('\n').map(line => JSON.parse(line))
      const completed = events.findLast(event => event.type === 'turn.completed')
      assert.ok(completed && !events.some(event => event.type === 'turn.failed'), 'Missing successful client output.')
      attempt.usage = completed.usage
      assert.ok(Number.isInteger(completed.usage.input_tokens) && Number.isInteger(completed.usage.output_tokens)
        && completed.usage.input_tokens + completed.usage.output_tokens <= profile.maximumReportedTokensPerAttempt,
      'Reported usage exceeded the frozen token acceptance ceiling.')
      const items = events.filter(event => event.type === 'item.completed').map(event => event.item)
      assert.ok(items.every(item => ['agent_message', 'reasoning', 'command_execution'].includes(item.type)),
        'An unexpected external tool or mutation was reported.')
      attempt.toolTypes = [...new Set(items.map(item => item.type))]
      attempt.answer = JSON.parse(codexFinalAnswer(events))
      await save(`${prefix}/answer.json`, attempt.answer)
      const after = await snapshot(root)
      attempt.acceptance = verifySkillDiscovery({ answer: attempt.answer, before, after, version: candidate.version, client: 'codex' })
      assert.equal(digest(await readFile(personalPath)), inputs.personalInstructionsSha256, 'Personal instructions changed during evaluation.')
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
      console.log(`Codex discovery ${workingDirectory}: ${attempt.state}.`)
    }
  }
  assert.ok(report.attempts.length === workingDirectories.length
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
  console.log(`Real Codex discovery: ${report.status}. Full agent evaluations remain a separate gate.`)
}
