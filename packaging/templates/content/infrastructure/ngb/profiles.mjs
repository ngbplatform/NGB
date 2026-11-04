import assert from 'node:assert/strict'
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises'
import { randomUUID } from 'node:crypto'
import { tmpdir } from 'node:os'
import { basename, dirname, join, relative, resolve } from 'node:path'
import { digest } from './kit-contracts.mjs'
import { assetPaths, readAsset, safePath } from './kit-files.mjs'
import { report } from './kit.mjs'
import { executionEnvironment, runTool } from './process.mjs'
import { applicationConfiguration, validateApplication } from './validation.mjs'
import { layerViolations, readProjectXml } from './project-data.mjs'
import { browserTestCounts, frontendCoverage, frontendTestCounts, uniqueCoverageReport } from './evidence.mjs'
import { applicationFiles, managedPackageFiles } from './application.mjs'
import { assemblyMetadata, resolvedProject } from './resolved-data.mjs'
import { classifyCoverageExclusion, evaluateCoverage, mergeCoverage, parseCobertura } from './coverage.mjs'
import { startTestEnvironment } from './environment.mjs'
import { runLoadProfile } from './performance-runner.mjs'

function processCheck(id, result) {
  return { id, required: true, state: result.state,
    message: result.state === 'passed' ? 'Executed successfully.' : `Tool ${result.tool}: ${result.reason ?? `exit ${result.exitCode}`}.`,
    exitCode: result.exitCode, elapsedMs: result.elapsedMs }
}

export function testCounts(xml) {
  const root = readProjectXml(xml)
  assert.equal(root.name, 'TestRun', 'Expected a TRX TestRun report.')
  let counters = null
  function visit(node) {
    if (node.name === 'Counters') {
      assert.equal(counters, null, 'Duplicate test discovery counters.')
      counters = node.attributes
    }
    node.children.forEach(visit)
  }
  visit(root)
  assert.ok(counters, 'Test report has no discovery counters.')
  const total = Number(counters.total)
  const executed = Number(counters.executed)
  const passed = Number(counters.passed)
  const failed = Number(counters.failed)
  assert.ok([total, executed, passed, failed].every(Number.isSafeInteger), 'Invalid test report counters.')
  assert.ok(total > 0 && executed === total && passed === total && failed === 0,
    'Required test suite contains failures, skips or no discovered tests.')
  return { total, executed, passed, failed, skipped: total - executed }
}

export async function doctor(root) {
  const validation = await validateApplication(root)
  const checks = [...validation.checks]
  const home = await mkdtemp(join(tmpdir(), 'ngb-doctor-'))
  try {
    const env = executionEnvironment(home)
    checks.push({ id: 'tools.node', required: true,
      state: /^v24\./.test(process.version) ? 'passed' : 'failed',
      message: `Node ${process.version}; supported release toolchain requires Node 24.x.` })
    const dotnet = await runTool('dotnet', ['--version'], { cwd: home, env, timeoutMs: 15000 })
    checks.push({ ...processCheck('tools.dotnet', dotnet),
      state: dotnet.state === 'passed' && !/^10\./.test(dotnet.output.trim()) ? 'failed' : dotnet.state })
    const npm = await runTool('npm', ['--version'], { cwd: home, env, timeoutMs: 15000 })
    checks.push(processCheck('tools.npm', npm))
    const docker = await runTool('docker', ['version', '--format', '{{.Server.Version}}'], { cwd: home, env, timeoutMs: 15000 })
    checks.push({ ...processCheck('tools.docker', docker), required: false,
      message: 'Docker is required for integration/E2E/certification, not static inspection or unit tests.' })
  } finally {
    await rm(home, { recursive: true, force: true })
  }
  return { ...validation, command: 'doctor', checks,
    status: checks.every(check => !check.required || check.state === 'passed') ? 'passed' : 'failed' }
}

