import { expect, test } from '@playwright/test'

import { expectNoHorizontalPageOverflow } from '../support/assertions'
import { mockOccupancySummaryReportApis, rejectUnhandledApiRequests } from '../support/mockApi'
import { PM_TEST_ROUTES } from '../support/routes'

test.describe('pm-web report page mobile layout', () => {
  test('occupancy summary stays within the mobile viewport while keeping sheet scrolling internal', async ({ page }) => {
    await mockOccupancySummaryReportApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/report-definitions/pm.occupancy.summary',
      '/api/reports/pm.occupancy.summary',
    ])

    await page.goto(PM_TEST_ROUTES.occupancySummaryReport)

    const reportPage = page.getByTestId('report-page')
    const reportSheetScroll = page.getByTestId('report-sheet-scroll')

    await expect(reportPage).toBeVisible()
    await expect(reportPage.getByText('Occupancy Summary', { exact: true })).toBeVisible()
    await expect(reportSheetScroll).toBeVisible()
    await expect(reportSheetScroll.getByText('Riverfront Tower', { exact: true })).toBeVisible()

    const horizontalMetrics = await reportSheetScroll.evaluate((element) => {
      const host = element as HTMLElement
      return {
        clientWidth: host.clientWidth,
        scrollWidth: host.scrollWidth,
      }
    })

    expect(horizontalMetrics.scrollWidth).toBeGreaterThan(horizontalMetrics.clientWidth)
    await expectNoHorizontalPageOverflow(page)
  })
})
