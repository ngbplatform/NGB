import assert from 'node:assert/strict'
import { readdir } from 'node:fs/promises'
import { posix } from 'node:path'
import { digest, distinctPaths } from './kit-contracts.mjs'
import { readAsset, safePath } from './kit-files.mjs'
import { inspectKit, report, sdkVersion } from './kit.mjs'
import { layerViolations, projectReferences, readProjectXml, validateConfiguration } from './project-data.mjs'
import { toolVersion } from './contracts.mjs'

const excluded = new Set(['.git', '.ngb', '.local', 'node_modules', 'bin', 'obj', 'dist', 'coverage', 'TestResults', 'playwright-report', 'test-results'])

export async function discoverProjects(root, referenceProjects = new Set()) {
  const projects = []
  let count = 0
  async function visit(directory, depth) {
    assert.ok(depth < 32, 'Application nesting exceeds the inspection limit.')
    const entries = await readdir(directory ? await safePath(root, directory) : root, { withFileTypes: true })
    count += entries.length
    assert.ok(count <= 50000, 'Application scan exceeds 50,000 entries; split the workspace into configured applications.')
    for (const entry of entries) {
      if (excluded.has(entry.name)) continue
      const path = directory ? `${directory}/${entry.name}` : entry.name
      assert.ok(!entry.isSymbolicLink(), `Inspection cannot follow symbolic links: ${path}`)
      if (entry.isDirectory()) await visit(path, depth + 1)
      else if (entry.name.endsWith('.csproj') && !referenceProjects.has(path)) projects.push(path)
    }
  }
  await visit('', 0)
  distinctPaths(projects)
  return projects.sort()
}

export async function applicationConfiguration(root) {
  const text = await readAsset(root, 'ngb.config.json')
  assert.ok(text, 'ngb.config.json is missing. Declare project roles and required test suites before validation.')
  return validateConfiguration(JSON.parse(text))
}

