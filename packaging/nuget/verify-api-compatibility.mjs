import { execFileSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { mkdir, mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { dependencyCache } from '../../quality/external-consumer/dependency-cache.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const inventory = JSON.parse(await readFile(new URL('./baselines.json', import.meta.url), 'utf8'))
const candidateFeed = resolve(process.argv[2] ?? join(repository, 'artifacts/nuget-release'))
const root = await mkdtemp(join(tmpdir(), 'ngb-api-compatibility-'))
const results = []
const runtimes = inventory.packages.filter(item => item.kind === 'runtime')
const versions = [...new Set(runtimes.flatMap(item => [item.firstStableInMajor, item.previous]))]
const xml = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;')

if (!process.env.NGB_APICOMPAT) {
  execFileSync('dotnet', ['tool', 'restore'], { cwd: repository, stdio: 'inherit' })
}

async function restoreReferences(version, packages, candidate = false) {
  const directory = join(root, version)
  await mkdir(directory)
  await writeFile(join(directory, 'Directory.Build.props'), '<Project />\n')
  await writeFile(join(directory, 'Directory.Build.targets'), '<Project />\n')
  await writeFile(join(directory, 'ReferenceGraph.csproj'), `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultItems>false</EnableDefaultItems></PropertyGroup>
  <ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" />
${packages.map(item => `    <PackageReference Include="${item.id}" Version="[${version}]" />`).join('\n')}
  </ItemGroup>
</Project>\n`)
  await writeFile(join(directory, 'NuGet.Config'), `<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear />
    ${candidate ? `<add key="candidate" value="${xml(candidateFeed)}" />` : ''}
    <add key="registry" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping><clear />
    ${candidate ? '<packageSource key="candidate"><package pattern="NGB.Platform.*" /></packageSource>' : ''}
    <packageSource key="registry"><package pattern="*" /></packageSource>
  </packageSourceMapping>
  <fallbackPackageFolders><clear /></fallbackPackageFolders>
</configuration>\n`)
  const env = { ...process.env, NUGET_PACKAGES: join(directory, 'packages'), NUGET_HTTP_CACHE_PATH: dependencyCache(directory, version).nugetHttp }
  execFileSync('dotnet', ['restore', 'ReferenceGraph.csproj', '--configfile', 'NuGet.Config', '--verbosity', 'quiet'], { cwd: directory, env, stdio: 'inherit' })
  const resolved = JSON.parse(execFileSync('dotnet', ['msbuild', 'ReferenceGraph.csproj', '-nologo', '-target:ResolveReferences', '-getItem:ReferencePath', '-nodeReuse:false'], { cwd: directory, env, encoding: 'utf8', maxBuffer: 32 * 1024 * 1024 }))
  return { directory, references: resolved.Items.ReferencePath.map(item => item.FullPath).join(',') }
}

const candidate = await restoreReferences(inventory.target, runtimes, true)
for (const version of versions) {
  const packages = runtimes.filter(item => item.firstStableInMajor === version || item.previous === version)
  const baseline = await restoreReferences(version, packages)
  for (const item of packages) {
    const id = item.id.toLowerCase()
    const oldArchive = join(baseline.directory, 'packages', id, version, `${id}.${version}.nupkg`)
    const newArchive = join(candidateFeed, `${item.id}.${inventory.target}.nupkg`)
    const args = ['package', newArchive, '--baseline-package', oldArchive,
      '--package-assembly-references', `net10.0|${candidate.references}`,
      '--baseline-package-assembly-references', `net10.0|${baseline.references}`,
      '--enable-rule-cannot-change-parameter-name', '--enable-rule-attributes-must-match']
    const tool = process.env.NGB_APICOMPAT
    const output = execFileSync(tool ?? 'dotnet', tool ? args : ['tool', 'run', 'apicompat', '--', ...args], {
      cwd: repository, encoding: 'utf8', maxBuffer: 32 * 1024 * 1024,
    })
    if (/Could not resolve|could not be resolved|warning\s+(?:CP|PKV)/i.test(output)) {
      throw new Error(`Unresolved compatibility diagnostic for ${item.id}: ${output}`)
    }
    const sha256 = async filename => createHash('sha256').update(await readFile(filename)).digest('hex')
    results.push({ package: item.id, baseline: version, baselineSha256: await sha256(oldArchive), candidateSha256: await sha256(newArchive), status: 'passed' })
    console.log(`Compatible: ${item.id} ${version} -> ${inventory.target}`)
  }
}
await writeFile(join(candidateFeed, 'api-compatibility.json'), `${JSON.stringify({ schemaVersion: 1, target: inventory.target, results }, null, 2)}\n`)
console.log(`Verified ${results.length} comparisons using isolated public registry baselines.`)