export async function executeProfile(root, options) {
  const config = await applicationConfiguration(root)
  const validation = await validateApplication(root)
  const checks = [...validation.checks]
  const snapshot = async () => Object.fromEntries(Object.entries(await applicationFiles(root))
    .map(([path, bytes]) => [path, digest(bytes)]).sort(([left], [right]) => left.localeCompare(right, 'en')))
  const sourceInputs = await snapshot()
  const home = await mkdtemp(join(tmpdir(), 'ngb-execution-'))
  const runId = randomUUID()
  const relativeOutput = `.ngb/reports/${runId}`
  const output = await safePath(root, relativeOutput)
  await mkdir(output, { recursive: true, mode: 0o700 })
  const env = executionEnvironment(home)
  const backendSources = new Set()
  const backendAssemblies = new Set()
  const backendReports = []
  const abort = new AbortController()
  const deadline = setTimeout(() => abort.abort(), 3600000)
  const cancel = () => abort.abort()
  process.once('SIGINT', cancel)
  process.once('SIGTERM', cancel)
  const run = async (id, tool, args, cwd = root, overrides = {}) => {
    const result = await runTool(tool, args, { cwd, env: { ...env, ...overrides }, signal: abort.signal, maxBytes: 4 * 1024 * 1024 })
    await writeFile(join(output, `${id}.log`), result.output, { mode: 0o600 })
    checks.push(processCheck(id, result))
    return result.state === 'passed'
  }
  const inspectEvidence = async (id, path, inspect) => {
    try {
      const text = await readAsset(root, path, 8 * 1024 * 1024)
      assert.ok(text, 'The current run did not produce its required test report.')
      checks.push({ id, required: true, state: 'passed', message: 'Verified current-run discovery and coverage evidence.',
        evidence: await inspect(text), reportSha256: digest(text) })
    } catch (error) {
      checks.push({ id, required: true, state: 'failed', message: error.message })
    }
  }
  const inspectBuild = async () => {
    try {
      assert.ok(config.projects.length <= 256, 'Build inspection supports at most 256 projects per application.')
      const facts = []
      const targets = new Map()
      const assemblies = []
      for (const [index, project] of config.projects.entries()) {
        const id = `build.target.${index}`
        assert.ok(await run(id, 'dotnet', ['msbuild', project.path, '-nologo', '-p:Configuration=Release',
          '-getProperty:TargetPath,ProjectAssetsFile', '-getItem:Compile']),
          `Could not locate the built assembly: ${project.path}`)
        const resolved = JSON.parse(await readAsset(root, `${relativeOutput}/${id}.log`, 4 * 1024 * 1024))
        const properties = resolved.Properties
        assert.ok(properties?.TargetPath && properties.ProjectAssetsFile, 'Build did not report its assembly and restore outputs.')
        const path = relative(root, properties.ProjectAssetsFile).replaceAll('\\', '/')
        const text = await readAsset(root, path, 16 * 1024 * 1024)
        assert.ok(text, `Resolved build metadata is missing: ${path}`)
        facts.push(resolvedProject(root, project, JSON.parse(text), validation.sdkVersion, env.NUGET_PACKAGES))
        const target = properties.TargetPath
        const local = relative(root, target).replaceAll('\\', '/')
        assert.match(local, /\.dll$/, 'Build output must be a managed assembly.')
        targets.set(project.path, await safePath(root, local))
        const name = basename(target, '.dll')
        assert.match(name, /^[A-Za-z_][A-Za-z0-9_.-]*$/, 'Unsupported assembly name in the coverage inventory.')
        backendAssemblies.add(name)
        if (project.role !== 'tests') {
          assert.ok(Array.isArray(resolved.Items?.Compile), `Build did not report Compile items: ${project.path}`)
          for (const item of resolved.Items.Compile) {
            assert.ok(typeof item.FullPath === 'string', 'Compile item has no resolved path.')
            const path = relative(root, item.FullPath).replaceAll('\\', '/')
            await safePath(root, path)
            backendSources.add(path)
          }
        }
      }
      const violations = layerViolations(config, facts)
      assert.equal(violations.length, 0, violations.map(item => `${item.path}: ${item.dependency}`).join('\n'))
      const helper = targets.get('infrastructure/validation/Ngb.Validation.csproj')
      assert.ok(helper, 'The declared NGB metadata helper project is required for build inspection.')
      for (const [index, project] of config.projects.entries()) {
        const target = targets.get(project.path)
        const id = `build.metadata.${index}`
        assert.ok(await run(id, 'dotnet', [helper, '--assembly', target], root, { DOTNET_GCHeapHardLimit: '0x10000000' }),
          `Metadata helper could not inspect ${project.path}.`)
        const text = await readAsset(root, `${relativeOutput}/${id}.log`, 4 * 1024 * 1024)
        assemblies.push({ path: project.path, ...assemblyMetadata(JSON.parse(text), basename(target, '.dll')) })
      }
      const declaration = checks.find(check => check.id === 'architecture.declarations')
      if (declaration.state === 'not-run') {
        declaration.state = 'passed'
        declaration.message = 'Conditional declarations were verified against the resolved dependency graph after build.'
      }
      checks.push({ id: 'build.contracts', required: true, state: 'passed',
        message: 'Resolved dependency directions, SDK package versions and managed assembly metadata verified.', assemblies,
        graphSha256: digest(JSON.stringify(facts)) })
    } catch (error) {
      checks.push({ id: 'build.contracts', required: true, state: 'failed', message: error.message })
    }
  }
  const inspectBackendCoverage = async () => {
    try {
      assert.ok(backendReports.length > 0 && backendSources.size > 0, 'Backend source or coverage inventory is empty.')
      const reports = []
      const identities = []
      for (const path of backendReports) {
        const text = await readAsset(root, path, 16 * 1024 * 1024)
        reports.push(parseCobertura(text, root))
        identities.push({ path, sha256: digest(text) })
      }
      const excludedFiles = []
      const requiredFiles = []
      for (const file of [...backendSources].sort()) {
        const text = await readAsset(root, file)
        assert.ok(text !== null, `Compiled source is missing: ${file}`)
        const reason = classifyCoverageExclusion(file, text)
        if (reason) excludedFiles.push({ file, reason })
        else requiredFiles.push(file)
      }
      const evaluation = evaluateCoverage({ requiredFiles, excludedFiles, reportFiles: mergeCoverage(reports),
        thresholds: { lines: 100, branches: 100, methods: 100 } })
      await writeFile(join(output, 'backend-coverage.json'), `${JSON.stringify({ reports: identities, ...evaluation }, null, 2)}\n`, { mode: 0o600 })
      checks.push({ id: 'test.backend.coverage', required: true, state: evaluation.passed ? 'passed' : 'failed',
        message: 'Full backend source inventory requires 100% per-file lines, branches and methods.',
        evidence: evaluation, reports: identities })
    } catch (error) {
      checks.push({ id: 'test.backend.coverage', required: true, state: 'failed', message: error.message })
    }
  }
  try {
    const blocking = checks.filter(check => check.state === 'failed' && !['packages.locked'].includes(check.id))
    if (blocking.length === 0) {
      const packages = await managedPackageFiles(root)
      const candidate = Object.hasOwn(packages, 'NuGet.Local.Config')
      const restored = await run('build.restore', 'dotnet', ['restore', config.solution, '--locked-mode', '--configfile',
        candidate ? 'NuGet.Local.Config' : 'NuGet.Config'])
      const built = restored && await run('build.backend', 'dotnet', ['build', config.solution, '--no-restore', '-c', 'Release',
        '-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false'])
      if (built) await inspectBuild()
      const web = resolve(root, config.frontend.path)
      if (candidate) await run('build.frontend-cache', 'npm', ['cache', 'add', resolve(root, 'web/.ngb-packages/ui.tgz'), '--ignore-scripts'], web)
      const webRestored = await run('build.frontend-restore', 'npm', ['ci', '--ignore-scripts', '--workspaces=false'], web)
      const webBuilt = webRestored && await run('build.frontend', 'npm', ['run', 'build'], web)
      if (options.command !== 'validate' && built && webBuilt && checks.some(check => check.id === 'build.contracts' && check.state === 'passed')) {
        const profile = options.profile ?? 'unit'
        const suites = profile === 'all' || options.command === 'certify' ? ['unit', 'integration', 'e2e'] : [profile]
        assert.ok(backendAssemblies.size > 0, 'Build inspection is required before collecting backend coverage.')
        const settings = join(output, 'backend.runsettings')
        const testDirectories = config.projects.filter(project => project.role === 'tests').map(project => dirname(project.path))
        assert.ok(testDirectories.every(path => /^[A-Za-z0-9_./-]+$/.test(path)), 'Unsupported test directory in coverage configuration.')
        await writeFile(settings, `<?xml version="1.0" encoding="utf-8"?>
<RunSettings>
    <DataCollectionRunSettings>
        <DataCollectors>
            <DataCollector friendlyName="XPlat Code Coverage">
                <Configuration>
                    <Format>cobertura</Format>
                    <Include>${[...backendAssemblies].sort().map(name => `[${name}]*`).join(',')}</Include>
                    <IncludeTestAssembly>true</IncludeTestAssembly>
                    <ExcludeByFile>${testDirectories.map(path => `**/${path}/**/*.cs`).join(',')},**/obj/**/*.cs</ExcludeByFile>
                    <SkipAutoProps>false</SkipAutoProps>
                    <UseSourceLink>false</UseSourceLink>
                </Configuration>
            </DataCollector>
        </DataCollectors>
    </DataCollectionRunSettings>
</RunSettings>
`, { mode: 0o600 })
        if (suites.includes('unit') || suites.includes('e2e')) {
          await run('tools.browser', 'npm', ['exec', '--no', '--', 'playwright', 'install', 'chromium'], web)
        }
        for (const suite of suites) {
          if (abort.signal.aborted) break
          if (suite === 'e2e') {
            const path = `${relativeOutput}/browser.json`
            let environment
            try {
              environment = await startTestEnvironment(root, { signal: abort.signal })
              checks.push({ id: 'test.e2e.environment', required: true, state: 'passed',
                message: 'Disposable application and identity services are healthy.', runId: environment.runId })
              await run('test.e2e', 'npm', ['run', 'test:e2e', '--', '--config',
                resolve(root, config.tests.e2e.config), '--reporter=json'], web, {
                PLAYWRIGHT_JSON_OUTPUT_FILE: join(output, 'browser.json'),
                NGB_TEST_RUN_ID: environment.runId,
                NGB_TEST_ORIGIN: environment.origin,
                NGB_TEST_PASSWORD: environment.password,
              })
              if (options.command === 'certify') {
                const load = await runLoadProfile({ environment, output, env, signal: abort.signal })
                checks.push(...load.checks)
                checks.push({ id: 'certification.performance', required: true, state: load.passed ? 'passed' : 'failed',
                  message: load.plan.profile.scope, plan: load.plan })
              }
            } catch (error) {
              checks.push({ id: 'test.e2e.environment', required: true, state: 'failed', message: error.message })
            } finally {
              if (environment) {
                try {
                  await environment.stop()
                  checks.push({ id: 'test.e2e.cleanup', required: true, state: 'passed',
                    message: 'Owned test containers, network and volumes were removed.', runId: environment.runId })
                } catch (error) {
                  checks.push({ id: 'test.e2e.cleanup', required: true, state: 'failed',
                    message: error.message, runId: environment.runId })
                }
              }
            }
            await inspectEvidence('test.e2e.discovery', path, text => browserTestCounts(JSON.parse(text)))
          } else {
            for (const [index, project] of config.tests[suite].entries()) {
              const id = `test.${suite}.${index}`
              const passed = await run(id, 'dotnet', ['test', project, '--no-restore', '--no-build', '-c', 'Release',
                '--logger', `trx;LogFileName=${id}.trx`, '--results-directory', join(output, id),
                '--collect:XPlat Code Coverage', '--settings', settings])
              if (passed) {
                await inspectEvidence(`${id}.discovery`, `${relativeOutput}/${id}/${id}.trx`, testCounts)
                try {
                  const paths = (await assetPaths(root, `${relativeOutput}/${id}`)).filter(path => path.endsWith('/coverage.cobertura.xml'))
                  const attachments = []
                  for (const path of paths) attachments.push({ path, sha256: digest(await readAsset(root, path, 16 * 1024 * 1024)) })
                  const selected = uniqueCoverageReport(attachments)
                  checks.push({ id: `${id}.coverage-report`, required: true, state: 'passed',
                    message: 'Verified one current-run backend coverage measurement, including identical VSTest attachment copies.',
                    evidence: selected })
                  backendReports.push(selected.path)
                } catch (error) {
                  checks.push({ id: `${id}.coverage-report`, required: true, state: 'failed', message: error.message })
                }
              }
            }
            if (suite === 'unit') {
              await run('test.frontend', 'npm', ['run', 'test', '--', '--run', '--coverage', '--reporter=json',
                `--outputFile=${join(output, 'frontend.json')}`, `--coverage.reportsDirectory=${join(output, 'frontend-coverage')}`], web)
              await inspectEvidence('test.frontend.discovery', `${relativeOutput}/frontend.json`, text => frontendTestCounts(JSON.parse(text)))
              const sources = (await assetPaths(root, `${config.frontend.path}/src`))
                .filter(path => /\.(?:[cm]?[jt]sx?|vue)$/.test(path) && !/\.d\.[cm]?ts$/.test(path))
                .map(path => path.slice(config.frontend.path.length + 1))
              await inspectEvidence('test.frontend.coverage', `${relativeOutput}/frontend-coverage/coverage-summary.json`,
                text => frontendCoverage(JSON.parse(text), sources, web))
            }
          }
        }
        if (suites.includes('unit') && suites.includes('integration')) await inspectBackendCoverage()
      }
    } else {
      checks.push({ id: 'execution.prerequisites', required: true, state: 'not-run',
        message: 'Fix failed inspection checks before executing this profile.' })
    }
    const required = ['build.restore', 'build.backend', 'build.contracts', 'build.frontend-restore', 'build.frontend']
    if (options.command !== 'validate') {
      const profile = options.profile ?? 'unit'
      const suites = profile === 'all' || options.command === 'certify' ? ['unit', 'integration', 'e2e'] : [profile]
      for (const suite of suites) {
        if (suite === 'e2e') {
          required.push('test.e2e.environment', 'test.e2e', 'test.e2e.discovery', 'test.e2e.cleanup')
        } else {
          for (const [index] of config.tests[suite].entries()) {
            required.push(`test.${suite}.${index}`, `test.${suite}.${index}.discovery`, `test.${suite}.${index}.coverage-report`)
          }
          if (suite === 'unit') required.push('test.frontend', 'test.frontend.discovery', 'test.frontend.coverage')
        }
      }
      if (suites.includes('unit') && suites.includes('integration')) required.push('test.backend.coverage')
    }
    for (const id of required) {
      if (!checks.some(check => check.id === id)) checks.push({ id, required: true, state: 'not-run', message: 'A prerequisite failed or execution was cancelled.' })
    }
    if (options.command === 'certify' && !checks.some(check => check.id === 'certification.performance')) {
      checks.push({ id: 'certification.performance', required: true, state: 'not-run',
        message: 'The disposable environment prerequisite failed; the required starter HTTP workload did not run.' })
    }
    const unchanged = JSON.stringify(await snapshot()) === JSON.stringify(sourceInputs)
    checks.push({ id: 'execution.inputs', required: true, state: unchanged ? 'passed' : 'failed',
      message: unchanged ? 'Application source and dependency inputs remained unchanged during execution.'
        : 'Application inputs changed during execution; rerun against a consistent source snapshot.' })
    const evidence = report(options.command, checks, {
      runId, profile: options.profile ?? 'unit', sdkVersion: validation.sdkVersion,
      kitSha256: validation.kitSha256, dependencySha256: validation.dependencySha256,
      outputDirectory: relativeOutput,
      sourceSha256: digest(JSON.stringify(sourceInputs)), sourceInputs,
      toolchain: { node: process.version, platform: process.platform, architecture: process.arch },
    })
    await writeFile(join(output, 'report.json'), `${JSON.stringify(evidence, null, 2)}\n`, { mode: 0o600 })
    return evidence
  } finally {
    clearTimeout(deadline)
    process.removeListener('SIGINT', cancel)
    process.removeListener('SIGTERM', cancel)
    await rm(home, { recursive: true, force: true })
  }
}
