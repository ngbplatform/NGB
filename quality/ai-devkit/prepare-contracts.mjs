import assert from 'node:assert/strict'
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { basename, dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { parseArgs } from 'node:util'
import { unzipSync } from 'fflate'
import { assertArtifactIdentity, assertArtifactInventory } from '../external-consumer/release-contracts.mjs'
import { readNpmArchive } from '../../packaging/templates/content/infrastructure/ngb/archive.mjs'
import { digest, portablePath } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { assemblyMetadata } from '../../packaging/templates/content/infrastructure/ngb/resolved-data.mjs'
import { executionEnvironment, runTool } from '../../packaging/templates/content/infrastructure/ngb/process.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const { values } = parseArgs({ options: { packages: { type: 'string' }, write: { type: 'boolean' } } })
assert.ok(values.packages, 'Use --packages <sealed-candidate>; --write explicitly refreshes generated contract references.')
const candidate = resolve(values.packages)
const manifest = JSON.parse(await readFile(join(candidate, 'release-manifest.json'), 'utf8'))
const inventory = JSON.parse(await readFile(join(repository, 'packaging/nuget/baselines.json'), 'utf8'))
assertArtifactInventory(manifest, inventory)
const canonical = JSON.parse(await readFile(join(repository, 'ai-devkit/kit-source.json'), 'utf8'))
assert.equal(manifest.version, canonical.version)
const stage = await mkdtemp(join(tmpdir(), 'ngb-contract-reference-'))
const generated = new Map()
const artifacts = new Map()
const load = async path => {
  const identity = manifest.artifacts.find(item => item.path === path)
  assert.ok(identity, `Missing sealed artifact: ${path}`)
  const bytes = await readFile(join(candidate, path))
  assertArtifactIdentity(identity, bytes)
  artifacts.set(path, identity.sha256)
  return bytes
}
try {
  const env = executionEnvironment(join(stage, 'home'))
  const invoke = async args => {
    const result = await runTool('dotnet', args, { cwd: stage, env, maxBytes: 16 * 1024 * 1024 })
    assert.equal(result.state, 'passed', `Contract extraction failed: ${result.output}`)
    return result.output
  }
  assert.equal((await invoke(['--version'])).trim(), '10.0.401', 'Reference generation uses the pinned .NET SDK.')
  const template = unzipSync(await load(`nuget-release/NGB.Platform.Templates.${manifest.version}.nupkg`), {
    filter: entry => entry.name.startsWith('content/infrastructure/validation/'),
  })
  const helper = join(stage, 'helper')
  await mkdir(helper)
  for (const name of ['Ngb.Validation.csproj', 'AssemblyInspector.cs', 'Program.cs', 'packages.lock.json']) {
    const bytes = template[`content/infrastructure/validation/${name}`]
    assert.ok(bytes, `Template metadata helper is incomplete: ${name}`)
    assert.deepEqual(Buffer.from(bytes), await readFile(join(repository, 'packaging/templates/content/infrastructure/validation', name)),
      'Repack the template after changing the metadata helper.')
    await writeFile(join(helper, name), bytes)
  }
  await writeFile(join(stage, 'NuGet.Config'), '<configuration><packageSources><clear /></packageSources></configuration>\n')
  await invoke(['restore', 'helper/Ngb.Validation.csproj', '--locked-mode', '--configfile', 'NuGet.Config'])
  await invoke(['build', 'helper/Ngb.Validation.csproj', '--no-restore', '-c', 'Release', '-m:1',
    '-nodeReuse:false', '-p:UseSharedCompilation=false'])
  const inspector = join(helper, 'bin/Release/net10.0/Ngb.Validation.dll')
  const packages = []
  for (const item of inventory.packages.filter(item => item.kind === 'runtime').sort((a, b) => a.id.localeCompare(b.id, 'en'))) {
    const artifact = `nuget-release/${item.id}.${manifest.version}.nupkg`
    const bytes = await load(artifact)
    const entries = unzipSync(bytes, { filter: entry => {
      if (!/^lib\/net10\.0\/[^/]+\.dll$/.test(entry.name)) return false
      assert.ok(entry.originalSize <= 64 * 1024 * 1024, 'Managed assembly exceeds its extraction limit.')
      return true
    } })
    assert.equal(Object.keys(entries).length, 1, `Expected one public net10.0 assembly in ${item.id}.`)
    const [[path, assemblyBytes]] = Object.entries(entries)
    const filename = basename(path)
    await writeFile(join(stage, filename), assemblyBytes)
    const contract = JSON.parse(await invoke([inspector, '--assembly', join(stage, filename)]))
    assemblyMetadata(contract, basename(filename, '.dll'))
    const types = Object.entries(Object.groupBy(contract.publicContracts, member => member.type))
      .map(([name, members]) => ({
        name,
        attributes: members.find(member => member.kind === 'type').signature,
        contractSha256: digest(JSON.stringify(members)),
        methods: [...new Set(members.filter(member => member.kind === 'method').map(member => member.name))].sort(),
        fields: [...new Set(members.filter(member => member.kind === 'field').map(member => member.name))].sort(),
      })).sort((a, b) => a.name.localeCompare(b.name, 'en'))
    const snapshot = { schemaVersion: 1, package: item.id, version: manifest.version,
      packageSha256: artifacts.get(artifact), assembly: { name: contract.name, version: contract.version, sha256: contract.sha256 },
      contractSha256: digest(JSON.stringify(contract.publicContracts)), types }
    const reference = `references/contracts/${item.id}.json`
    generated.set(reference, `${JSON.stringify(snapshot, null, 2)}\n`)
    packages.push({ package: item.id, reference, types: types.length, contractSha256: snapshot.contractSha256 })
  }
  const uiArtifact = `npm/ngbplatform-ui-${manifest.version}.tgz`
  const ui = readNpmArchive(await load(uiArtifact))
  const metadata = JSON.parse(ui.get('package/package.json').toString('utf8'))
  assert.equal(metadata.name, '@ngbplatform/ui')
  assert.equal(metadata.version, manifest.version)
  const exports = []
  for (const [subpath, target] of Object.entries(metadata.exports)) {
    const paths = typeof target === 'string' ? [target] : Object.values(target)
    const files = [...new Set(paths)].map(path => {
      assert.ok(path.startsWith('./'), 'Public npm export must be package-relative.')
      portablePath(path.slice(2))
      const bytes = ui.get(`package/${path.slice(2)}`)
      assert.ok(bytes, `Missing public npm export: ${subpath}`)
      return { path, sha256: digest(bytes) }
    })
    exports.push({ subpath, files })
  }
  generated.set('references/contracts/ui.json', `${JSON.stringify({ schemaVersion: 1,
    package: metadata.name, version: metadata.version, packageSha256: artifacts.get(uiArtifact), exports }, null, 2)}\n`)
  generated.set('references/contracts/index.json', `${JSON.stringify({ schemaVersion: 1, version: manifest.version,
    purpose: 'Artifact-derived public symbol and export inventory. Public visibility alone does not establish a supported extension workflow.',
    generator: 'quality/ai-devkit/prepare-contracts.mjs', sdk: '10.0.401',
    packages, ui: 'references/contracts/ui.json',
    files: Object.fromEntries([...generated].map(([path, text]) => [path, digest(text)])) }, null, 2)}\n`)
  for (const [path, text] of generated) {
    assert.ok(Buffer.byteLength(text) <= 1024 * 1024, `Generated reference exceeds the Kit file limit: ${path}`)
    const destination = join(repository, 'ai-devkit', path)
    if (values.write) {
      await mkdir(dirname(destination), { recursive: true })
      await writeFile(destination, text)
    } else {
      assert.equal(await readFile(destination, 'utf8'), text, `Public contract drift: ${path}. Review affected capabilities before --write.`)
    }
  }
  console.log(`Verified ${packages.length} package assemblies, ${packages.reduce((count, item) => count + item.types, 0)} public types and ${exports.length} npm entry points.`)
} finally {
  await rm(stage, { recursive: true, force: true })
}
