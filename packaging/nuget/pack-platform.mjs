import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { prepareToolchain } from '../../quality/external-consumer/environment.mjs'
import { command } from '../../quality/external-consumer/execution.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const prepared = await prepareToolchain()
await command('bash', ['packaging/nuget/pack-platform.sh', '--prepared-toolchain', ...process.argv.slice(2)], {
  cwd: repository, env: prepared.env,
})
