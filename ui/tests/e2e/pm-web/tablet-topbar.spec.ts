import { expect, test } from '@playwright/test'

import { expectNoHorizontalPageOverflow } from '../support/assertions'
import { mockHomeDashboardApis, rejectUnhandledApiRequests } from '../support/mockApi'
import { PM_TEST_ROUTES } from '../support/routes'

test.describe('pm-web tablet topbar layout', () => {
  test('keeps the command palette readable on tablet widths', async ({ page }) => {
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

    const topbar = page.getByTestId('site-topbar')
    const search = topbar.getByRole('button', { name: /Search pages, records, reports, or run a command/i })
    const user = topbar.locator('button[title="User"]:visible').first()

    await expect(search).toBeVisible()
    await expect(user).toBeVisible()

    const searchBox = await search.boundingBox()
    const userBox = await user.boundingBox()

    expect(searchBox).not.toBeNull()
    expect(userBox).not.toBeNull()
    expect(searchBox!.width).toBeGreaterThan(260)
    expect(searchBox!.y).toBeGreaterThanOrEqual(userBox!.y + userBox!.height - 1)

    await expectNoHorizontalPageOverflow(page)
  })
})
