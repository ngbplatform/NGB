import assert from 'node:assert/strict'
import test from 'node:test'
import { assemblyMetadata, resolvedProject } from '../../packaging/templates/content/infrastructure/ngb/resolved-data.mjs'
import { layerViolations } from '../../packaging/templates/content/infrastructure/ngb/project-data.mjs'

const project = { path: 'App.Runtime/App.Runtime.csproj', role: 'runtime' }
function assets() {
  return { version: 4, packageFolders: { '/cache/': {} }, project: {
    restore: { projectPath: `/app/${project.path}`, packagesPath: '/cache/', sources: { 'https://api.nuget.org/v3/index.json': {} } },
    frameworks: { 'net10.0': {} },
  }, libraries: { 'NGB.Platform.Accounting/3.4.0': { type: 'package', sha512: 'a'.repeat(86) + '==' },
    'App.Definitions/1.0.0': { type: 'project', msbuildProject: '../App.Definitions/App.Definitions.csproj' } } }
}

test('resolved dependencies enforce actual package versions and provider boundaries', () => {
  const value = assets()
  const facts = resolvedProject('/app', project, value, '3.4.0', '/cache')
  const config = { projects: [project, { path: 'App.Definitions/App.Definitions.csproj', role: 'definitions' }] }
  assert.deepEqual(layerViolations(config, [facts]), [])
  value.project.restore.sources['/app/.ngb/nuget'] = {}
  value.project.frameworks['net10.0'].frameworkReferences = { 'Microsoft.AspNetCore.App': {} }
  value.libraries['Npgsql/10.0.3'] = { type: 'package', sha512: 'b'.repeat(86) + '==' }
  assert.equal(layerViolations(config, [resolvedProject('/app', project, value, '3.4.0', '/cache')]).length, 2)
})

test('resolved facts reject foreign caches, sources, projects, identities and incomplete evidence', () => {
  for (const change of [
    value => { value.version = 99 }, value => { delete value.project.restore.projectPath },
    value => { value.project.restore.projectPath = '/other/app.csproj' },
    value => { value.project.restore.packagesPath = '/personal/cache' },
    value => { value.packageFolders = {} }, value => { delete value.packageFolders },
    value => { value.project.restore.sources = {} }, value => { delete value.project.restore.sources },
    value => { value.project.restore.sources = { 'https://unapproved.test': {} } },
    value => { value.libraries['broken'] = { type: 'package' } },
    value => { value.libraries['NGB.Platform.Accounting/3.4.0'].sha512 = 'bad' },
    value => { value.libraries['ngb.platform.core/3.3.0'] = { type: 'package', sha512: 'a'.repeat(86) + '==' } },
    value => { value.libraries['Unknown/1'].type = 'other' },
    value => { value.libraries['App.Definitions/1.0.0'].msbuildProject = '/outside.csproj' },
    value => { value.libraries['App.Definitions/1.0.0'].msbuildProject = '../../outside.csproj' },
    value => { delete value.project.frameworks },
  ]) {
    const value = assets()
    value.libraries['Unknown/1'] = { type: 'package', sha512: 'a'.repeat(86) + '==' }
    change(value)
    assert.throws(() => resolvedProject('/app', project, value, '3.4.0', '/cache'))
  }
  const empty = assets()
  delete empty.libraries
  assert.deepEqual(resolvedProject('/app', project, empty, '3.4.0', '/cache').packages, [])
  const legacy = assets()
  legacy.version = 3
  assert.ok(resolvedProject('/app/', project, legacy, '3.4.0', '/cache'))
})

test('assembly summaries require actual identity and bounded public metadata', () => {
  const value = { name: 'App.Runtime', version: '1.0.0.0', sha256: 'a'.repeat(64), references: ['System.Runtime'],
    publicContracts: [{ type: 'App.Service', kind: 'method', name: 'Run', signature: '000001' }] }
  assert.equal(assemblyMetadata(value, 'App.Runtime').publicContractCount, 1)
  for (const change of [
    item => { item.name = 'Other' }, item => { item.version = '1.0' }, item => { item.sha256 = 'bad' },
    item => { item.references = ['../other'] }, item => { item.references = null },
    item => { item.publicContracts = null }, item => { item.publicContracts[0].kind = 'unknown' },
    item => { item.publicContracts[0].name = '' },
  ]) {
    const invalid = structuredClone(value)
    change(invalid)
    assert.throws(() => assemblyMetadata(invalid, 'App.Runtime'))
  }
  assert.throws(() => assemblyMetadata(null, 'App.Runtime'))
})
