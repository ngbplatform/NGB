import { expect, test } from '@playwright/test'

import { expectNoAccessibilityViolations } from '../support/accessibility'
import {
  mockWorkCenterApis,
  rejectUnhandledApiRequests,
} from '../support/mockApi'
import { PM_TEST_ROUTES } from '../support/routes'

test.describe('pm-web Work Center accessibility', () => {
  test('drawer exposes an accessible dialog, tabs, feed and keyboard actions', async ({ page }) => {
    await mockWorkCenterApis(page)
    await rejectUnhandledApiRequests(page, ['/api/main-menu'])
    await page.goto(PM_TEST_ROUTES.home)

    await page.getByRole('button', { name: 'Work Center' }).click()
    const drawer = page.getByTestId('drawer-panel')
    const dialog = page.getByRole('dialog')
    await expect(dialog).toBeAttached()
    await expect(dialog).toHaveAttribute('aria-modal', 'true')
    await expect(dialog).toHaveAttribute('aria-labelledby', /.+/)
    await expect(dialog).toContainText('Work Center')
    await expect(drawer.getByRole('tablist', { name: 'Work Center views' })).toBeVisible()

    const tasksTab = drawer.getByRole('tab', { name: 'Tasks', exact: true })
    await tasksTab.focus()
    await page.keyboard.press('Enter')
    await expect(tasksTab).toHaveAttribute('aria-selected', 'true')

    const task = drawer.getByRole('button', { name: /Review unapplied payment/ })
    await task.focus()
    await expect(task).toBeFocused()
    await expectNoAccessibilityViolations(page, '[data-testid="drawer-panel"]')

    await page.keyboard.press('Escape')
    await expect(drawer).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Work Center' })).toBeFocused()
  })

  test('full page and preferences satisfy WCAG AA semantics', async ({ page }) => {
    await mockWorkCenterApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/me/notification-preferences',
    ])

    await page.goto('/work-center?tab=attention')
    await expect(page.getByRole('heading', { name: 'Work Center', exact: true })).toBeVisible()
    await expectNoAccessibilityViolations(page, '[data-testid="site-main"]')

    await page.goto('/settings/notifications')
    await expect(page.getByRole('heading', { name: 'Work Center preferences' })).toBeVisible()
    await expectNoAccessibilityViolations(page, '[data-testid="site-main"]')
  })
})
