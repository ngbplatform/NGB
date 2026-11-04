import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: './tests/e2e',
  globalSetup: './tests/e2e/setup.mjs',
  fullyParallel: false,
  forbidOnly: true,
  retries: 0,
  workers: 1,
  timeout: 60_000,
  globalTimeout: 1_200_000,
  outputDir: '../.ngb/browser-results',
  reporter: [['list'], ['json', { outputFile: '../.ngb/browser-results/report.json' }]],
  use: {
    ...devices['Desktop Chrome'],
    headless: true,
    trace: 'off',
    screenshot: 'off',
    video: 'off',
  },
})
