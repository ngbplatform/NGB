import { mkdir, writeFile } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { command } from '../external-consumer/execution.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const output = resolve(process.argv[2] ?? join(repository, 'artifacts/mcp/contracts'))
await mkdir(output, { recursive: true })
let status = 'failed'
const environment = { ...process.env }
delete environment.NGB_MCP_SCHEMA_OUTPUT
try {
  for (const project of ['NGB.Mcp.Tests', 'NGB.Mcp.AspNetCore.IntegrationTests']) {
    await command('dotnet', ['test', `${project}/${project}.csproj`, '-m:1', '-nr:false', '--verbosity', 'minimal',
      '--logger', `trx;LogFileName=${project}.trx`, '--results-directory', output], { cwd: repository, env: environment })
  }
  status = 'passed'
} finally {
  await writeFile(join(output, 'contracts.json'), `${JSON.stringify({ status }, null, 2)}\n`)
}
