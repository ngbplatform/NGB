#!/usr/bin/env node

import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { main } from '../../packaging/templates/content/infrastructure/ngb/coverage.mjs'

export { classifyCoverageExclusion, evaluateCoverage, parseCobertura, mergeCoverage } from '../../packaging/templates/content/infrastructure/ngb/coverage.mjs'

if (process.argv[1] && fileURLToPath(import.meta.url) === resolve(process.argv[1])) {
  await main()
}
