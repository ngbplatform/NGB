import { expect, test } from '@playwright/test'

import {
  mockGenericMetadataCatalogApis,
  mockGenericMetadataDocumentApis,
  rejectUnhandledApiRequests,
} from '../support/mockApi'
import { PM_TEST_IDS, PM_TEST_ROUTES } from '../support/routes'

const existingPartyPath = `/catalogs/pm.party/${PM_TEST_IDS.partyCatalogPrimaryId}`
const existingPaymentPath = `/documents/pm.receivable_payment/${PM_TEST_IDS.receivablePaymentDocumentId}`

test.describe('pm-web metadata access and error handling', () => {
  test('shows a permission-denied banner when the generic party register is forbidden', async ({ page }) => {
    await mockGenericMetadataCatalogApis(page, {
      pageFailure: {
        status: 403,
        detail: 'You do not have permission to view parties.',
        title: 'Forbidden',
      },
    })
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/catalogs/pm.party',
    ])

    await page.goto(PM_TEST_ROUTES.genericPartyCatalog)

    await expect(page.getByText('Parties', { exact: true })).toBeVisible()
    await expect(page.getByText('You do not have permission to view parties.', { exact: true })).toBeVisible()
  })

  test('shows a permission-denied banner when the generic party editor is forbidden', async ({ page }) => {
    await mockGenericMetadataCatalogApis(page, {
      detailsFailure: {
        status: 403,
        detail: 'You do not have permission to open this party record.',
        title: 'Forbidden',
      },
    })
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/catalogs/pm.party',
    ])

    await page.goto(existingPartyPath)

    await expect(page).toHaveURL(new RegExp(`${existingPartyPath.replace(/\./g, '\\.')}(\\?.*)?$`))
    await expect(page.getByText('You do not have permission to open this party record.', { exact: true })).toBeVisible()
  })

  test('keeps the create drawer open and shows inline validation issues when party save fails', async ({ page }) => {
    await mockGenericMetadataCatalogApis(page, {
      createFailure: {
        status: 400,
        detail: 'One or more validation errors has occurred.',
        title: 'Validation failed',
        body: {
          errors: {
            email: ['Email address is required for vendor parties.'],
          },
        },
      },
    })
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/catalogs/pm.party',
    ])

    await page.goto(PM_TEST_ROUTES.genericPartyCatalog)
    await page.getByTitle('Create').click()

    const drawer = page.getByTestId('drawer-panel')
    await expect(drawer).toBeVisible()
    await drawer.locator('[data-validation-key="display"] input').fill('Northwind Services')
    await drawer.locator('[data-validation-key="party_type"] input').fill('Vendor')
    await drawer.getByTitle('Save').click()

    await expect(page).toHaveURL(/\/catalogs\/pm\.party\?panel=new$/)
    await expect(drawer).toBeVisible()
    await expect(drawer.getByText('Please fix the highlighted fields.', { exact: true })).toBeVisible()
    await expect(
      drawer.locator('[data-validation-key="email"]').getByText('Email address is required for vendor parties.', { exact: true }),
    ).toBeVisible()
  })

  test('keeps the receivable payment editor dirty and shows permission denied when save is forbidden', async ({ page }) => {
    await mockGenericMetadataDocumentApis(page, {
      updateFailure: {
        status: 403,
        detail: 'You do not have permission to edit receivable payments.',
        title: 'Forbidden',
      },
    })
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/documents/pm.receivable_payment',
      '/api/audit/entities',
    ])

    await page.goto(existingPaymentPath)

    const memoField = page.locator('[data-validation-key="memo"] textarea')
    await expect(memoField).toHaveValue('April rent payment')
    await memoField.fill('Blocked payment memo update')
    await page.getByTitle('Save').click()

    await expect(page).toHaveURL(new RegExp(`${existingPaymentPath.replace(/\./g, '\\.')}(\\?.*)?$`))
    await expect(page.getByText('You do not have permission to edit receivable payments.', { exact: true })).toBeVisible()
    await expect(memoField).toHaveValue('Blocked payment memo update')
  })
})
