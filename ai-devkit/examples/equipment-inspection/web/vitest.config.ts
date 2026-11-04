import vue from '@vitejs/plugin-vue'
import { playwright } from '@vitest/browser-playwright'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  plugins: [vue()],
  resolve: { dedupe: ['vue'] },
  optimizeDeps: {
    include: ['@ngbplatform/ui/editor', '@ngbplatform/ui/layout',
      '@ngbplatform/ui/lazy', '@ngbplatform/ui/navigation'],
  },
  test: {
    include: ['tests/unit/**/*.spec.ts'],
    browser: {
      enabled: true,
      headless: true,
      provider: playwright(),
      instances: [{ browser: 'chromium' }],
    },
    coverage: {
      provider: 'v8',
      include: ['src/**/*.{ts,vue}'],
      exclude: ['**/*.d.ts'],
      reporter: ['text', 'json-summary', 'json'],
      reportsDirectory: '../.ngb/coverage/frontend',
      thresholds: { perFile: true, lines: 100, branches: 100, functions: 100, statements: 100 },
    },
  },
})
