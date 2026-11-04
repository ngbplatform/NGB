import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: './tests/e2e',
  globalSetup: './tests/e2e/setup.mjs',
  fullyParallel: false,
  forbidOnly: true,
  retries: 0,
  workers: 1,
  timeout: 90_000,
  globalTimeout: 1_200_000,
  outputDir: '../.ngb/browser-results',
  reporter: [['list'], ['json', { outputFile: '../.ngb/browser-results/report.json' }]],
  use: { ...devices['Desktop Chrome'], actionTimeout: 15000, headless: true,
    trace: 'off', screenshot: 'only-on-failure', video: 'off' },
})
