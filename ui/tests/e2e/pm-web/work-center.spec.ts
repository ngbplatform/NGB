import { expect, test } from '@playwright/test'

import {
  mockWorkCenterApis,
  rejectUnhandledApiRequests,
} from '../support/mockApi'
import { PM_TEST_ROUTES } from '../support/routes'

test.describe('pm-web Work Center', () => {
  test('loads the drawer, applies task and notification mutations, and opens the full workspace', async ({ page }) => {
    await mockWorkCenterApis(page)
    await rejectUnhandledApiRequests(page, ['/api/main-menu'])

    await page.goto(PM_TEST_ROUTES.home)
    await page.getByRole('button', { name: 'Work Center' }).click()

    await expect(page.getByRole('heading', { name: 'Work Center', exact: true })).toBeVisible()
    await expect(page.getByText('Review unapplied payment', { exact: true })).toBeVisible()
    await expect(page.getByText('Payment exception detected', { exact: true })).toBeVisible()

    await page.getByRole('button', { name: 'Assign to me', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Assign to me', exact: true })).toHaveCount(0)

    await page.getByRole('button', { name: 'Dismiss', exact: true }).click()
    await expect(page.getByText('Payment exception detected', { exact: true })).toHaveCount(0)

    await page.getByRole('button', { name: 'View all', exact: true }).click()
    await expect(page).toHaveURL(/\/work-center\?tab=attention$/)
    await expect(page.getByTestId('drawer-panel')).toHaveCount(0)

    const workspace = page.getByTestId('site-main')
    await expect(workspace.getByRole('heading', { name: 'Work Center', exact: true })).toBeVisible()
    await expect(workspace.getByText('Review unapplied payment', { exact: true })).toBeVisible()
    await expect(workspace.getByText(/\d+ open tasks/, { exact: true })).toBeVisible()
    await expect(workspace.getByText('Overdue', { exact: true }).first()).toBeVisible()
  })

  test('keeps task and notification preferences separate while mandatory entries stay locked', async ({ page }) => {
    const workCenter = await mockWorkCenterApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/me/notification-preferences',
    ])

    await page.goto('/settings/notifications')

    await expect(page.getByText('Property Management Tasks', { exact: true })).toBeVisible()
    await expect(page.getByText('Platform Notifications', { exact: true })).toBeVisible()

    const paymentPreference = page.getByRole('checkbox', { name: /Review unapplied payment/ })
    const mandatoryPreference = page.getByRole('checkbox', { name: /Security access changes/ })

    await expect(paymentPreference).toBeChecked()
    await expect(mandatoryPreference).toBeChecked()
    await expect(mandatoryPreference).toBeDisabled()

    await paymentPreference.uncheck()
    await page.getByRole('button', { name: 'Save preferences', exact: true }).click()

    await expect.poll(() => workCenter.getPreferenceUpdates()).toContainEqual({
      code: 'pm.payment.review',
      channel: 'InApp',
      isEnabled: false,
    })
    await expect(paymentPreference).not.toBeChecked()
  })

  test('keeps drawer tab state isolated from an already-open full Work Center page', async ({ page }) => {
    await mockWorkCenterApis(page)
    await rejectUnhandledApiRequests(page, ['/api/main-menu'])

    await page.goto('/work-center?tab=attention')
    const workspace = page.getByTestId('site-main')
    const attentionTab = workspace.getByRole('tab', {
      name: /Needs Attention/,
      includeHidden: true,
    })
    await expect(attentionTab).toHaveAttribute('aria-selected', 'true')

    await page.getByRole('button', { name: 'Work Center' }).click()
    const drawer = page.getByTestId('drawer-panel')
    await drawer.getByRole('tab', { name: 'Tasks', exact: true }).click()

    await expect(drawer.getByRole('tab', { name: 'Tasks', exact: true })).toHaveAttribute(
      'aria-selected',
      'true',
    )
    await expect(page).toHaveURL(/\/work-center\?tab=attention$/)
    await expect(attentionTab).toHaveAttribute('aria-selected', 'true')
  })
})
