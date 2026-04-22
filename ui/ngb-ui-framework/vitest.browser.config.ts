import { dirname } from 'node:path'
import { fileURLToPath } from 'node:url'

import vue from '@vitejs/plugin-vue'
import { playwright } from '@vitest/browser-playwright'
import { defineConfig } from 'vitest/config'

const packageRoot = dirname(fileURLToPath(import.meta.url))
const supportedBrowsers = ['chromium', 'firefox', 'webkit'] as const

function resolveBrowserInstances() {
  const matrix = String(process.env.NGB_UI_FRAMEWORK_BROWSER_MATRIX ?? 'chromium')
    .trim()
    .toLowerCase()

  const requested = matrix === 'all'
    ? supportedBrowsers
    : matrix
      .split(',')
      .map((entry) => entry.trim())
      .filter((entry): entry is typeof supportedBrowsers[number] =>
        supportedBrowsers.includes(entry as typeof supportedBrowsers[number]))

  const browsers = requested.length > 0 ? requested : ['chromium']
  return browsers.map((browser) => ({ browser }))
}

export default defineConfig({
  root: packageRoot,
  plugins: [vue()],
  define: {
    'import.meta.env.VITE_KEYCLOAK_URL': JSON.stringify('http://localhost:8080'),
    'import.meta.env.VITE_KEYCLOAK_REALM': JSON.stringify('ngb-demo'),
    'import.meta.env.VITE_KEYCLOAK_CLIENT_ID': JSON.stringify('ngb-web-client'),
  },
  optimizeDeps: {
    include: ['@headlessui/vue', 'vue-router', 'pinia', 'keycloak-js'],
  },
  test: {
    name: 'ngb-ui-framework-browser',
    include: ['tests/browser/**/*.browser.spec.ts'],
    setupFiles: ['vitest-browser-vue', './tests/browser/setup.ts'],
    browser: {
      enabled: true,
      headless: true,
      provider: playwright(),
      instances: resolveBrowserInstances(),
    },
    reporters: ['default'],
  },
})
