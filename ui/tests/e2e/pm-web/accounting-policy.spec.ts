import { expect, test } from '@playwright/test'

import { expectNoHorizontalPageOverflow } from '../support/assertions'
import { mockAccountingPolicyApis, rejectUnhandledApiRequests } from '../support/mockApi'
import { PM_TEST_ROUTES } from '../support/routes'

test.describe('pm-web accounting policy mobile layout', () => {
  test('accounting policy settings stay within the mobile viewport', async ({ page }) => {
    await mockAccountingPolicyApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/catalogs/pm.accounting_policy',
    ])

    await page.goto(PM_TEST_ROUTES.accountingPolicy)

    const settingsPage = page.getByTestId('accounting-policy-page')
    const settingsForm = page.getByTestId('accounting-policy-form')

    await expect(settingsPage).toBeVisible()
    await expect(settingsPage.getByText('Accounting Policy', { exact: true })).toBeVisible()
    await expect(settingsForm).toBeVisible()
    await expect(settingsForm.getByText('Default Cash Control Account', { exact: true })).toBeVisible()
    await expect(settingsForm.getByText('Tenant Receivables (A/R) Account', { exact: true })).toBeVisible()
    await expect(settingsForm.getByText('Vendor Payables (A/P) Account', { exact: true })).toBeVisible()

    await expectNoHorizontalPageOverflow(page)
  })
})
