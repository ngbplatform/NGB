import { expect, test } from '@playwright/test'

import { TRADE_SMOKE_PROFILE, mockVerticalSmokeApis } from '../support/verticalSmokeApi'

test.describe('trade-web shell', () => {
  test('renders Trade home and command palette through the e2e stack', async ({ page }) => {
    await mockVerticalSmokeApis(page, TRADE_SMOKE_PROFILE)

    await page.goto('/home')

    await expect(page.getByTestId('site-shell')).toBeVisible()
    await expect(page.getByTestId('trade-home-page')).toBeVisible()
    await expect(page.getByText('Live Trade Activity')).toBeVisible()
    const sidebar = page.getByTestId('site-sidebar-nav')
    await expect(sidebar.getByRole('button', { name: 'Sales Invoices' })).toBeVisible()

    await page.getByText('Search pages, records, reports, or run a command').click()
    const palette = page.getByTestId('command-palette-dialog')
    await expect(palette).toBeVisible()
    await page.getByTestId('command-palette-input').fill('sales')
    await expect(palette.getByRole('option').filter({ hasText: 'Sales Invoices' }).first()).toBeVisible()
  })
})
