import { createRequire } from 'node:module'
import { join } from 'node:path'

const require = createRequire(join(process.env.NGB_EVAL_APPLICATION, 'web/package.json'))
const { expect, test } = require('@playwright/test')

async function signIn(page, username) {
  await page.goto(process.env.NGB_EXAMPLE_ORIGIN)
  await page.locator('input[name="username"]').fill(username)
  await page.locator('input[name="password"]').fill(process.env.NGB_EXAMPLE_PASSWORD)
  await page.locator('button[type="submit"], input[type="submit"]').first().click()
  await expect(page.getByTestId('site-shell')).toBeVisible({ timeout: 30000 })
}

for (const username of ['administrator', 'application-administrator']) {
  test(`${username} can edit equipment through the published UI`, async ({ page }) => {
    await signIn(page, username)
    const id = process.env.NGB_EVAL_EQUIPMENT_ID
    await page.goto(`${process.env.NGB_EXAMPLE_ORIGIN}/catalogs/example.equipment/${id}`)
    const field = page.locator('[data-validation-key="display"] input').first()
    await field.fill('Edited by ' + username)
    const save = page.getByRole('button', { name: 'Save', exact: true })
    await expect(save).toBeEnabled()
    const response = page.waitForResponse(response => response.url().endsWith('/api/catalogs/example.equipment/' + id)
      && response.request().method() === 'PUT')
    await save.click()
    expect((await response).status()).toBe(200)
    await page.reload()
    await expect(field).toHaveValue('Edited by ' + username)
  })
}

test('view-only reader can inspect equipment while Save remains disabled', async ({ page }) => {
  await signIn(page, 'reader')
  const id = process.env.NGB_EVAL_EQUIPMENT_ID
  await page.goto(`${process.env.NGB_EXAMPLE_ORIGIN}/catalogs/example.equipment/${id}`)
  const field = page.locator('[data-validation-key="display"] input').first()
  await expect(field).toHaveValue(/^(?:Browser protected equipment|Edited by )/)
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeDisabled()
  await page.reload()
  await expect(field).toHaveValue(/^(?:Browser protected equipment|Edited by )/)
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeDisabled()
})
