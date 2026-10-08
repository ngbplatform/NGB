import vue from '@vitejs/plugin-vue'
import { playwright } from '@vitest/browser-playwright'
import { defineConfig } from 'vitest/config'
import { fileURLToPath } from 'node:url'

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: [
      { find: /^@ngbplatform\/ui$/, replacement: fileURLToPath(new URL('./ngb-ui-framework/src/index.ts', import.meta.url)) },
      { find: '@ngbplatform/ui/styles', replacement: fileURLToPath(new URL('./ngb-ui-framework/src/styles/tailwind.css', import.meta.url)) },
      { find: '@ngbplatform/ui/lazy', replacement: fileURLToPath(new URL('./ngb-ui-framework/src/lazy.ts', import.meta.url)) },
      { find: /^vue$/, replacement: fileURLToPath(new URL('./node_modules/vue/dist/vue.esm-bundler.js', import.meta.url)) },
      { find: /^pinia$/, replacement: fileURLToPath(new URL('./node_modules/pinia/dist/pinia.mjs', import.meta.url)) },
      { find: /^vue-router$/, replacement: fileURLToPath(new URL('./node_modules/vue-router/dist/vue-router.mjs', import.meta.url)) },
    ],
  },
  optimizeDeps: { include: ['vue', 'pinia', 'vue-router', 'vitest-browser-vue'] },
  test: {
    include: ['tests/template/**/*.spec.ts'],
    browser: {
      enabled: true,
      headless: true,
      provider: playwright(),
      instances: [{ browser: 'chromium' }],
    },
    coverage: {
      provider: 'v8',
      allowExternal: true,
      include: [
        `${fileURLToPath(new URL('../packaging/templates/content/web/src', import.meta.url))}/**/*.{ts,vue}`,
        'ngb-ui-framework/tailwind-preset.js',
      ],
      exclude: ['**/*.d.ts'],
      reporter: ['text', 'json-summary'],
      reportsDirectory: '../artifacts/coverage/template-frontend',
      thresholds: { lines: 100, branches: 100, functions: 100, statements: 100 },
    },
  },
})
