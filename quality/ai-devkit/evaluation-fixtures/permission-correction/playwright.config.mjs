import { createRequire } from 'node:module'
import { join } from 'node:path'

const require = createRequire(join(process.env.NGB_EVAL_APPLICATION, 'web/package.json'))
const { defineConfig, devices } = require('@playwright/test')

export default defineConfig({
  testDir: '.',
  testMatch: 'permissions.spec.mjs',
  globalSetup: './browser-setup.mjs',
  fullyParallel: false,
  forbidOnly: true,
  retries: 0,
  workers: 1,
  timeout: 90000,
  globalTimeout: 420000,
  outputDir: './browser-results',
  reporter: [['list'], ['json', { outputFile: './browser-results/report.json' }]],
  use: { ...devices['Desktop Chrome'], actionTimeout: 15000, headless: true,
    trace: 'off', screenshot: 'off', video: 'off' },
})
