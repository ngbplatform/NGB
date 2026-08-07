import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    fileParallelism: false,
    maxWorkers: 1,
    projects: [
      './ngb-ui-framework/vitest.diff-unit.config.ts',
      './ngb-ui-framework/vitest.browser.config.ts',
    ],
    coverage: {
      provider: 'v8',
      reportsDirectory: '../artifacts/coverage/frontend-diff-framework',
      reporter: ['text', 'json'],
      reportOnFailure: true,
      include: [
        'ngb-ui-framework/src/ngb/api/contracts.ts',
        'ngb-ui-framework/src/ngb/api/documents.ts',
        'ngb-ui-framework/src/ngb/editor/config.ts',
        'ngb-ui-framework/src/ngb/editor/useConfiguredEntityEditorDocumentActions.ts',
        'ngb-ui-framework/src/ngb/site/NgbSiteShell.vue',
        'ngb-ui-framework/src/ngb/site/NgbTopBar.vue',
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
