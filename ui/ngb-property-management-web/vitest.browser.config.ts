import { dirname } from 'node:path'
import { fileURLToPath } from 'node:url'

import vue from '@vitejs/plugin-vue'
import { playwright } from '@vitest/browser-playwright'
import { defineConfig } from 'vitest/config'

const packageRoot = dirname(fileURLToPath(import.meta.url))

export default defineConfig({
  root: packageRoot,
  plugins: [vue()],
  define: {
    'import.meta.env.VITE_KEYCLOAK_URL': JSON.stringify('http://localhost:8080'),
    'import.meta.env.VITE_KEYCLOAK_REALM': JSON.stringify('ngb-demo'),
    'import.meta.env.VITE_KEYCLOAK_CLIENT_ID': JSON.stringify('ngb-web-client'),
  },
  optimizeDeps: {
    include: ['@headlessui/vue', 'pinia', 'vue-router', 'keycloak-js'],
  },
  test: {
    name: 'ngb-property-management-web-browser',
    include: ['tests/browser/**/*.browser.spec.ts'],
    setupFiles: ['vitest-browser-vue', './tests/browser/setup.ts'],
    browser: {
      enabled: true,
      headless: true,
      provider: playwright(),
      instances: [
        {
          browser: 'chromium',
        },
      ],
    },
    reporters: ['default'],
  },
})
