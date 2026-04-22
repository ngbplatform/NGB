import { expect, test } from '@playwright/test'

import {
  partyCatalogBaseItemsFixture,
  receivablePaymentBaseDocumentsFixture,
} from '../fixtures/pmMetadataRoutes'
import {
  mockGenericMetadataCatalogApis,
  mockGenericMetadataDocumentApis,
  rejectUnhandledApiRequests,
} from '../support/mockApi'
import { PM_TEST_IDS, PM_TEST_ROUTES } from '../support/routes'

test.describe('pm-web generic metadata routes', () => {
  test('renders the generic party catalog route and saves a new record from the drawer', async ({ page }) => {
    await mockGenericMetadataCatalogApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/catalogs/pm.party',
    ])

    await page.goto(PM_TEST_ROUTES.genericPartyCatalog)

    await expect(page.getByText('Parties', { exact: true })).toBeVisible()
    await expect(page.getByText('Harbor State Bank', { exact: true })).toBeVisible()
    await expect(page.getByText('Maple Tenant LLC', { exact: true })).toBeVisible()

    await page.getByTitle('Create').click()

    const drawer = page.getByTestId('drawer-panel')
    await expect(drawer).toBeVisible()
    await expect(page).toHaveURL(/\/catalogs\/pm\.party\?panel=new$/)

    await drawer.locator('[data-validation-key="display"] input').fill('Northwind Services')
    await drawer.locator('[data-validation-key="party_type"] input').fill('Vendor')
    await drawer.locator('[data-validation-key="email"] input').fill('ap@northwind.example')
    await drawer.getByTitle('Save').click()

    await expect(page.getByTestId('drawer-panel')).toHaveCount(0)
    await expect(page.getByText('Northwind Services', { exact: true })).toBeVisible()
    await expect(page.getByText('ap@northwind.example', { exact: true })).toBeVisible()
  })

  test('opens an existing party in compact mode, expands to full page, and saves changes', async ({ page }) => {
    await mockGenericMetadataCatalogApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/catalogs/pm.party',
    ])

    await page.goto(PM_TEST_ROUTES.genericPartyCatalog)

    await page.getByText('Harbor State Bank', { exact: true }).click()

    const compactDrawer = page.getByTestId('drawer-panel')
    await expect(compactDrawer).toBeVisible()
    await expect(page).toHaveURL(new RegExp([
      '/catalogs/pm\\.party\\?.*panel=edit.*id=',
      PM_TEST_IDS.partyCatalogPrimaryId,
      '|/catalogs/pm\\.party\\?.*id=',
      PM_TEST_IDS.partyCatalogPrimaryId,
      '.*panel=edit',
    ].join('')))

    await compactDrawer.getByTitle('Open full page').click()

    await expect(page).toHaveURL(new RegExp(`/catalogs/pm\\.party/${PM_TEST_IDS.partyCatalogPrimaryId}(\\?.*)?$`))

    const displayInput = page.locator('[data-validation-key="display"] input')
    await expect(displayInput).toHaveValue(partyCatalogBaseItemsFixture[0]?.display ?? '')
    await displayInput.fill('Harbor State Bank Operating')
    await page.getByTitle('Save').click()

    await expect(displayInput).toHaveValue('Harbor State Bank Operating')

    await page.getByRole('button', { name: 'Close' }).click()
    await expect(page).toHaveURL(/\/catalogs\/pm\.party(\?.*)?$/)
    await expect(page.getByText('Harbor State Bank Operating', { exact: true })).toBeVisible()
  })

  test('renders the generic receivable payments register and saves a new draft from the drawer', async ({ page }) => {
    await mockGenericMetadataDocumentApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/documents/pm.receivable_payment',
      '/api/audit/entities',
    ])

    await page.goto(PM_TEST_ROUTES.genericReceivablePayments)

    await expect(page.getByText('Payments', { exact: true })).toBeVisible()
    await expect(page.getByText('Receivable Payment RP-2026-0007', { exact: true })).toBeVisible()

    await page.getByTitle('Create').click()

    const drawer = page.getByTestId('drawer-panel')
    await expect(drawer).toBeVisible()
    await expect(drawer.getByText('New Receivable Payments', { exact: true })).toBeVisible()

    await drawer.locator('[data-validation-key="payment_reference"] input').fill('LOCKBOX-NEW')
    await drawer.locator('[data-validation-key="memo"] textarea').fill('New payment draft')
    await drawer.getByTitle('Save').click()

    await expect(drawer.getByText('Receivable Payment draft', { exact: true })).toBeVisible()
    await expect(drawer.locator('[data-validation-key="payment_reference"] input')).toHaveValue('LOCKBOX-NEW')
    await expect(drawer.locator('[data-validation-key="memo"] textarea')).toHaveValue('New payment draft')

    await drawer.getByTitle('Close').click()
    await expect(page.getByTestId('drawer-panel')).toHaveCount(0)
    await expect(page.getByText('Receivable Payment draft', { exact: true })).toBeVisible()
  })

  test('opens an existing receivable payment from the register in compact mode', async ({ page }) => {
    await mockGenericMetadataDocumentApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/documents/pm.receivable_payment',
      '/api/audit/entities',
    ])

    await page.goto(PM_TEST_ROUTES.genericReceivablePayments)

    await page.getByText(receivablePaymentBaseDocumentsFixture[0]?.display ?? '', { exact: true }).click()

    const drawer = page.getByTestId('drawer-panel')
    await expect(drawer).toBeVisible()
    await expect(drawer.getByText(receivablePaymentBaseDocumentsFixture[0]?.display ?? '', { exact: true })).toBeVisible()
    await expect(drawer.locator('[data-validation-key="payment_reference"] input')).toHaveValue('LOCKBOX-2048')
    await expect(drawer.locator('[data-validation-key="memo"] textarea')).toHaveValue('April rent payment')
  })
})
