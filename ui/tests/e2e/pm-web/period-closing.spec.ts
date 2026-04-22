import { expect, test } from '@playwright/test'

import { mockAccountingPeriodClosingApis, rejectUnhandledApiRequests } from '../support/mockApi'
import { PM_TEST_ROUTES } from '../support/routes'

test.describe('pm-web accounting period closing', () => {
  test('renders the close workspace and closes the selected month', async ({ page }) => {
    await mockAccountingPeriodClosingApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/accounting/period-closing',
    ])

    await page.goto(PM_TEST_ROUTES.accountingPeriodClosing)

    await expect(page.getByTestId('period-closing-page')).toBeVisible()
    await expect(page.getByText('Month Calendar', { exact: true })).toBeVisible()
    await expect(page.getByText('Month Workspace', { exact: true })).toBeVisible()
    await expect(page.getByText('Fiscal Year Close', { exact: true })).toBeVisible()

    await page.getByRole('button', { name: 'Close Month', exact: true }).click()

    await expect(page.getByText('Close month?', { exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Close Month', exact: true }).last().click()

    await expect(page.getByText('Month closed', { exact: true })).toBeVisible()
    await expect(page.getByText('April 2026 is now closed.', { exact: true })).toBeVisible()
  })
})
