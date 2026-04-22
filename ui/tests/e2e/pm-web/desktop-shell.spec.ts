import { expect, test } from '@playwright/test'

import { mockReceivablesOpenItemsApis, rejectUnhandledApiRequests } from '../support/mockApi'
import { PM_TEST_ROUTES } from '../support/routes'

test.describe('pm-web desktop shell', () => {
  test('keeps the sidebar full-height and prevents page-level scrolling', async ({ page }) => {
    await mockReceivablesOpenItemsApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/documents/pm.lease',
      '/api/receivables/open-items/details',
    ])

    await page.goto(PM_TEST_ROUTES.receivablesOpenItems)

    await expect(page.getByTestId('site-shell')).toBeVisible()
    await expect(page.getByTestId('site-sidebar')).toBeVisible()
    await expect(page.getByTestId('site-topbar')).toBeVisible()

    const viewport = page.viewportSize()
    expect(viewport).not.toBeNull()

    const shellMetrics = await page.evaluate(() => ({
      innerHeight: window.innerHeight,
      scrollHeight: document.documentElement.scrollHeight,
    }))

    expect(shellMetrics.scrollHeight).toBeLessThanOrEqual(shellMetrics.innerHeight + 1)

    const sidebarBox = await page.getByTestId('site-sidebar').boundingBox()
    const topbarBox = await page.getByTestId('site-topbar').boundingBox()
    const sidebarBrandBox = await page.getByTestId('site-sidebar-brand').boundingBox()

    expect(sidebarBox).not.toBeNull()
    expect(topbarBox).not.toBeNull()
    expect(sidebarBrandBox).not.toBeNull()

    expect(Math.round(sidebarBox!.height)).toBeGreaterThanOrEqual(viewport!.height - 1)
    expect(Math.round(topbarBox!.height)).toBe(56)
    expect(Math.round(sidebarBrandBox!.height)).toBe(56)
  })
})
