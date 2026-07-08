import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    projects: [
      './ngb-agency-billing-web/vitest.config.ts',
      './ngb-ui-framework/vitest.config.ts',
      './ngb-property-management-web/vitest.config.ts',
      './ngb-trade-web/vitest.config.ts',
    ],
  },
})
