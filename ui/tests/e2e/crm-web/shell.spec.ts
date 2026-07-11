import { expect, test } from '@playwright/test'

import { CRM_SMOKE_PROFILE, mockVerticalSmokeApis } from '../support/verticalSmokeApi'

test.describe('crm-web shell', () => {
  test('renders CRM home and command palette through the e2e stack', async ({ page }) => {
    await mockVerticalSmokeApis(page, CRM_SMOKE_PROFILE)

    await page.goto('/home')

    await expect(page.getByTestId('site-shell')).toBeVisible()
    await expect(page.getByTestId('crm-home-page')).toBeVisible()
    await expect(page.getByText('CRM pipeline workspace')).toBeVisible()
    const sidebar = page.getByTestId('site-sidebar-nav')
    await expect(sidebar.getByRole('button', { name: 'Leads' })).toBeVisible()
    await expect(sidebar.getByRole('button', { name: 'Quotes' })).toBeVisible()

    await page.getByText('Search pages, records, reports, or run a command').click()
    const palette = page.getByTestId('command-palette-dialog')
    await expect(palette).toBeVisible()
    await page.getByTestId('command-palette-input').fill('quote')
    await expect(palette.getByRole('option').filter({ hasText: 'Quotes' }).first()).toBeVisible()
  })
})
