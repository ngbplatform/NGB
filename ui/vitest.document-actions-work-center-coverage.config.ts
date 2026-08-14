import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    // Several auth tests intentionally mutate import-time environment state.
    // The merged coverage run must therefore preserve the same isolation as
    // the standalone project runs instead of importing those modules in
    // parallel with browser projects that define valid auth configuration.
    fileParallelism: false,
    maxWorkers: 1,
    projects: [
      './ngb-ui-framework/vitest.config.ts',
      './ngb-ui-framework/vitest.browser.config.ts',
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
      allowExternal: true,
      reportsDirectory: '../artifacts/coverage/frontend',
      reporter: ['text', 'html', 'json-summary', 'lcov'],
      reportOnFailure: true,
      include: [
        // Work Center is subject to the permanent module-level
        // strict per-file gate. Existing files changed by the release are
        // checked with the diff-coverage gate instead.
        'ngb-ui-framework/src/ngb/work-center/**/*.{ts,vue}',
      ],
      thresholds: {
        100: true,
        perFile: true,
      },
    },
  },
})
