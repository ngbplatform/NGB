import { expect, test } from '@playwright/test'

import { expectCardsToStackVertically, expectNoHorizontalPageOverflow } from '../support/assertions'
import { mockPayablesOpenItemsApis, mockReceivablesOpenItemsApis, rejectUnhandledApiRequests } from '../support/mockApi'
import { PM_TEST_ROUTES } from '../support/routes'

test.describe('pm-web mobile layout', () => {
  test('receivables open items stays within the mobile viewport', async ({ page }) => {
    await mockReceivablesOpenItemsApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/documents/pm.lease',
      '/api/receivables/open-items/details',
    ])

    await page.goto(PM_TEST_ROUTES.receivablesOpenItems)

    await expect(page.getByTestId('open-items-page')).toBeVisible()
    await expect(page.getByText('Tenant: Alex Tenant')).toBeVisible()
    await expect(page.getByText('Property: Riverfront Flats')).toBeVisible()

    await expectNoHorizontalPageOverflow(page)
    await expectCardsToStackVertically(page.locator('[data-testid="open-items-summary"] > *'))
  })

  test('payables open items stays within the mobile viewport', async ({ page }) => {
    await mockPayablesOpenItemsApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/catalogs/pm.party',
      '/api/catalogs/pm.property',
      '/api/payables/open-items/details',
    ])

    await page.goto(PM_TEST_ROUTES.payablesOpenItems)

    await expect(page.getByTestId('open-items-page')).toBeVisible()
    await expect(page.getByText('Vendor: Northwind Services LLC')).toBeVisible()
    await expect(page.getByText('Property: Maple Court')).toBeVisible()

    await expectNoHorizontalPageOverflow(page)
    await expectCardsToStackVertically(page.locator('[data-testid="open-items-summary"] > *'))
  })
})
