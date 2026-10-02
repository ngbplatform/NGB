import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: '../tests/e2e/pm-web',
  testMatch: /attachments-notes\.real\.spec\.ts/,
  globalSetup: '../tests/e2e/pm-web/contentRealStack.ts',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  timeout: 90_000,
  outputDir: '../test-results/content-real',
  reporter: [['list'], ['html', { open: 'never', outputFolder: '../playwright-report/content-real' }]],
  use: {
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    acceptDownloads: true,
  },
  projects: [
    { name: 'content-chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'content-firefox', use: { ...devices['Desktop Firefox'] } },
    { name: 'content-webkit', use: { ...devices['Desktop Safari'] } },
  ],
})
