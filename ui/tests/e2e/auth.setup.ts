import fs from 'node:fs'
import path from 'node:path'

import { expect, test as setup } from '@playwright/test'

import { mockCommonPmApis, rejectUnhandledApiRequests } from './support/mockApi'
import { loadPmWebE2eEnv, requireE2eEnv, resolvePlaywrightAuthFile } from './support/e2eEnv'

setup.setTimeout(60_000)

const env = loadPmWebE2eEnv(process.cwd())
const authFile = resolvePlaywrightAuthFile(process.cwd())
const username = requireE2eEnv(env, 'PLAYWRIGHT_AUTH_USERNAME')
const password = requireE2eEnv(env, 'PLAYWRIGHT_AUTH_PASSWORD')

setup('authenticate ngb tester', async ({ page }) => {
  await mockCommonPmApis(page)
  await rejectUnhandledApiRequests(page, ['/api/main-menu'])

  await page.goto('/')

  const usernameField = page.locator('input[name="username"]')
  const passwordField = page.locator('input[name="password"]')
  const submitButton = page.locator('button[type="submit"], input[type="submit"]').first()

  await expect(usernameField).toBeVisible({ timeout: 60_000 })
  await usernameField.fill(username)
  await passwordField.fill(password)

  await Promise.all([
    page.waitForURL((url) => url.pathname === '/home', { timeout: 60_000 }),
    submitButton.click(),
  ])

  await expect(page.getByTestId('site-shell')).toBeVisible({ timeout: 30_000 })

  fs.mkdirSync(path.dirname(authFile), { recursive: true })
  await page.context().storageState({ path: authFile })
})
