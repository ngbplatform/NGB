import { expect, test } from '@playwright/test'

import { receivablePaymentBaseDocumentsFixture } from '../fixtures/pmMetadataRoutes'
import {
  mockGenericMetadataCatalogApis,
  mockGenericMetadataDocumentApis,
  rejectUnhandledApiRequests,
} from '../support/mockApi'
import { PM_TEST_IDS, PM_TEST_ROUTES } from '../support/routes'

const existingPaymentPath = `/documents/pm.receivable_payment/${PM_TEST_IDS.receivablePaymentDocumentId}`

test.describe('pm-web metadata loading and read-only states', () => {
  test('shows the generic party register loading state while data is pending', async ({ page }) => {
    await mockGenericMetadataCatalogApis(page, { pageDelayMs: 1_200 })
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/catalogs/pm.party',
    ])

    await page.goto(PM_TEST_ROUTES.genericPartyCatalog)

    await expect(page.getByTestId('site-main').getByText('Loading…', { exact: true })).toBeVisible()
    await expect(page.getByText('Harbor State Bank', { exact: true })).toBeVisible()
    await expect(page.getByTestId('site-main').getByText('Loading…', { exact: true })).toHaveCount(0)
  })

  test('shows the generic receivable payment editor loading state while the document is pending', async ({ page }) => {
    await mockGenericMetadataDocumentApis(page, { detailsDelayMs: 1_200 })
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/documents/pm.receivable_payment',
      '/api/audit/entities',
    ])

    await page.goto(existingPaymentPath)

    await expect(page.getByTestId('site-main').getByText('Loading…', { exact: true })).toBeVisible()
    await expect(page.locator('[data-validation-key="payment_reference"] input')).toHaveValue('LOCKBOX-2048')
    await expect(page.getByTestId('site-main').getByText('Loading…', { exact: true })).toHaveCount(0)
  })

  test('renders a posted generic receivable payment as read-only', async ({ page }) => {
    const postedDocuments = receivablePaymentBaseDocumentsFixture.map((document, index) =>
      index === 0 ? { ...document, status: 2 } : { ...document })

    await mockGenericMetadataDocumentApis(page, { initialDocuments: postedDocuments })
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/documents/pm.receivable_payment',
      '/api/audit/entities',
    ])

    await page.goto(existingPaymentPath)

    const paymentReferenceInput = page.locator('[data-validation-key="payment_reference"] input')
    const memoTextArea = page.locator('[data-validation-key="memo"] textarea')

    await expect(page.getByText('Posted', { exact: true })).toBeVisible()
    await expect(paymentReferenceInput).toHaveValue('LOCKBOX-2048')
    await expect(memoTextArea).toHaveValue('April rent payment')
    await expect(paymentReferenceInput).not.toBeEditable()
    await expect(memoTextArea).not.toBeEditable()
    await expect(page.getByTitle('Save')).toBeDisabled()
    await expect(page.getByTitle('Mark for deletion')).toHaveCount(0)
  })
})
