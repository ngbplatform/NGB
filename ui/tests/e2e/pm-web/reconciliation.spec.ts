import { expect, test } from '@playwright/test'

import { expectCardsToStackVertically, expectNoHorizontalPageOverflow } from '../support/assertions'
import {
  mockPayablesReconciliationApis,
  mockReceivablesReconciliationApis,
  rejectUnhandledApiRequests,
} from '../support/mockApi'
import { PM_TEST_ROUTES } from '../support/routes'

test.describe('pm-web reconciliation mobile layout', () => {
  test('receivables reconciliation stays within the mobile viewport', async ({ page }) => {
    await mockReceivablesReconciliationApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/receivables/reconciliation',
    ])

    await page.goto(PM_TEST_ROUTES.receivablesReconciliation)

    const reconciliationPage = page.getByTestId('reconciliation-page')
    const reconciliationKpiGrid = page.getByTestId('reconciliation-kpi-grid')

    await expect(reconciliationPage).toBeVisible()
    await expect(reconciliationPage.getByText('Receivables', { exact: true })).toBeVisible()
    await expect(reconciliationKpiGrid.getByText('AR Net', { exact: true })).toBeVisible()
    await expect(page.getByText('What the numbers mean')).toBeVisible()
    await expect(page.getByTestId('reconciliation-table-wrap')).toBeVisible()

    await expectNoHorizontalPageOverflow(page)
    await expectCardsToStackVertically(page.locator('[data-testid="reconciliation-kpi-grid"] > *'))
  })

  test('payables reconciliation stays within the mobile viewport', async ({ page }) => {
    await mockPayablesReconciliationApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/payables/reconciliation',
    ])

    await page.goto(PM_TEST_ROUTES.payablesReconciliation)

    const reconciliationPage = page.getByTestId('reconciliation-page')
    const reconciliationKpiGrid = page.getByTestId('reconciliation-kpi-grid')

    await expect(reconciliationPage).toBeVisible()
    await expect(reconciliationPage.getByText('Payables', { exact: true })).toBeVisible()
    await expect(reconciliationKpiGrid.getByText('AP Net', { exact: true })).toBeVisible()
    await expect(page.getByText('What the numbers mean')).toBeVisible()
    await expect(page.getByTestId('reconciliation-table-wrap')).toBeVisible()

    await expectNoHorizontalPageOverflow(page)
    await expectCardsToStackVertically(page.locator('[data-testid="reconciliation-kpi-grid"] > *'))
  })
})
