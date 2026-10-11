import assert from 'node:assert/strict'
import test from 'node:test'
import {
  argumentsFor, assertDeployment, exactVersion, jsonText, localNugetConfig, planUpgrade, registryLockfile, withManagedIgnores,
} from '../../packaging/templates/content/infrastructure/ngb/contracts.mjs'

function application() {
  return {
    'Directory.Build.props': '<Project><PropertyGroup><NgbPlatformVersion>3.1.0</NgbPlatformVersion></PropertyGroup></Project>',
    'App.Api/App.Api.csproj': '<Project><PackageReference Include="NGB.Platform.Api" Version="$(NgbPlatformVersion)" /></Project>',
    'web/package.json': JSON.stringify({ dependencies: { '@ngbplatform/ui': '3.1.0', vue: '^3.5.0' }, scripts: { build: 'vite build' } }),
    'App.Api/Program.cs': '// user code',
  }
}

test('commands require explicit supported arguments and safe application names', () => {
  assert.equal(argumentsFor([]).command, 'help')
  assert.equal(argumentsFor(['--help']).command, 'help')
  assert.equal(argumentsFor(['create', 'MyApp', '--local', '--start']).name, 'MyApp')
  assert.equal(argumentsFor(['create', 'MyApp', '--version', '3.2.0']).version, '3.2.0')
  assert.equal(argumentsFor(['upgrade', '--to', '3.2.0', '--apply']).apply, true)
  for (const args of [
    ['unknown'], ['start', '--apply'], ['create'], ['create', '../escape'],
    ['upgrade'], ['upgrade', '--to', '^3.2.0'], ['create', 'MyApp', '--version', 'latest'],
    ['start', 'extra'], ['start', '--unknown'],
  ]) assert.throws(() => argumentsFor(args))
  assert.throws(() => exactVersion('3.2.0-preview.1'))
})

test('managed ignores are additive and idempotent', () => {
  const original = 'custom/\n.ngb/\n'
  const updated = withManagedIgnores(original)
  assert.ok(updated.startsWith(original))
  assert.equal(updated.split('.ngb/').length, 2)
  assert.match(updated, /NuGet.Local.Config/)
  assert.equal(withManagedIgnores(updated), updated)
})

