import { expect, test } from '@playwright/test'

import { receivablePaymentBaseDocumentsFixture } from '../fixtures/pmMetadataRoutes'
import {
  mockCommandPaletteApis,
  mockGenericMetadataCatalogApis,
  mockGenericMetadataDocumentApis,
  mockHomeDashboardApis,
  rejectUnhandledApiRequests,
} from '../support/mockApi'
import { PM_TEST_IDS, PM_TEST_ROUTES } from '../support/routes'

const existingPaymentPath = `/documents/pm.receivable_payment/${PM_TEST_IDS.receivablePaymentDocumentId}`

test.describe('pm-web visual regression', () => {
  test('captures the home desktop shell', async ({ page }) => {
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
    await expect(page.getByTestId('site-shell')).toBeVisible()
    await expect(page.getByTestId('site-shell')).toHaveScreenshot('home-shell-desktop.png', {
      animations: 'disabled',
      caret: 'hide',
    })
  })

  test('captures the desktop command palette dialog', async ({ page }) => {
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

    const input = page.getByTestId('command-palette-input')
    await input.fill('payables')

    await expect(page.getByTestId('command-palette-dialog')).toHaveScreenshot('command-palette-dialog-desktop.png', {
      animations: 'disabled',
      caret: 'hide',
    })
  })

  test('captures the empty generic party register state', async ({ page }) => {
    await mockGenericMetadataCatalogApis(page, { initialItems: [] })
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/catalogs/pm.party',
    ])

    await page.goto(PM_TEST_ROUTES.genericPartyCatalog)

    await expect(page.getByTestId('site-shell')).toHaveScreenshot('party-catalog-empty-desktop.png', {
      animations: 'disabled',
      caret: 'hide',
    })
  })

  test('captures the posted receivable payment read-only state', async ({ page }) => {
    const postedDocuments = receivablePaymentBaseDocumentsFixture.map((document, index) =>
      index === 0 ? { ...document, status: 2 } : { ...document })

    await mockGenericMetadataDocumentApis(page, { initialDocuments: postedDocuments })
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/documents/pm.receivable_payment',
      '/api/audit/entities',
    ])

    await page.goto(existingPaymentPath)

    await expect(page.getByTestId('site-main')).toHaveScreenshot('receivable-payment-posted-readonly-desktop.png', {
      animations: 'disabled',
      caret: 'hide',
    })
  })
})
