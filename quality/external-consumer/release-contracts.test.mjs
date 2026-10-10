import assert from 'node:assert/strict'
import { mkdir, mkdtemp, readFile, rm, symlink, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import test from 'node:test'
import { strToU8, zipSync } from 'fflate'
import {
  artifactIdentity, assertArtifactIdentity, assertArtifactInventory, assertTemplatePackage, canonicalNugetPayload,
  digest, templateContentExcludes, validateMatrix,
} from './release-contracts.mjs'

const zip = entries => zipSync(Object.fromEntries(Object.entries(entries).map(([name, text]) => [name, strToU8(text)])))

async function templateFixture(t) {
  const root = await mkdtemp(join(tmpdir(), 'ngb-template-payload-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  const files = {
    'README.md': 'Current instructions\n',
    '.gitignore': '.env\n',
    '.template.config/template.json': '{"identity":"NGB"}',
    'web/package-lock.json': '{"lockfileVersion":3}',
    'Api/packages.lock.json': '{"version":1}',
    'Api/Program.cs': 'Console.WriteLine("Application");\n',
  }
  for (const [path, text] of Object.entries(files)) {
    await mkdir(dirname(join(root, path)), { recursive: true })
    await writeFile(join(root, path), text)
  }
  const entries = Object.fromEntries(Object.entries(files).map(([path, text]) => [`content/${path}`, text]))
  entries['NGB.Platform.Templates.nuspec'] = '<package />'
  entries['content/'] = ''
  return { root, entries }
}

test('template package comparison uses the pack exclusions and includes hidden files and lockfiles', async t => {
  const { root, entries } = await templateFixture(t)
  const project = await readFile(new URL('../../packaging/templates/NGB.Platform.Templates.csproj', import.meta.url), 'utf8')
  assert.match(project, /<Content Include="content\/\*\*\/\*"/)
  assert.match(project, /<NoDefaultExcludes>true<\/NoDefaultExcludes>/)
  assert.deepEqual(project.match(/Exclude="([^"]+)"/)[1].split(';'), templateContentExcludes.map(path => `content/${path}`))
  for (const path of ['bin/output', 'Api/obj/output', 'web/node_modules/dependency', 'web/dist/output', '.env', '.local/config']) {
    await mkdir(dirname(join(root, path)), { recursive: true })
    await writeFile(join(root, path), 'not packed')
  }
  await assertTemplatePackage(zip(entries), root)
  await assertTemplatePackage(zip({ ...entries, '.signature.p7s': 'signature', '[Content_Types].xml': '<Types />' }), root)
})

test('template packages reject stale README, source, lockfiles and missing or extra content', async t => {
  const { root, entries } = await templateFixture(t)
  for (const path of ['README.md', 'Api/Program.cs', 'web/package-lock.json', 'Api/packages.lock.json']) {
    await assert.rejects(assertTemplatePackage(zip({ ...entries, [`content/${path}`]: 'old' }), root), error => {
      assert.ok(error.message.includes(path))
      assert.match(error.message, /pack-platform\.sh/)
      return true
    })
  }
  for (const path of ['.gitignore', '.template.config/template.json']) {
    const missing = { ...entries }
    delete missing[`content/${path}`]
    await assert.rejects(assertTemplatePackage(zip(missing), root), /does not match current sources/)
  }
  for (const path of ['deleted.txt', '.env', 'web/node_modules/dependency']) {
    await assert.rejects(assertTemplatePackage(zip({ ...entries, [`content/${path}`]: 'unexpected' }), root), /does not match current sources/)
  }
  await assert.rejects(assertTemplatePackage(zip({ 'A.nuspec': '<package />' }), root), /does not match current sources/)
  await assert.rejects(assertTemplatePackage(Buffer.from('invalid archive'), root))
})

test('template verification refuses unsafe and duplicate archive paths and linked sources', async t => {
  const { root, entries } = await templateFixture(t)
  for (const path of ['content/../README.md', 'content/Api\\Program.cs', 'content/./README.md', 'content//README.md']) {
    await assert.rejects(assertTemplatePackage(zip({ ...entries, [path]: 'unexpected' }), root), /Unsafe/)
  }
  const duplicate = Buffer.from(zip({ ...entries, 'content/README.xx': 'different bytes' }))
  const alias = Buffer.from('content/README.xx')
  let offset = duplicate.indexOf(alias)
  while (offset !== -1) {
    Buffer.from('content/README.md').copy(duplicate, offset)
    offset = duplicate.indexOf(alias, offset + alias.length)
  }
  await assert.rejects(assertTemplatePackage(duplicate, root), /Duplicate/)
  await symlink(join(root, 'README.md'), join(root, 'linked.md'))
  await assert.rejects(assertTemplatePackage(zip(entries), root), /Symbolic links/)
  const empty = await mkdtemp(join(tmpdir(), 'ngb-template-empty-'))
  t.after(() => rm(empty, { recursive: true, force: true }))
  await assert.rejects(assertTemplatePackage(zip(entries), empty), /source directory is empty/)
})

test('signed NuGet identity ignores only the signature and archive representation', () => {
  const original = zip({ 'A.nuspec': '<package />', 'lib/net10.0/A.dll': 'assembly', 'empty/': '' })
  const signed = zip({ '.signature.p7s': 'registry signature', 'lib/net10.0/A.dll': 'assembly', 'A.nuspec': '<package />' })
  const expected = artifactIdentity('nuget-release/A.3.2.0.nupkg', original)
  assert.equal(canonicalNugetPayload(original), canonicalNugetPayload(signed))
  assertArtifactIdentity(expected, original)
  assertArtifactIdentity(expected, signed, true)
  assert.throws(() => assertArtifactIdentity(expected, signed), /bytes differ/)
  assert.throws(() => assertArtifactIdentity(expected, zip({ 'A.nuspec': '<package />', 'lib/net10.0/A.dll': 'changed' }), true), /payload differs/)
  assert.throws(() => canonicalNugetPayload(zip({})), /empty/)
  assert.throws(() => canonicalNugetPayload(zip({ a: 'a' })), /nuspec/)
  for (const path of ['/root', '../escape', 'lib\\escape']) {
    assert.throws(() => canonicalNugetPayload(zip({ 'A.nuspec': 'a', [path]: 'b' })), /Unsafe/)
  }
  assert.throws(() => artifactIdentity('../outside.tgz', original), /path/)
})

test('npm requires exact bytes and SHA-512 integrity; symbols require exact bytes', () => {
  const npm = artifactIdentity('npm/ngbplatform-ui-3.2.0.tgz', Buffer.from('npm'))
  assert.equal(npm.integrity, `sha512-${digest('npm', 'sha512', 'base64')}`)
  assertArtifactIdentity(npm, Buffer.from('npm'), true)
  assert.throws(() => assertArtifactIdentity(npm, Buffer.from('other'), true))
  assert.throws(() => assertArtifactIdentity({ ...npm, integrity: 'sha512-wrong' }, Buffer.from('npm')))
  const symbols = zip({ 'A.nuspec': 'symbols' })
  assertArtifactIdentity(artifactIdentity('nuget-release/A.3.2.0.snupkg', symbols), symbols, true)
})

test('artifact inventory classifies templates separately and rejects missing or extra artifacts', () => {
  const inventory = { target: '3.2.0', packages: [{ id: 'A', kind: 'runtime' }, { id: 'T', kind: 'template' }] }
  const manifest = { schemaVersion: 1, version: '3.2.0', artifacts: [
    'nuget-release/A.3.2.0.nupkg', 'nuget-release/A.3.2.0.snupkg', 'nuget-release/T.3.2.0.nupkg', 'npm/ngbplatform-ui-3.2.0.tgz',
  ].map(path => ({ path })) }
  assertArtifactInventory(manifest, inventory)
  assert.throws(() => assertArtifactInventory({ ...manifest, artifacts: manifest.artifacts.slice(1) }, inventory))
  assert.throws(() => assertArtifactInventory({ ...manifest, artifacts: [...manifest.artifacts, { path: 'extra' }] }, inventory))
  assert.throws(() => assertArtifactInventory({ ...manifest, version: '3.3.0' }, inventory))
})

test('matrix binds all mandatory gates, stages, exact versions and profiles', async () => {
  const matrix = JSON.parse(await readFile(new URL('./matrix.json', import.meta.url), 'utf8'))
  validateMatrix(matrix)
  for (const mutate of [
    value => { value.source = '^3.1.0' },
    value => { value.source = value.target },
    value => { delete value.profiles['clean-starter'] },
    value => { value.gates.pop() },
    value => { value.gates[0].blocks = 'anything' },
    value => { value.gates[0].blocks = 'promotion' },
    value => { value.gates[0].command = '' },
    value => { value.gates[0].assertions = [] },
    value => { value.profiles['clean-starter'].assertions = [] },
    value => { value.profiles['clean-starter'].target = '3.3.0' },
    value => { value.profiles['core-upgrade'].source = '3.0.0' },
    value => { value.profiles['notes-upgrade'].minio = true },
    value => { value.profiles['attachments-upgrade'].minio = false },
  ]) {
    const invalid = structuredClone(matrix)
    mutate(invalid)
    assert.throws(() => validateMatrix(invalid))
  }
})
