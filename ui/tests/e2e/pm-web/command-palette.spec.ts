import { expect, test } from '@playwright/test'

import { mockCommandPaletteApis, mockHomeDashboardApis, rejectUnhandledApiRequests } from '../support/mockApi'
import { PM_TEST_ROUTES } from '../support/routes'

test.describe('pm-web command palette', () => {
  test('opens from the keyboard shortcut and clears the query after Escape', async ({ page }) => {
    await mockHomeDashboardApis(page)
    await mockCommandPaletteApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/report-definitions',
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

    const opener = page.getByRole('button', { name: /Search pages, records, reports, or run a command/i })
    await opener.focus()

    const modifier = await page.evaluate(() =>
      /Mac|iPhone|iPad|iPod/i.test(String(globalThis.navigator?.platform ?? '')) ? 'Meta' : 'Control')

    await page.keyboard.press(`${modifier}+K`)

    const dialog = page.getByTestId('command-palette-dialog')
    const input = page.getByTestId('command-palette-input')

    await expect(dialog).toBeVisible()
    await expect(input).toBeFocused()

    await input.fill('payables')
    await page.keyboard.press('Escape')

    await expect(dialog).toHaveCount(0)

    await page.keyboard.press(`${modifier}+K`)

    await expect(dialog).toBeVisible()
    await expect(input).toHaveValue('')
  })

  test('opens from the top bar and navigates to payables open items', async ({ page }) => {
    await mockHomeDashboardApis(page)
    await mockCommandPaletteApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/report-definitions',
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

    await page.getByRole('button', { name: /Search pages, records, reports, or run a command/i }).click()
    await expect(page.getByTestId('command-palette-dialog')).toBeVisible()

    const input = page.getByTestId('command-palette-input')
    await input.fill('payables')

    await expect(page.getByRole('option', { name: /Payables/i })).toBeVisible()
    await input.press('Enter')

    await expect(page).toHaveURL(/\/payables\/open-items$/)
    await expect(page.getByTestId('command-palette-dialog')).toHaveCount(0)
  })
})
