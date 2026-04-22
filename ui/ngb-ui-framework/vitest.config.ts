import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    name: 'ngb-ui-framework',
    environment: 'node',
    include: ['tests/unit/**/*.spec.ts'],
    exclude: ['tests/browser/**/*.browser.spec.ts'],
    reporters: ['default'],
  },
})
