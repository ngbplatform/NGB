import { expect, test } from '@playwright/test'

import { AGENCY_BILLING_SMOKE_PROFILE, mockVerticalSmokeApis } from '../support/verticalSmokeApi'

test.describe('agency-billing-web shell', () => {
  test('renders Agency Billing home and command palette through the e2e stack', async ({ page }) => {
    await mockVerticalSmokeApis(page, AGENCY_BILLING_SMOKE_PROFILE)

    await page.goto('/home')

    await expect(page.getByTestId('site-shell')).toBeVisible()
    await expect(page.getByTestId('agency-billing-home-page')).toBeVisible()
    await expect(page.getByText('Agency Billing control center')).toBeVisible()
    const sidebar = page.getByTestId('site-sidebar-nav')
    await expect(sidebar.getByRole('button', { name: 'Timesheets' })).toBeVisible()

    await page.getByText('Search pages, records, reports, or run a command').click()
    const palette = page.getByTestId('command-palette-dialog')
    await expect(palette).toBeVisible()
    await page.getByTestId('command-palette-input').fill('invoice')
    await expect(palette.getByRole('option').filter({ hasText: 'Sales Invoices' }).first()).toBeVisible()
  })
})
