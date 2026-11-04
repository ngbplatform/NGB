import assert from 'node:assert/strict'
import { mkdir, mkdtemp, readFile, realpath, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { basename, dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { parseArgs } from 'node:util'
import { assertArtifactIdentity, assertArtifactInventory } from '../external-consumer/release-contracts.mjs'
import { assetPaths, readAsset } from '../../packaging/templates/content/infrastructure/ngb/kit-files.mjs'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { verifyCliArchive } from '../../packaging/templates/content/infrastructure/ngb/archive.mjs'
import { inspectCapabilities, preparePayload } from '../../ai-devkit/prepare.mjs'
import { exampleFrontendSources, exampleSources, passingTestCases } from './example-evidence.mjs'
import { executionEnvironment, runTool } from '../../packaging/templates/content/infrastructure/ngb/process.mjs'
import { browserTestCounts, frontendCoverage, frontendTestCounts, uniqueCoverageReport } from '../../packaging/templates/content/infrastructure/ngb/evidence.mjs'
import { classifyCoverageExclusion, evaluateCoverage, mergeCoverage, parseCobertura } from '../../packaging/templates/content/infrastructure/ngb/coverage.mjs'
import { testCounts } from '../../packaging/templates/content/infrastructure/ngb/profiles.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const { values } = parseArgs({ options: { packages: { type: 'string' }, output: { type: 'string' } } })
assert.ok(values.packages && values.output, 'Use --packages <sealed-candidate> --output <new-evidence-directory>.')
const candidate = resolve(values.packages)
const output = resolve(values.output)
const manifestBytes = await readFile(join(candidate, 'release-manifest.json'))
const manifest = JSON.parse(manifestBytes)
const inventory = JSON.parse(await readFile(join(repository, 'packaging/nuget/baselines.json'), 'utf8'))
assertArtifactInventory(manifest, inventory)
await mkdir(output)
const stage = await realpath(await mkdtemp(join(tmpdir(), 'ngb-example-acceptance-')))
const env = executionEnvironment(join(stage, 'home'))
const abort = new AbortController()
const cancel = () => abort.abort()
process.once('SIGINT', cancel)
process.once('SIGTERM', cancel)
const evidence = { schemaVersion: 2, status: 'not-ready', candidateSha256: digest(manifestBytes),
  runnerSha256: digest(await readFile(fileURLToPath(import.meta.url))),
  version: manifest.version, startedAt: new Date().toISOString(), node: process.version, stage, examples: [] }
const save = async (path, bytes) => {
  await mkdir(dirname(join(output, path)), { recursive: true })
  await writeFile(join(output, path), bytes, { mode: 0o600, flag: 'wx' })
  return { path, sha256: digest(bytes) }
}
const invoke = async (id, args, cwd, tool = 'dotnet', timeoutMs = 600_000) => {
  abort.signal.throwIfAborted()
  const result = await runTool(tool, args, { cwd, env, timeoutMs, signal: abort.signal,
    maxBytes: 8 * 1024 * 1024,
    onSpawn: async pid => {
      await save(`processes/${id}.json`, JSON.stringify({ schemaVersion: 1,
        candidateSha256: evidence.candidateSha256, runnerPid: process.pid, childPid: pid,
        startedAt: new Date().toISOString(), tool, args, cwd,
        purpose: 'Diagnostic identity only; this receipt does not establish current process ownership.' }, null, 2) + '\n')
      console.log(`Independent example step: ${id}.`)
    },
  })
  await writeFile(join(output, `${id}.log`), result.output, { mode: 0o600 })
  assert.equal(result.state, 'passed', `Example ${id} failed: ${result.reason ?? result.exitCode}. See its saved log.`)
  return result.output
}
try {
  await save('started.json', JSON.stringify(evidence, null, 2) + '\n')
  assert.match(process.version, /^v24\./)
  assert.equal((await invoke('dotnet-version', ['--version'], stage)).trim(), '10.0.401')
  const feed = join(stage, 'feed')
  await mkdir(feed)
  for (const artifact of manifest.artifacts.filter(item => item.path.endsWith('.nupkg'))) {
    const bytes = await readFile(join(candidate, artifact.path))
    assertArtifactIdentity(artifact, bytes)
    await writeFile(join(feed, basename(artifact.path)), bytes, { mode: 0o400 })
  }
  const cliArtifact = manifest.artifacts.find(item => item.path === `npm/ngbplatform-cli-${manifest.version}.tgz`)
  assert.ok(cliArtifact, 'The candidate does not contain the exact CLI package.')
  const cliBytes = await readFile(join(candidate, cliArtifact.path))
  assertArtifactIdentity(cliArtifact, cliBytes)
  const cli = verifyCliArchive(cliBytes, manifest.version)
  const payload = JSON.parse(cli.files.get('package/infrastructure/ngb/kit-payload.json'))
  assert.deepEqual(payload, await preparePayload(), 'Repack the candidate after changing example or guidance sources.')
  const inputs = exampleSources(payload)
  evidence.sources = inputs
  evidence.capabilityInputsSha256 = inspectCapabilities(payload).inputsSha256
  const solutions = Object.keys(inputs).filter(path => path.endsWith('.slnx'))
  assert.ok(solutions.length > 0, 'No independent example solutions were discovered.')
  for (const solution of solutions) {
    const name = dirname(solution)
    assert.match(name, /^[a-z][a-z0-9-]+$/)
    const root = join(stage, name)
    const result = { name, status: 'not-ready', suites: [] }
    evidence.examples.push(result)
    for (const file of payload.files.filter(file => file.path.startsWith(`ai/vendor/examples/${name}/`))) {
      const path = join(stage, file.path.slice('ai/vendor/examples/'.length))
      await mkdir(dirname(path), { recursive: true })
      await writeFile(path, file.content, { flag: 'wx' })
    }
    await writeFile(join(root, 'NuGet.Config'), `<?xml version="1.0" encoding="utf-8"?>
<configuration>
    <packageSources><clear />
        <add key="candidate" value="../feed" />
        <add key="registry" value="https://api.nuget.org/v3/index.json" />
    </packageSources>
    <packageSourceMapping><clear />
        <packageSource key="candidate"><package pattern="NGB.Platform.*" /></packageSource>
        <packageSource key="registry"><package pattern="*" /></packageSource>
    </packageSourceMapping>
    <fallbackPackageFolders><clear /></fallbackPackageFolders>
</configuration>
`)
    await invoke(`${name}.restore`, ['restore', basename(solution), '--configfile', 'NuGet.Config', '-p:RestorePackagesWithLockFile=true'], root)
    await invoke(`${name}.build`, ['build', basename(solution), '--no-restore', '-c', 'Release',
      '-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false'], root)
    const listed = await invoke(`${name}.projects`, ['sln', basename(solution), 'list'], root)
    const projects = listed.split(/\r?\n/).map(line => line.trim()).filter(line => line.endsWith('.csproj'))
    assert.ok(projects.length > 0, 'Empty example project inventory.')
    const production = new Set()
    const testProjects = []
    const testSources = new Map()
    const assemblies = []
    for (const [index, project] of projects.entries()) {
      const metadata = JSON.parse(await invoke(`${name}.metadata.${index}`, ['msbuild', project, '-nologo',
        '-p:Configuration=Release', '-getProperty:IsTestProject,AssemblyName', '-getItem:Compile,ProjectReference'], root))
      const assembly = metadata.Properties.AssemblyName
      assert.match(assembly, /^[A-Za-z_][A-Za-z0-9_.-]*$/)
      assemblies.push(assembly)
      for (const item of metadata.Items.ProjectReference) {
        const path = relative(root, item.FullPath)
        assert.ok(!path.startsWith('..') && projects.includes(path), 'Example references a project outside its declared solution.')
      }
      const compiled = metadata.Items.Compile.map(item => relative(root, item.FullPath).replaceAll('\\', '/'))
      assert.ok(compiled.every(path => !path.startsWith('../')), 'Example compiles source outside its isolated root.')
      if (metadata.Properties.IsTestProject === 'true') {
        testProjects.push(project)
        testSources.set(project, compiled.map(path => `${name}/${path}`)
          .filter(path => Object.hasOwn(inputs, path)).sort())
      } else {
        for (const path of compiled) production.add(path)
      }
      const assets = JSON.parse(await readFile(join(root, dirname(project), 'obj/project.assets.json'), 'utf8'))
      for (const [identity, library] of Object.entries(assets.libraries)) {
        if (library.type === 'package' && identity.startsWith('NGB.Platform.')) {
          assert.equal(identity.split('/')[1], manifest.version, 'Example resolved the wrong SDK version.')
        }
      }
    }
    assert.ok(testProjects.length >= 2, 'An independent example requires unit and actual-provider integration suites.')
    const settings = join(root, 'coverage.runsettings')
    await writeFile(settings, `<RunSettings><RunConfiguration><TreatNoTestsAsError>true</TreatNoTestsAsError></RunConfiguration>
<xUnit><FailSkips>true</FailSkips></xUnit>
<DataCollectionRunSettings><DataCollectors><DataCollector friendlyName="XPlat Code Coverage"><Configuration>
<Format>cobertura</Format><Include>${assemblies.map(name => `[${name}]*`).join(',')}</Include><IncludeTestAssembly>true</IncludeTestAssembly>
<ExcludeByFile>${testProjects.map(project => `**/${dirname(project)}/**/*.cs`).join(',')},**/obj/**/*.cs</ExcludeByFile>
<SkipAutoProps>false</SkipAutoProps><UseSourceLink>false</UseSourceLink>
</Configuration></DataCollector></DataCollectors></DataCollectionRunSettings></RunSettings>\n`)
    const reports = []
    const suiteFailures = []
    for (const [index, project] of testProjects.entries()) {
      const directory = `.ngb/acceptance-${index}`
      try {
        await invoke(`${name}.tests.${index}`, ['test', project, '--no-build', '--no-restore', '-c', 'Release',
          '--logger', 'trx;LogFileName=tests.trx', '--results-directory', join(root, directory),
          '--collect:XPlat Code Coverage', '--settings', settings], root, 'dotnet', 1_800_000)
      } catch (error) {
        const artifacts = []
        for (const path of await assetPaths(root, directory)) {
          if (path.endsWith('/tests.trx') || path.endsWith('/coverage.cobertura.xml')) {
            const bytes = await readAsset(root, path, 16 * 1024 * 1024)
            artifacts.push(await save(`${name}/suite-${index}/failed/${path.slice(directory.length + 1)}`, bytes))
            if (path.endsWith('/coverage.cobertura.xml')) reports.push(parseCobertura(bytes, root))
          }
        }
        result.suites.push({ project, status: 'failed', sources: testSources.get(project), artifacts })
        suiteFailures.push(error.message)
        abort.signal.throwIfAborted()
        continue
      }
      const trx = await readAsset(root, `${directory}/tests.trx`, 8 * 1024 * 1024)
      const trxArtifact = await save(`${name}/suite-${index}/tests.trx`, trx)
      const paths = (await assetPaths(root, directory)).filter(path => path.endsWith('/coverage.cobertura.xml'))
      const attachments = await Promise.all(paths.map(async path => ({ path,
        sha256: digest(await readAsset(root, path, 16 * 1024 * 1024)) })))
      const report = uniqueCoverageReport(attachments)
      const coverage = await readAsset(root, report.path, 16 * 1024 * 1024)
      const coverageArtifact = await save(`${name}/suite-${index}/coverage.cobertura.xml`, coverage)
      const counts = testCounts(trx)
      reports.push(parseCobertura(coverage, root))
      result.suites.push({ project, counts, sources: testSources.get(project), cases: passingTestCases(trx),
        trx: trxArtifact, coverage: coverageArtifact })
    }
    const requiredFiles = []
    const excludedFiles = []
    for (const path of [...production].sort()) {
      const reason = classifyCoverageExclusion(path, await readAsset(root, path))
      if (reason) excludedFiles.push({ file: path, reason })
      else requiredFiles.push(path)
    }
    result.coverage = evaluateCoverage({ requiredFiles, excludedFiles, reportFiles: mergeCoverage(reports),
      thresholds: { lines: 100, branches: 100, methods: 100 } })
    if (Object.hasOwn(inputs, `${name}/web/package.json`)) {
      const frontend = { status: 'not-ready', reports: {} }
      result.frontend = frontend
      const web = join(root, 'web')
      const rawReports = {
        unit: '.ngb/frontend-tests.json',
        coverage: '.ngb/coverage/frontend/coverage-summary.json',
        browser: '.ngb/browser-results/report.json',
      }
      try {
        const artifact = manifest.artifacts.find(item => item.path === `npm/ngbplatform-ui-${manifest.version}.tgz`)
        assert.ok(artifact, 'The candidate does not contain the exact UI package.')
        const bytes = await readFile(join(candidate, artifact.path))
        assertArtifactIdentity(artifact, bytes)
        const archive = join(root, '.ngb', 'ui.tgz')
        await mkdir(dirname(archive), { recursive: true })
        await writeFile(archive, bytes, { flag: 'wx' })
        await invoke(`${name}.frontend-cache`, ['cache', 'add', archive, '--ignore-scripts'], web, 'npm')
        await invoke(`${name}.frontend-install`, ['ci', '--ignore-scripts', '--no-audit', '--no-fund'], web, 'npm')
        const installed = JSON.parse(await readFile(join(web, 'node_modules/@ngbplatform/ui/package.json'), 'utf8'))
        assert.equal(installed.version, manifest.version, 'Example resolved the wrong UI version.')
        await invoke(`${name}.frontend-build`, ['run', 'build'], web, 'npm')
        await invoke(`${name}.frontend-browser-install`, ['exec', '--no', '--', 'playwright', 'install', 'chromium'], web, 'npm')
        for (const [id, args] of [
          ['unit', ['run', 'test', '--', '--run', '--coverage', '--reporter=json', `--outputFile=${join(root, rawReports.unit)}`]],
          ['browser', ['run', 'test:e2e']],
        ]) {
          try {
            await invoke(`${name}.frontend-${id}`, args, web, 'npm')
          } catch (error) {
            suiteFailures.push(error.message)
          }
        }
        for (const [key, path] of Object.entries(rawReports)) {
          try {
            frontend.reports[key] = await save(`${name}/frontend/${key}.json`, await readAsset(root, path, 16 * 1024 * 1024))
          } catch (error) {
            suiteFailures.push(`Missing ${key} evidence: ${error.message}`)
          }
        }
        frontend.unit = frontendTestCounts(JSON.parse(await readAsset(root, rawReports.unit)))
        frontend.browser = browserTestCounts(JSON.parse(await readAsset(root, rawReports.browser)))
        frontend.coverage = frontendCoverage(JSON.parse(await readAsset(root, rawReports.coverage)),
          exampleFrontendSources(inputs, name), web)
        frontend.status = 'passed'
      } catch (error) {
        frontend.failure = error.message
        suiteFailures.push(error.message)
      }
    }
    assert.equal(suiteFailures.length, 0, suiteFailures.join('\n'))
    assert.ok(result.coverage.passed, `Example ${name} has incomplete per-file coverage.`)
    result.status = 'passed'
  }
  assert.deepEqual(await preparePayload(), payload, 'Kit sources changed during acceptance.')
  evidence.status = 'passed'
} catch (error) {
  evidence.failure = error.message
  process.exitCode = 1
} finally {
  process.removeListener('SIGINT', cancel)
  process.removeListener('SIGTERM', cancel)
  evidence.completedAt = new Date().toISOString()
  await writeFile(join(output, 'examples.json'), `${JSON.stringify(evidence, null, 2)}\n`)
  console.log(`Independent examples: ${evidence.status}. Evidence: ${join(output, 'examples.json')}`)
}
