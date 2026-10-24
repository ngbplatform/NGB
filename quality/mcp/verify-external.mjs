import { execFileSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { cp, mkdir, mkdtemp, readFile, readdir, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { basename, dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const directory = dirname(fileURLToPath(import.meta.url))
const repository = resolve(directory, '../..')
const feed = resolve(process.argv[2] ?? join(repository, 'artifacts/nuget'))
const work = await mkdtemp(join(tmpdir(), 'ngb-mcp-external-'))
const evidence = resolve(process.argv[3] ?? join(repository, 'artifacts/mcp/external'))
const performance = join(evidence, 'performance', basename(work))
await mkdir(evidence, { recursive: true })
await cp(join(directory, 'external'), work, { recursive: true })
await writeFile(join(work, 'Directory.Build.props'), '<Project />\n')
await writeFile(join(work, 'Directory.Build.targets'), '<Project />\n')
const escapeXml = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;')
await writeFile(join(work, 'NuGet.Config'), `<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <config><add key="globalPackagesFolder" value="${escapeXml(join(work, 'packages'))}" /></config>
  <packageSources><clear />
    <add key="candidate" value="${escapeXml(feed)}" />
    <add key="registry" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping><clear />
    <packageSource key="candidate"><package pattern="NGB.Platform.*" /></packageSource>
    <packageSource key="registry"><package pattern="*" /></packageSource>
  </packageSourceMapping>
  <fallbackPackageFolders><clear /></fallbackPackageFolders>
</configuration>\n`)

const archives = (await readdir(feed)).filter(name => /^NGB\.Platform\..*\.3\.3\.0\.nupkg$/.test(name)).sort()
if (!archives.includes('NGB.Platform.Mcp.3.3.0.nupkg') || !archives.includes('NGB.Platform.Mcp.AspNetCore.3.3.0.nupkg')) {
  throw new Error('Both MCP candidate packages must exist before external verification.')
}
const packages = await Promise.all(archives.map(async name => ({
  name,
  sha256: createHash('sha256').update(await readFile(join(feed, name))).digest('hex'),
})))
await writeFile(join(evidence, 'inputs.json'), `${JSON.stringify({ work, feed, packages, status: 'running' }, null, 2)}\n`)
console.log(`Source-free MCP consumer: ${work}`)
try {
  execFileSync('dotnet', ['test', 'ExternalMcp.Tests.csproj', '-m:1', '-nr:false', '--verbosity', 'minimal',
    '--logger', 'trx;LogFileName=external-mcp.trx', '--results-directory', evidence], {
    cwd: work,
    env: { ...process.env, NUGET_PACKAGES: join(work, 'packages'), NGB_MCP_PERFORMANCE_DIR: performance },
    stdio: 'inherit',
  })
  const assets = JSON.parse(await readFile(join(work, 'obj/project.assets.json'), 'utf8'))
  if (Object.values(assets.libraries).some(library => library.type === 'project')) {
    throw new Error('External verification must not resolve NGB source projects.')
  }
  const samples = (await readFile(join(performance, 'ExternalMcp.Tests.jsonl'), 'utf8')).trim().split('\n').map(line => JSON.parse(line))
  const scenarios = Object.fromEntries([...new Set(samples.map(sample => sample.scenario))].map(scenario => {
    const values = samples.filter(sample => sample.scenario === scenario)
    const times = values.map(sample => sample.ms).sort((left, right) => left - right)
    const percentile = fraction => times[Math.max(0, Math.ceil(times.length * fraction) - 1)]
    return [scenario, { samples: times.length, medianMs: percentile(0.5), p95Ms: percentile(0.95), p99Ms: percentile(0.99),
      minQueries: Math.min(...values.map(sample => sample.sqlCommands)), maxQueries: Math.max(...values.map(sample => sample.sqlCommands)) }]
  }))
  await writeFile(join(evidence, 'performance.json'), `${JSON.stringify({ status: 'passed', raw: performance, scenarios }, null, 2)}\n`)
  await writeFile(join(evidence, 'inputs.json'), `${JSON.stringify({ work, feed, packages, performance, status: 'passed' }, null, 2)}\n`)
} catch (error) {
  await writeFile(join(evidence, 'inputs.json'), `${JSON.stringify({ work, feed, packages, status: 'failed' }, null, 2)}\n`)
  throw error
}
