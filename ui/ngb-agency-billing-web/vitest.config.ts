import { dirname } from 'node:path'
import { fileURLToPath } from 'node:url'

import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vitest/config'

const packageRoot = dirname(fileURLToPath(import.meta.url))

export default defineConfig({
  root: packageRoot,
  plugins: [vue()],
  test: {
    name: 'ngb-agency-billing-web',
    environment: 'node',
    include: ['tests/unit/**/*.spec.ts'],
    reporters: ['default'],
  },
})
