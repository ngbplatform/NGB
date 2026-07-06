import path from 'node:path'
import { fileURLToPath } from 'node:url'

import { defineConfig, devices } from '@playwright/test'

import { CRM_WEB_DEV_HOST, CRM_WEB_DEV_PORT } from './devServer.config'
import { loadE2eEnv, resolvePlaywrightAuthFile } from '../tests/e2e/support/e2eEnv'

const uiWorkspaceDir = fileURLToPath(new URL('..', import.meta.url))
const e2eHost = process.env.CRM_WEB_E2E_HOST?.trim() || CRM_WEB_DEV_HOST
const e2ePort = parsePort(process.env.CRM_WEB_E2E_PORT, CRM_WEB_DEV_PORT + 1)
const e2eBaseUrl = `http://${e2eHost}:${e2ePort}`

loadE2eEnv({
  rootDir: uiWorkspaceDir,
  appDirectory: 'ngb-crm-web',
})

function parsePort(value: string | undefined, fallback: number): number {
  const parsed = Number.parseInt(String(value ?? '').trim(), 10)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

export default defineConfig({
  testDir: '../tests/e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 2 : undefined,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: e2eBaseUrl,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  webServer: {
    command: `npm --workspace ngb-crm-web run dev -- --host ${e2eHost} --port ${e2ePort} --strictPort --mode e2e`,
    port: e2ePort,
    reuseExistingServer: false,
    cwd: path.resolve(uiWorkspaceDir),
  },
  projects: [
    {
      name: 'setup-chromium',
      testMatch: /auth\.setup\.ts/,
      use: {
        ...devices['Desktop Chrome'],
      },
    },
    {
      name: 'desktop-standard',
      dependencies: ['setup-chromium'],
      testMatch: /crm-web\/.*\.spec\.ts/,
      use: {
        storageState: resolvePlaywrightAuthFile(uiWorkspaceDir, 'chromium'),
        ...devices['Desktop Chrome'],
        viewport: { width: 1440, height: 900 },
      },
    },
  ],
})
