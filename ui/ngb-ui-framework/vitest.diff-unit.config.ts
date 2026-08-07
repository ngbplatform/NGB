import { dirname } from 'node:path'
import { fileURLToPath } from 'node:url'

import { defineConfig } from 'vitest/config'

const packageRoot = dirname(fileURLToPath(import.meta.url))

export default defineConfig({
  root: packageRoot,
  test: {
    name: '@ngbplatform/ui-diff-unit',
    environment: 'node',
    include: ['tests/unit/**/*.spec.ts'],
    exclude: [
      'tests/browser/**/*.browser.spec.ts',
      // This unrelated negative import-time auth test intentionally removes
      // browser-defined env. It remains mandatory in the normal unit suite,
      // but cannot share the merged transform cache with browser coverage.
      'tests/unit/ngb/auth/keycloak.spec.ts',
    ],
    reporters: ['default'],
  },
})
