import { fileURLToPath } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { ngbUiFrameworkPublicAssetsPlugin } from '@ngbplatform/ui/vite-public-assets'
import { CRM_WEB_DEV_HOST, CRM_WEB_DEV_PORT } from './devServer.config'

const e2eKeycloakAlias = fileURLToPath(new URL('../tests/e2e/support/fakeKeycloak.ts', import.meta.url))

export default defineConfig(({ mode }) => {
  return {
    plugins: [vue(), ngbUiFrameworkPublicAssetsPlugin()],
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