test('upgrade preserves application code and changes only registered package versions', () => {
  const files = application()
  files['App.Api/App.Api.csproj'] += `
    <!-- <PackageReference Include="NGB.Platform.Api" Version="2.0.0" /> -->
    <PackageReference Include="NGB.Platform.Core" Version="[3.1.0]" />
    <PackageReference Include="NGB.Platform.Contracts" Version='3.1.0' />
    <PackageReference Include="NGB.Platform.Runtime" Version="[$(NgbPlatformVersion)]" />
    <PackageReference Include="Dapper" Version="2.1.89" />`
  const original = structuredClone(files)
  const plan = planUpgrade(files, '3.2.0')
  assert.equal(plan.source, '3.1.0')
  assert.equal(plan.target, '3.2.0')
  assert.deepEqual(files, original)
  assert.equal(plan.changes['App.Api/Program.cs'], undefined)
  assert.match(plan.changes['App.Api/App.Api.csproj'], /Version="\[3.2.0\]"/)
  assert.match(plan.changes['App.Api/App.Api.csproj'], /Version='3.2.0'/)
  assert.match(plan.changes['App.Api/App.Api.csproj'], /Version="2.0.0"/)
  assert.match(plan.changes['App.Api/App.Api.csproj'], /Dapper" Version="2.1.89"/)
  assert.equal(JSON.parse(plan.changes['web/package.json']).scripts.build, 'vite build')
})

test('central package declarations and unrelated conditional groups remain usable', () => {
  const files = application()
  files['Directory.Build.props'] = '<Project><PropertyGroup Condition="true"><Other>true</Other></PropertyGroup><PropertyGroup><NgbPlatformVersion>3.1.0</NgbPlatformVersion></PropertyGroup></Project>'
  files['App.Api/App.Api.csproj'] = '<PackageReference Include="NGB.Platform.Api" />'
  files['Directory.Packages.props'] = '<PackageVersion Include="NGB.Platform.Api" Version="3.1.0" />'
  assert.match(planUpgrade(files, '3.2.0').changes['Directory.Packages.props'], /3.2.0/)
})

test('shared props update the platform property and direct references together', () => {
  const files = application()
  files['Directory.Build.props'] = files['Directory.Build.props'].replace('</Project>',
    '<ItemGroup><PackageReference Include="NGB.Platform.Core" Version="3.1.0" /></ItemGroup></Project>')
  const updated = planUpgrade(files, '3.2.0').changes['Directory.Build.props']
  assert.match(updated, /<NgbPlatformVersion>3.2.0<\/NgbPlatformVersion>/)
  assert.match(updated, /NGB.Platform.Core" Version="3.2.0"/)
  assert.ok(!updated.includes('3.1.0'))
})

test('unsupported, ambiguous or mixed source versions fail before any mutation', () => {
  for (const change of [
    files => { delete files['Directory.Build.props'] },
    files => { files['Directory.Build.props'] += files['Directory.Build.props'] },
    files => { files['Directory.Build.props'] = files['Directory.Build.props'].replace('3.1.0', '3.0.0') },
    files => { files['Directory.Build.props'] = files['Directory.Build.props'].replace('<PropertyGroup>', '<PropertyGroup Condition="true">') },
    files => { files['App.Api/App.Api.csproj'] = '<Project />' },
    files => { files['App.Api/App.Api.csproj'] = '<PackageReference Include="NGB.Platform.Api" />' },
    files => { files['App.Api/App.Api.csproj'] = '<PackageReference Include="NGB.Platform.Api" Version="3.0.0" />' },
    files => { delete files['web/package.json'] },
    files => { files['web/package.json'] = '{}' },
    files => { files['web/package.json'] = '{"dependencies":{"@ngbplatform/ui":"^3.1.0"}}' },
    files => { files['web/package.json'] = '{"dependencies":{"@ngbplatform/ui":"3.1.0"},"workspaces":["*"]}' },
  ]) {
    const files = application()
    change(files)
    assert.throws(() => planUpgrade(files, '3.2.0'))
  }
  assert.throws(() => planUpgrade(application(), '4.0.0'), /No automated migration/)
})

test('local feed routing preserves other sources and credentials without changing the original config', () => {
  const config = `<configuration>
  <packageSources><clear /><add key="private" value="https://example.test/v3/index.json" /></packageSources>
  <packageSourceMapping><packageSource key="private"><package pattern="NGB.Platform.Core" /><package pattern="Company.*" /></packageSource></packageSourceMapping>
  <packageSourceCredentials><private><add key="Username" value="user" /></private></packageSourceCredentials>
</configuration>`
  const result = localNugetConfig(config)
  assert.match(result, /value=".ngb\/nuget"/)
  assert.match(result, /pattern="Company.\*"/)
  assert.match(result, /packageSourceCredentials/)
  assert.ok(!result.includes('pattern="NGB.Platform.Core"'))
  assert.throws(() => localNugetConfig('<configuration />'), /packageSources/)
  assert.throws(() => localNugetConfig('<packageSources></packageSources>'), /packageSourceMapping/)
  assert.throws(() => localNugetConfig(result), /reserved/)
})

test('lockfiles keep registry identity, peer metadata, newline style and unrelated dependencies', () => {
  const original = JSON.stringify({ lockfileVersion: 3, packages: {
    '': { dependencies: { '@ngbplatform/ui': 'file:ui.tgz', vue: '^3.5.0' } },
    'node_modules/@ngbplatform/ui': { version: '3.2.0', resolved: 'file:ui.tgz', peerDependencies: { vue: '^3.5.0' } },
  } }, null, '\t').replaceAll('\n', '\r\n')
  const updated = registryLockfile(original, '3.2.0', 'sha512-archive')
  const lock = JSON.parse(updated)
  assert.match(updated, /\r\n\t"/)
  assert.equal(lock.packages[''].dependencies.vue, '^3.5.0')
  assert.equal(lock.packages[''].dependencies['@ngbplatform/ui'], '3.2.0')
  assert.equal(lock.packages['node_modules/@ngbplatform/ui'].integrity, 'sha512-archive')
  assert.deepEqual(lock.packages['node_modules/@ngbplatform/ui'].peerDependencies, { vue: '^3.5.0' })
  assert.throws(() => registryLockfile(original, '3.3.0', 'sha512-other'))
  assert.throws(() => registryLockfile('{"lockfileVersion":2}', '3.2.0', 'sha512-other'))
  assert.equal(jsonText({ value: true }, '{}'), '{\n  "value": true\n}\n')
})

test('deployment requires the supported topology and migration ordering', () => {
  const services = Object.fromEntries(['api', 'jobs', 'web', 'migrator'].map(name => [name, {
    build: { context: '.' }, depends_on: { migrator: { condition: 'service_completed_successfully' } },
  }]))
  assertDeployment(services)
  assert.throws(() => assertDeployment({}), /service api/)
  services.api.depends_on = {}
  assert.throws(() => assertDeployment(services), /successful migration/)
  delete services.api.depends_on
  assert.throws(() => assertDeployment(services), /successful migration/)
})
