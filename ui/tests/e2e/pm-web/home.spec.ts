import { expect, test } from '@playwright/test'

import { expectCardsToStackVertically, expectNoHorizontalPageOverflow } from '../support/assertions'
import { mockHomeDashboardApis, rejectUnhandledApiRequests } from '../support/mockApi'
import { PM_TEST_ROUTES } from '../support/routes'

test.describe('pm-web home page', () => {
  test('renders key sections and stays inside the viewport', async ({ page }, testInfo) => {
    await mockHomeDashboardApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/reports/pm.occupancy.summary/execute',
      '/api/reports/pm.maintenance.queue/execute',
      '/api/documents/pm.lease',
      '/api/documents/pm.rent_charge',
      '/api/documents/pm.receivable_charge',
      '/api/documents/pm.late_fee_charge',
      '/api/documents/pm.receivable_payment',
      '/api/documents/pm.receivable_returned_payment',
      '/api/receivables/reconciliation',
      '/api/accounting/period-closing/calendar',
    ])

    await page.goto(PM_TEST_ROUTES.home)

    await expect(page.getByTestId('home-page')).toBeVisible()
    await expect(page.getByText('Requires Attention')).toBeVisible()
    await expect(page.getByText('Portfolio health at a glance')).toBeVisible()
    await expect(page.getByText('Operational Snapshots')).toBeVisible()
    await expect(page.getByText('Open receivables')).toBeVisible()
    await expect(page.getByText('Upcoming move-ins / move-outs')).toBeVisible()

    await expectNoHorizontalPageOverflow(page)

    if (testInfo.project.name.startsWith('mobile')) {
      await expect(page.getByTestId('site-topbar-main-menu')).toBeVisible()
      await page.getByTestId('site-topbar-main-menu').click()
      await expect(page.getByTestId('mobile-main-menu-sidebar')).toBeVisible()
      await expect(page.locator('[data-testid="drawer-panel"]')).toBeVisible()

      await expectCardsToStackVertically(page.locator('[data-testid="home-attention-grid"] > *'))
    }
  })
})
