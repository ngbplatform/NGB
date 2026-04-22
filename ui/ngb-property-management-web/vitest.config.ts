import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    name: 'ngb-property-management-web',
    environment: 'node',
    include: ['tests/unit/**/*.spec.ts'],
    reporters: ['default'],
  },
})
