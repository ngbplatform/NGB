import { mkdir, mkdtemp, writeFile } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { command } from '../external-consumer/execution.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const output = resolve(process.argv[2] ?? join(repository, 'artifacts/mcp/verticals'))
await mkdir(output, { recursive: true })
const performance = await mkdtemp(join(output, 'performance-'))
const evidence = { status: 'running', performance, results: {} }
try {
  for (const vertical of ['PropertyManagement', 'Trade', 'AgencyBilling', 'CRM']) {
    const project = `NGB.${vertical}.Api.IntegrationTests/NGB.${vertical}.Api.IntegrationTests.csproj`
    await command('dotnet', ['test', project, '-m:1', '-nr:false', '--verbosity', 'minimal',
      '--filter', 'FullyQualifiedName~McpVerticalTests', '--logger', `trx;LogFileName=${vertical}.trx`, '--results-directory', output],
    { cwd: repository, env: { ...process.env, NGB_MCP_PERFORMANCE_DIR: performance } })
    evidence.results[vertical] = { status: 'passed', project }
  }
  evidence.status = 'passed'
} catch (error) {
  evidence.status = 'failed'
  throw error
} finally {
  await writeFile(join(output, 'verticals.json'), `${JSON.stringify(evidence, null, 2)}\n`)
}
