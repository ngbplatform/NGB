import { fileURLToPath } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { ngbUiFrameworkPublicAssetsPlugin } from '@ngbplatform/ui/vite-public-assets'
import { CRM_WEB_DEV_HOST, CRM_WEB_DEV_PORT } from './devServer.config'

const e2eKeycloakAlias = fileURLToPath(new URL('../tests/e2e/support/fakeKeycloak.ts', import.meta.url))

export default defineConfig(({ mode }) => {
  return {
    plugins: [vue(), ngbUiFrameworkPublicAssetsPlugin()],
    optimizeDeps: {
      // The source package mixes TS entry points with Vue SFCs. Keep their
      // configuration modules in the same graph instead of duplicating state.
      exclude: ['@ngbplatform/ui'],
      // Excluding the source package also hides its dependencies from the scan.
      // Bundle them up front so concurrent browsers do not load outdated chunks
      // while Vite discovers the framework's imports on the first page load.
      include: [
        'vue',
        'pinia',
        'vue-router',
        '@headlessui/vue',
        '@microsoft/signalr',
        ...(mode === 'e2e' ? [] : ['keycloak-js']),
      ],
    },
    resolve: mode === 'e2e'
      ? {
          alias: {
            'keycloak-js': e2eKeycloakAlias,
          },
        }
      : undefined,
    server: {
      host: CRM_WEB_DEV_HOST,
      port: CRM_WEB_DEV_PORT,
    },
  }
})
