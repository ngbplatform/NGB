import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    fileParallelism: false,
    maxWorkers: 1,
    projects: [
      './ngb-ui-framework/vitest.diff-unit.config.ts',
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
      reportsDirectory: '../artifacts/coverage/frontend-diff',
      reporter: ['text', 'html', 'json-summary', 'lcov'],
      reportOnFailure: true,
      include: [
        'ngb-ui-framework/src/ngb/api/contracts.ts',
        'ngb-ui-framework/src/ngb/api/documents.ts',
        'ngb-ui-framework/src/ngb/editor/config.ts',
        'ngb-ui-framework/src/ngb/editor/useConfiguredEntityEditorDocumentActions.ts',
        'ngb-ui-framework/src/ngb/site/NgbSiteShell.vue',
        'ngb-ui-framework/src/ngb/site/NgbTopBar.vue',
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
