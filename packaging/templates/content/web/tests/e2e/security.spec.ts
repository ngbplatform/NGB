import { expect, test, type Page } from '@playwright/test'

async function signIn(page: Page, username: string): Promise<void> {
  const origin = process.env.NGB_TEST_ORIGIN
  const password = process.env.NGB_TEST_PASSWORD
  expect(origin, 'The disposable test environment must supply an origin.').toBeTruthy()
  expect(password, 'The disposable identity server must supply a password.').toBeTruthy()
  await page.goto(`${origin}/admin/security/roles`)
  await page.locator('input[name="username"]').fill(username)
  await page.locator('input[name="password"]').fill(password!)
  await page.locator('button[type="submit"], input[type="submit"]').first().click()
  await expect(page.getByTestId('site-shell')).toBeVisible({ timeout: 30_000 })
}

test('trusted administrator can open the protected role editor without a platform user record', async ({ page }) => {
  await signIn(page, 'administrator')
  await page.goto(`${process.env.NGB_TEST_ORIGIN}/admin/security/roles/new`)
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeVisible()
})

test('a signed-in user without permission cannot read roles through UI navigation', async ({ page }) => {
  const rejected = page.waitForResponse(response => response.url().includes('/api/security/roles') && response.status() === 403)
  await signIn(page, 'reader')
  await rejected
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toHaveCount(0)
})
