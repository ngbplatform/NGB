#!/usr/bin/env node

import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { runCli } from './packaging/templates/content/infrastructure/ngb/cli.mjs'

const repository = dirname(fileURLToPath(import.meta.url))

await runCli(process.argv.slice(2), {
  repository,
  outputDirectory: dirname(repository),
  packagesDirectory: resolve(repository, 'artifacts/release-candidate'),
})
