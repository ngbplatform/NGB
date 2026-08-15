import { expect, test } from '@playwright/test'

import {
  mockWorkCenterApis,
  rejectUnhandledApiRequests,
} from '../support/mockApi'
import { PM_TEST_ROUTES } from '../support/routes'

test.describe('pm-web responsive Work Center visual regression', () => {
  test('keeps the mobile full page and drawer within the viewport', async ({ page }) => {
    await mockWorkCenterApis(page)
    await rejectUnhandledApiRequests(page, ['/api/main-menu'])

    await page.goto('/work-center?tab=attention')
    await expect(page.getByTestId('site-main')).toHaveScreenshot('work-center-page-mobile.png', {
      animations: 'disabled',
      caret: 'hide',
    })

    await page.goto(PM_TEST_ROUTES.home)
    await page.getByRole('button', { name: 'Work Center' }).click()
    const drawer = page.getByTestId('drawer-panel')
    await expect(drawer).toBeVisible()
    await expect(drawer).toHaveScreenshot('work-center-drawer-mobile.png', {
      animations: 'disabled',
      caret: 'hide',
    })
  })
})
