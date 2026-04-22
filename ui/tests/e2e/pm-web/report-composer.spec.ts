import { expect, test, type Page } from '@playwright/test'

import { mockOccupancySummaryReportApis, rejectUnhandledApiRequests } from '../support/mockApi'
import { PM_TEST_ROUTES } from '../support/routes'

async function createVariant(page: Page, variantName: string) {
  await page.getByTitle('Composer').click()

  const drawer = page.getByTestId('drawer-panel')
  await expect(drawer).toBeVisible()

  await drawer.getByRole('tab', { name: 'Variant' }).click()
  await drawer.getByTitle('Create variant').click()

  const variantDialog = page.getByRole('dialog', { name: 'Create variant' })
  const variantNameInput = variantDialog.getByRole('textbox', { name: 'Month-end ledger' })

  await variantNameInput.evaluate((element, value) => {
    const input = element as HTMLInputElement
    input.value = String(value ?? '')
    input.dispatchEvent(new Event('input', { bubbles: true }))
    input.dispatchEvent(new Event('change', { bubbles: true }))
  }, variantName)
  await expect(variantNameInput).toHaveValue(variantName)
  await variantDialog.getByRole('button', { name: 'Create', exact: true }).click()

  await expect.poll(() => page.getByRole('dialog', { name: 'Create variant' }).count()).toBe(0)
  await expect(page).toHaveURL(new RegExp(`variant=${variantName.toLowerCase().replace(/[^a-z0-9]+/g, '-')}`))
}

test.describe('pm-web report composer', () => {
  test('opens the composer, adds row grouping, and runs the report', async ({ page }) => {
    const reportApi = await mockOccupancySummaryReportApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/report-definitions/pm.occupancy.summary',
      '/api/reports/pm.occupancy.summary',
    ])

    await page.goto(PM_TEST_ROUTES.occupancySummaryReport)

    await expect(page.getByTestId('report-page')).toBeVisible()
    await expect(page.getByText('Occupancy Summary', { exact: true })).toBeVisible()

    await page.getByTitle('Composer').click()

    const drawer = page.getByTestId('drawer-panel')
    await expect(drawer).toBeVisible()
    await expect(drawer.getByTestId('report-composer-panel')).toBeVisible()

    await drawer.getByRole('tab', { name: 'Grouping' }).click()
    await drawer.getByRole('button', { name: 'Add row' }).click()
    await drawer.getByTitle('Run').click()

    await expect(page.getByTestId('drawer-panel')).toHaveCount(0)
    await expect.poll(() => reportApi.getLastExecuteRequest()?.layout?.rowGroups?.[0]?.fieldCode ?? '').toBe('property')
  })

  test('creates a named variant from the composer', async ({ page }) => {
    const reportApi = await mockOccupancySummaryReportApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/report-definitions/pm.occupancy.summary',
      '/api/reports/pm.occupancy.summary',
    ])

    await page.goto(PM_TEST_ROUTES.occupancySummaryReport)

    await createVariant(page, 'Month-end Snapshot')

    await expect.poll(() => reportApi.getVariants().map((variant) => variant.name)).toContain('Month-end Snapshot')
    await expect(page).toHaveURL(/variant=month-end-snapshot/)
  })

  test('deletes the current variant and resets the page to definition default', async ({ page }) => {
    const reportApi = await mockOccupancySummaryReportApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/report-definitions/pm.occupancy.summary',
      '/api/reports/pm.occupancy.summary',
    ])

    await page.goto(PM_TEST_ROUTES.occupancySummaryReport)

    await createVariant(page, 'Delete Me')

    await expect.poll(() => reportApi.getVariants().map((variant) => variant.name)).toContain('Delete Me')

    await page.getByTitle('Composer').click()

    const drawer = page.getByTestId('drawer-panel')
    await expect(drawer).toBeVisible()
    await drawer.getByRole('tab', { name: 'Variant' }).click()
    await drawer.getByTitle('Delete variant').click()

    const deleteDialog = page.getByRole('dialog', { name: 'Delete variant' })
    await deleteDialog.getByRole('button', { name: 'Delete', exact: true }).click()

    await expect.poll(() => reportApi.getVariants().length).toBe(0)
    await expect(page).not.toHaveURL(/variant=/)

    await page.getByTitle('Composer').click()
    const reopenedDrawer = page.getByTestId('drawer-panel')
    await expect(reopenedDrawer).toBeVisible()
    await reopenedDrawer.getByRole('tab', { name: 'Variant' }).click()
    await expect(reopenedDrawer.getByText('Using the report definition default layout and filters.', { exact: true })).toBeVisible()
  })
})
