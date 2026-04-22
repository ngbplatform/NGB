import { expect, test, type Page } from '@playwright/test'

import {
  mockGenericMetadataCatalogApis,
  mockGenericMetadataDocumentApis,
  rejectUnhandledApiRequests,
} from '../support/mockApi'
import { PM_TEST_IDS, PM_TEST_ROUTES } from '../support/routes'

const existingPartyPath = `/catalogs/pm.party/${PM_TEST_IDS.partyCatalogPrimaryId}`
const existingPaymentPath = `/documents/pm.receivable_payment/${PM_TEST_IDS.receivablePaymentDocumentId}`

async function openMoreAction(page: Page, actionName: string) {
  await page.getByRole('button', { name: 'More actions' }).click()
  await page.getByRole('menuitem', { name: actionName, exact: true }).click()
}

test.describe('pm-web metadata resilience flows', () => {
  test('renders an empty generic party catalog safely', async ({ page }) => {
    await mockGenericMetadataCatalogApis(page, { initialItems: [] })
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/catalogs/pm.party',
    ])

    await page.goto(PM_TEST_ROUTES.genericPartyCatalog)

    await expect(page.getByText('Parties', { exact: true })).toBeVisible()
    await expect(page.getByText('0 / 0', { exact: true })).toBeVisible()
    await expect(page.getByText('Harbor State Bank', { exact: true })).toHaveCount(0)
    await expect(page.getByTitle('Create')).toBeVisible()
  })

  test('renders an empty generic receivable payments register safely', async ({ page }) => {
    await mockGenericMetadataDocumentApis(page, { initialDocuments: [] })
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/documents/pm.receivable_payment',
      '/api/audit/entities',
    ])

    await page.goto(PM_TEST_ROUTES.genericReceivablePayments)

    await expect(page.getByText('Payments', { exact: true })).toBeVisible()
    await expect(page.getByText('0 / 0', { exact: true })).toBeVisible()
    await expect(page.getByText('Receivable Payment RP-2026-0007', { exact: true })).toHaveCount(0)
    await expect(page.getByTitle('Create')).toBeVisible()
  })

  test('shows a register error when the generic party catalog load fails', async ({ page }) => {
    await mockGenericMetadataCatalogApis(page, {
      pageFailure: {
        detail: 'Party register unavailable for the April close.',
      },
    })
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/catalogs/pm.party',
    ])

    await page.goto(PM_TEST_ROUTES.genericPartyCatalog)

    await expect(page.getByText('Parties', { exact: true })).toBeVisible()
    await expect(page.getByText('Party register unavailable for the April close.', { exact: true })).toBeVisible()
  })

  test('shows a register error when the generic receivable payments load fails', async ({ page }) => {
    await mockGenericMetadataDocumentApis(page, {
      pageFailure: {
        detail: 'Receivable payment register timed out during test load.',
      },
    })
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/documents/pm.receivable_payment',
      '/api/audit/entities',
    ])

    await page.goto(PM_TEST_ROUTES.genericReceivablePayments)

    await expect(page.getByText('Payments', { exact: true })).toBeVisible()
    await expect(page.getByText('Receivable payment register timed out during test load.', { exact: true })).toBeVisible()
  })

  test('prompts before discarding unsaved changes in the generic party editor', async ({ page }) => {
    await mockGenericMetadataCatalogApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/catalogs/pm.party',
    ])

    await page.goto(existingPartyPath)
    await expect(page).toHaveURL(new RegExp(`${existingPartyPath.replace(/\./g, '\\.')}(\\?.*)?$`))

    const displayInput = page.locator('[data-validation-key="display"] input')
    await expect(displayInput).toHaveValue('Harbor State Bank')
    await displayInput.fill('Harbor State Bank Treasury')

    await page.getByTitle('Close').click()
    await expect(page.getByText('Discard changes?', { exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Stay', exact: true }).click()

    await expect(page).toHaveURL(new RegExp(`${existingPartyPath.replace(/\./g, '\\.')}(\\?.*)?$`))
    await expect(displayInput).toHaveValue('Harbor State Bank Treasury')

    await page.getByTitle('Close').click()
    await page.getByRole('button', { name: 'Leave', exact: true }).click()

    await expect(page).toHaveURL(/\/catalogs\/pm\.party(\?.*)?$/)
    await expect(page.getByText('Harbor State Bank', { exact: true })).toBeVisible()
    await expect(page.getByText('Harbor State Bank Treasury', { exact: true })).toHaveCount(0)
  })

  test('marks and restores a generic party record from the full-page editor', async ({ page }) => {
    await mockGenericMetadataCatalogApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/catalogs/pm.party',
    ])

    await page.goto(existingPartyPath)
    await expect(page).toHaveURL(new RegExp(`${existingPartyPath.replace(/\./g, '\\.')}(\\?.*)?$`))

    await page.getByTitle('Mark for deletion').click()
    await expect(page.getByText('Mark for deletion?', { exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Mark', exact: true }).click()

    const deletedBannerMessage = page.getByText(
      'This record is marked for deletion. Restore it to edit or post.',
      { exact: true },
    )
    await expect(deletedBannerMessage).toBeVisible()
    await expect(page.getByTitle('Unmark for deletion')).toBeVisible()

    await page.getByTitle('Unmark for deletion').click()
    await expect(page.getByTitle('Mark for deletion')).toBeVisible()
    await expect(deletedBannerMessage).toHaveCount(0)
  })

  test('copies an existing receivable payment into a new draft with prefilled values', async ({ page }) => {
    await mockGenericMetadataDocumentApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/documents/pm.receivable_payment',
      '/api/audit/entities',
    ])

    await page.goto(existingPaymentPath)
    await expect(page).toHaveURL(new RegExp(`${existingPaymentPath.replace(/\./g, '\\.')}(\\?.*)?$`))

    await openMoreAction(page, 'Copy')

    await expect(page).toHaveURL(/\/documents\/pm\.receivable_payment\/new\?copyDraft=/)
    await expect(page.locator('[data-validation-key="payment_reference"] input')).toHaveValue('LOCKBOX-2048')
    await expect(page.locator('[data-validation-key="memo"] textarea')).toHaveValue('April rent payment')

    await page.getByTitle('Save').click()

    await expect(page).toHaveURL(new RegExp(`/documents/pm\\.receivable_payment/${PM_TEST_IDS.receivablePaymentCreatedDocumentId}(\\?.*)?$`))
    await expect(page.locator('[data-validation-key="payment_reference"] input')).toHaveValue('LOCKBOX-2048')
    await expect(page.locator('[data-validation-key="memo"] textarea')).toHaveValue('April rent payment')
  })
})
