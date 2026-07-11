import { dirname } from 'node:path'
import { fileURLToPath } from 'node:url'

import { defineConfig } from 'vitest/config'

const packageRoot = dirname(fileURLToPath(import.meta.url))

export default defineConfig({
  root: packageRoot,
  test: {
    name: 'ngb-ui-framework',
    environment: 'node',
    include: ['tests/unit/**/*.spec.ts'],
    exclude: ['tests/browser/**/*.browser.spec.ts'],
    reporters: ['default'],
  },
})
