import path from 'node:path'
import { fileURLToPath } from 'node:url'

import { defineConfig, devices } from '@playwright/test'

import { PM_WEB_DEV_HOST, PM_WEB_DEV_PORT } from './devServer.config'
import { loadE2eEnv, resolvePlaywrightAuthFile } from '../tests/e2e/support/e2eEnv'

const uiWorkspaceDir = fileURLToPath(new URL('..', import.meta.url))
const e2eHost = process.env.PM_WEB_E2E_HOST?.trim() || PM_WEB_DEV_HOST
const e2ePort = parsePort(process.env.PM_WEB_E2E_PORT, PM_WEB_DEV_PORT + 1)
const e2eBaseUrl = `http://${e2eHost}:${e2ePort}`

loadE2eEnv({
  rootDir: uiWorkspaceDir,
  appDirectory: 'ngb-property-management-web',
})

const chromiumAuthFile = resolvePlaywrightAuthFile(uiWorkspaceDir, 'chromium')
const firefoxAuthFile = resolvePlaywrightAuthFile(uiWorkspaceDir, 'firefox')
const webkitAuthFile = resolvePlaywrightAuthFile(uiWorkspaceDir, 'webkit')
const mobileSpecs = /pm-web\/(mobile-layout|home|reconciliation|properties|accounting-policy|reports)\.spec\.ts/
const tabletSpecs = /pm-web\/(tablet-topbar|command-palette)\.spec\.ts/
const desktopChromiumSpecs =
  /pm-web\/(desktop-shell|home|command-palette|period-closing|journal-entries|chart-of-accounts|report-composer|report-resilience|open-items-workflow|metadata-routes|metadata-resilience|metadata-states|metadata-access-errors|router-redirects|visual-regression|document-side-flows|security-access|work-center)\.spec\.ts/
const desktopCrossBrowserSmokeSpecs =
  /pm-web\/(desktop-shell|home|command-palette|open-items-workflow|metadata-routes|metadata-resilience|metadata-states|metadata-access-errors|router-redirects|document-side-flows)\.spec\.ts/

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
    command: `npm --workspace ngb-property-management-web run dev -- --host ${e2eHost} --port ${e2ePort} --strictPort --mode e2e`,
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
      name: 'setup-firefox',
      testMatch: /auth\.setup\.ts/,
      use: {
        ...devices['Desktop Firefox'],
      },
    },
    {
      name: 'setup-webkit',
      testMatch: /auth\.setup\.ts/,
      use: {
        ...devices['Desktop Safari'],
      },
    },
    {
      name: 'mobile-narrow',
      dependencies: ['setup-chromium'],
      testMatch: mobileSpecs,
      use: {
        storageState: chromiumAuthFile,
        viewport: { width: 320, height: 740 },
      },
    },
    {
      name: 'mobile-standard',
      dependencies: ['setup-webkit'],
      testMatch: mobileSpecs,
      use: {
        storageState: webkitAuthFile,
        ...devices['iPhone 13'],
      },
    },
    {
      name: 'tablet-standard',
      dependencies: ['setup-chromium'],
      testMatch: tabletSpecs,
      use: {
        storageState: chromiumAuthFile,
        viewport: { width: 768, height: 1024 },
      },
    },
    {
      name: 'desktop-standard',
      dependencies: ['setup-chromium'],
      testMatch: desktopChromiumSpecs,
      use: {
        storageState: chromiumAuthFile,
        ...devices['Desktop Chrome'],
        viewport: { width: 1440, height: 900 },
      },
    },
    {
      name: 'desktop-firefox',
      dependencies: ['setup-firefox'],
      testMatch: desktopCrossBrowserSmokeSpecs,
      use: {
        storageState: firefoxAuthFile,
        ...devices['Desktop Firefox'],
        viewport: { width: 1440, height: 900 },
      },
    },
    {
      name: 'desktop-webkit',
      dependencies: ['setup-webkit'],
      testMatch: desktopCrossBrowserSmokeSpecs,
      use: {
        storageState: webkitAuthFile,
        ...devices['Desktop Safari'],
        viewport: { width: 1440, height: 900 },
      },
    },
  ],
})
