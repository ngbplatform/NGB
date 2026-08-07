import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    fileParallelism: false,
    maxWorkers: 1,
    projects: [
      './ngb-agency-billing-web/vitest.config.ts',
      './ngb-agency-billing-web/vitest.browser.config.ts',
      './ngb-property-management-web/vitest.config.ts',
      './ngb-property-management-web/vitest.browser.config.ts',
      './ngb-trade-web/vitest.config.ts',
      './ngb-trade-web/vitest.browser.config.ts',
      './ngb-crm-web/vitest.config.ts',
      './ngb-crm-web/vitest.browser.config.ts',
    ],
    coverage: {
      provider: 'v8',
      reportsDirectory: '../artifacts/coverage/frontend-diff-apps',
      reporter: ['text', 'json'],
      reportOnFailure: true,
      include: [
        'ngb-agency-billing-web/src/editor/**/*.{ts,vue}',
        'ngb-agency-billing-web/src/router/router.ts',
        'ngb-property-management-web/src/editor/**/*.{ts,vue}',
        'ngb-property-management-web/src/router/router.ts',
        'ngb-trade-web/src/editor/**/*.{ts,vue}',
        'ngb-trade-web/src/router/router.ts',
        'ngb-crm-web/src/editor/**/*.{ts,vue}',
        'ngb-crm-web/src/router/router.ts',
      ],
      thresholds: {
        statements: 100,
        branches: 100,
        functions: 100,
        lines: 100,
      },
    },
  },
})
