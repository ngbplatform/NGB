import { expect, type Page } from '@playwright/test'

import {
  mockVerticalWorkCenterApis,
  type VerticalSmokeProfile,
} from './verticalSmokeApi'

export async function verifyVerticalWorkCenter(
  page: Page,
  profile: VerticalSmokeProfile,
  vertical: 'crm' | 'ab' | 'trade',
): Promise<void> {
  const workCenter = await mockVerticalWorkCenterApis(page, profile, vertical)

  await page.goto('/home')
  await page.getByRole('button', { name: 'Work Center' }).click()

  const drawer = page.getByTestId('drawer-panel')
  await expect(drawer).toBeVisible()
  await expect(drawer.getByRole('heading', { name: 'Work Center', exact: true })).toBeVisible()
  await expect(drawer.getByText(workCenter.taskTitle, { exact: true })).toBeVisible()
  await expect(drawer.getByText(workCenter.notificationTitle, { exact: true })).toBeVisible()
  await expect(drawer.getByRole('tab', { name: 'Needs Attention', exact: true })).toBeVisible()
  await expect(drawer.getByRole('tab', { name: /Needs Attention \(/ })).toHaveCount(0)

  await drawer.getByRole('button', { name: 'View all', exact: true }).click()
  await expect(page).toHaveURL(/\/work-center\?tab=attention$/)
  await expect(drawer).toHaveCount(0)
  const workspace = page.getByTestId('site-main')
  await expect(workspace.getByRole('heading', { name: 'Work Center', exact: true })).toBeVisible()
  await expect(workspace.getByRole('tab', { name: 'Needs Attention (2)', exact: true })).toBeVisible()
  await expect(workspace.getByRole('tab', { name: 'Tasks (1)', exact: true })).toBeVisible()
  await expect(workspace.getByRole('tab', { name: 'Notifications (1)', exact: true })).toBeVisible()

  await workspace.getByRole('button', { name: `More actions for ${workCenter.taskTitle}` }).click()
  await workspace.getByRole('menuitem', { name: 'Assign to me', exact: true }).click()
  await expect.poll(() => workCenter.getMutations()).toContain('claim')

  await workspace.getByRole('button', { name: `More actions for ${workCenter.taskTitle}` }).click()
  await workspace.getByRole('menuitem', { name: 'Snooze 1 day', exact: true }).click()
  await expect.poll(() => workCenter.getMutations()).toContain('snooze')

  await workspace.getByRole('tab', { name: /Notifications/ }).click()
  await expect(workspace.getByText(workCenter.notificationTitle, { exact: true })).toBeVisible()
  await workspace.getByRole('button', { name: `More actions for ${workCenter.notificationTitle}` }).click()
  await workspace.getByRole('menuitem', { name: 'Dismiss', exact: true }).click()
  await expect.poll(() => workCenter.getMutations()).toContain('dismiss')

  await expect.poll(() => workCenter.getRequests().some((url) =>
    new URL(url).searchParams.get('vertical') === vertical)).toBe(true)
}

export async function verifyVerticalWorkCenterPreferences(
  page: Page,
  profile: VerticalSmokeProfile,
  vertical: 'crm' | 'ab' | 'trade',
): Promise<void> {
  const workCenter = await mockVerticalWorkCenterApis(page, profile, vertical)

  await page.goto('/settings/notifications')
  await expect(page.getByText(`${profile.roleName} Tasks`, { exact: true })).toBeVisible()
  await expect(page.getByText(`${profile.roleName} Notifications`, { exact: true })).toBeVisible()

  const taskPreference = page.getByRole('checkbox', { name: new RegExp(workCenter.taskTitle) })
  await expect(taskPreference).toBeChecked()
  await taskPreference.uncheck()
  await page.getByRole('button', { name: 'Save preferences', exact: true }).click()
  await expect.poll(() => workCenter.getMutations()).toContain('preferences')
  await expect(taskPreference).not.toBeChecked()
}
