import { fileURLToPath } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { ngbUiFrameworkPublicAssetsPlugin } from 'ngb-ui-framework/vite-public-assets'
import { PM_WEB_DEV_HOST, PM_WEB_DEV_PORT } from './devServer.config'

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
    build: {
      rollupOptions: {
        output: {
          manualChunks(id) {
            if (
              id.includes('node_modules/echarts')
              || id.includes('node_modules/zrender')
              || id.includes('node_modules/vue-echarts')
            ) {
              return 'charts'
            }
          },
        },
      },
    },
    server: {
      host: PM_WEB_DEV_HOST,
      port: PM_WEB_DEV_PORT,
    },
  }
})