export async function validateApplication(root) {
  const checks = []
  const identities = {}
  const check = async (id, action) => {
    try {
      const result = await action()
      checks.push({ id, required: true, state: 'passed', message: 'Verified.', ...result })
    } catch (error) {
      checks.push({ id, required: true, state: 'failed', message: error.message })
    }
  }
  let config
  let version
  const referenceProjects = new Set()
  await check('config.schema', async () => { config = await applicationConfiguration(root) })
  await check('versions.sdk', async () => {
    version = await sdkVersion(root)
    identities.sdkVersion = version
    assert.equal(version, toolVersion, 'SDK and CLI versions differ; use the coordinated upgrade.')
  })
  await check('kit.integrity', async () => {
    const state = await inspectKit(root)
    identities.kitVersion = state.payload.version
    identities.kitSha256 = state.plan.target
    assert.ok(state.installed, 'Kit is not installed.')
    assert.equal(state.payload.version, version, 'Kit and SDK versions differ.')
    assert.ok(state.plan.entries.every(entry => entry.state === 'unchanged'), 'Kit has changed, missing or divergent managed assets; inspect ai status.')
    for (const file of state.payload.files) {
      if (file.path.startsWith('ai/vendor/examples/') && file.path.endsWith('.csproj')) referenceProjects.add(file.path)
    }
  })
  if (!config) return report('validate', checks, identities)
  await check('packages.sources', async () => {
    const text = await readAsset(root, 'NuGet.Config')
    assert.ok(text, 'An application-local NuGet.Config is required.')
    const document = readProjectXml(text)
    assert.equal(document.name, 'configuration')
    const sources = document.children.find(node => node.name === 'packageSources')
    assert.ok(sources && sources.children[0]?.name === 'clear', 'NuGet sources must clear inherited feeds.')
    const entries = sources.children.filter(node => node.name === 'add')
    assert.ok(entries.length > 0, 'NuGet sources are empty.')
    assert.ok(entries.every(node => node.attributes.value === 'https://api.nuget.org/v3/index.json'),
      'SDK inspection supports the official NuGet registry; use the verified candidate package state for unpublished releases.')
    const mappings = document.children.find(node => node.name === 'packageSourceMapping')
    assert.ok(mappings && mappings.children[0]?.name === 'clear', 'NuGet package source mappings must clear inherited rules.')
    assert.ok(mappings.children.some(node => node.name === 'packageSource'
      && entries.some(entry => entry.attributes.key === node.attributes.key)
      && node.children.some(pattern => pattern.name === 'package' && ['*', 'NGB.Platform.*'].includes(pattern.attributes.pattern))),
    'NuGet must map platform packages to the declared registry.')
    assert.ok(!document.children.some(node => node.name === 'packageSourceCredentials'),
      'Keep credentials outside application package-source files.')
    for (const path of ['.npmrc', `${config.frontend.path}/.npmrc`]) {
      const npm = await readAsset(root, path)
      assert.ok(!npm || npm.split(/\r?\n/).every(line => !line.trim() || /^[#;]/.test(line.trim())),
        `Review unsupported npm overrides in ${path}; registry and lifecycle policy must remain explicit.`)
    }
    identities.sourcesSha256 = digest(text)
  })
  await check('projects.discovery', async () => {
    assert.deepEqual(await discoverProjects(root, referenceProjects), config.projects.map(project => project.path).sort(),
      'Discovered projects differ from ngb.config.json; every project needs an explicit role.')
    const solution = await readAsset(root, config.solution)
    assert.ok(solution, 'Configured solution does not exist.')
    if (config.solution.endsWith('.slnx')) {
      const document = readProjectXml(solution)
      assert.equal(document.name, 'Solution')
      const paths = []
      function collect(node) {
        if (node.name === 'Project') paths.push(node.attributes.Path?.replaceAll('\\', '/'))
        node.children.forEach(collect)
      }
      collect(document)
      assert.deepEqual(paths.sort(), config.projects.map(project => project.path).sort(), 'Solution and configured project inventories differ.')
    } else {
      return { state: 'not-run', message: 'Legacy .sln membership requires the build profile; .slnx supports data-only verification.' }
    }
  })
  await check('architecture.declarations', async () => {
    const facts = []
    const sharedFacts = []
    for (const shared of ['Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props']) {
      const text = await readAsset(root, shared)
      if (text) sharedFacts.push(projectReferences(shared, text))
    }
    for (const project of config.projects) {
      const source = await readAsset(root, project.path)
      assert.ok(source, `Missing project: ${project.path}`)
      const fact = projectReferences(project.path, source)
      for (const additions of sharedFacts) {
        fact.packages.push(...additions.packages)
        fact.projects.push(...additions.projects)
        fact.unevaluated.push(...additions.unevaluated)
      }
      let directory = posix.dirname(project.path)
      while (directory !== '.') {
        for (const name of ['Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props']) {
          if (await readAsset(root, `${directory}/${name}`)) fact.unevaluated.push(`${directory}/${name}`)
        }
        directory = posix.dirname(directory)
      }
      facts.push(fact)
    }
    const violations = layerViolations(config, facts)
    assert.equal(violations.length, 0, violations.map(value => `${value.path}: ${value.dependency} — ${value.reason}`).join('\n'))
    const unevaluated = facts.filter(fact => fact.unevaluated.length > 0)
    if (unevaluated.length) return { state: 'not-run', message: 'Conditional/imported project declarations require resolved build metadata.',
      locations: unevaluated.map(fact => fact.path) }
    return { message: 'Declared project/package edges satisfy configured layer roles. Startup/DI and resolved dependencies require execution profiles.' }
  })
  await check('packages.locked', async () => {
    for (const project of config.projects) {
      const path = `${posix.dirname(project.path)}/packages.lock.json`
      const text = await readAsset(root, path)
      assert.ok(text, `NuGet lockfile is missing: ${path}`)
      const lock = JSON.parse(text)
      assert.equal(lock.version, 1, `Unsupported NuGet lockfile: ${path}`)
      assert.ok(Object.keys(lock.dependencies ?? {}).length > 0, `Empty lockfile: ${path}`)
      for (const framework of Object.values(lock.dependencies)) {
        for (const [name, item] of Object.entries(framework)) {
          if (name.toLowerCase().startsWith('ngb.platform.')) assert.equal(item.resolved, version, `Mixed SDK package: ${name} in ${path}`)
        }
      }
    }
    const packageText = await readAsset(root, `${config.frontend.path}/package.json`)
    assert.ok(packageText, 'Frontend package.json is missing.')
    const manifest = JSON.parse(packageText)
    assert.equal(manifest.dependencies?.['@ngbplatform/ui'], version, 'Frontend must use the exact SDK version.')
    assert.ok(!manifest.workspaces, 'External applications must not resolve through monorepo workspaces.')
    const lockText = await readAsset(root, `${config.frontend.path}/package-lock.json`, 8 * 1024 * 1024)
    assert.ok(lockText, 'Frontend lockfile is missing.')
    const lock = JSON.parse(lockText)
    assert.ok(lock.lockfileVersion >= 3)
    assert.equal(lock.packages?.['']?.dependencies?.['@ngbplatform/ui'], version)
    const ui = lock.packages?.['node_modules/@ngbplatform/ui']
    assert.equal(ui?.version, version)
    assert.match(ui.integrity, /^sha512-[A-Za-z0-9+/]+=*$/)
    assert.equal(ui.resolved, `https://registry.npmjs.org/@ngbplatform/ui/-/ui-${version}.tgz`)
    identities.dependencySha256 = digest(packageText + lockText)
  })
  await check('tests.inventory', async () => {
    const declared = [...config.tests.unit, ...config.tests.integration].sort()
    const expected = config.projects.filter(project => project.role === 'tests').map(project => project.path).sort()
    assert.deepEqual(declared, expected, 'Every configured test project must belong to a mandatory profile.')
    for (const path of declared) {
      const text = await readAsset(root, path)
      assert.match(text, /<IsTestProject>true<\/IsTestProject>/, `Not a test project: ${path}`)
    }
    assert.ok(await readAsset(root, config.tests.e2e.config), 'Configured browser test suite is missing.')
    const web = JSON.parse(await readAsset(root, `${config.frontend.path}/package.json`))
    assert.ok(web.scripts?.test && web.scripts?.['test:e2e'], 'Frontend unit and E2E scripts are required.')
  })
  return report('validate', checks, identities)
}
