import { expect, test, type Page } from '@playwright/test'

import { receivablePaymentAuditFixture } from '../fixtures/pmMetadataRoutes'
import {
  mockGenericMetadataDocumentApis,
  rejectUnhandledApiRequests,
} from '../support/mockApi'
import { PM_TEST_IDS } from '../support/routes'

const documentPath = `/documents/pm.receivable_payment/${PM_TEST_IDS.receivablePaymentDocumentId}`

async function openExistingReceivablePayment(page: Page) {
  await page.goto(documentPath)
  await expect(page).toHaveURL(new RegExp(`${documentPath.replace(/\./g, '\\.')}(\\?.*)?$`))
  await expect(page.getByText('Receivable Payment RP-2026-0007', { exact: true })).toBeVisible()
  await expect(page.locator('[data-validation-key="payment_reference"] input')).toHaveValue('LOCKBOX-2048')
}

async function openMoreAction(page: Page, actionName: string) {
  await page.getByRole('button', { name: 'More actions' }).click()
  await page.getByRole('menuitem', { name: actionName, exact: true }).click()
}

test.describe('pm-web document side flows', () => {
  test('opens the audit log from the editor and shows change history', async ({ page }) => {
    await mockGenericMetadataDocumentApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/documents/pm.receivable_payment',
      '/api/audit/entities',
    ])

    await openExistingReceivablePayment(page)
    await openMoreAction(page, 'Audit log')

    const auditDrawer = page.getByTestId('drawer-panel')
    await expect(auditDrawer).toBeVisible()
    await expect(auditDrawer.getByTestId('drawer-body').getByText('Audit Log', { exact: true })).toBeVisible()
    await expect(auditDrawer.getByText('Memo', { exact: true })).toBeVisible()
    await expect(auditDrawer.getByText('April payment received at lockbox', { exact: true })).toBeVisible()
    await expect(auditDrawer.getByText(receivablePaymentAuditFixture.items[0]?.actor?.displayName ?? '', { exact: false })).toBeVisible()

    await auditDrawer.getByTitle('Close').click()
    await expect(page.getByTestId('drawer-panel')).toHaveCount(0)
  })

  test('opens the accounting effects page from the editor and returns to the document', async ({ page }) => {
    await mockGenericMetadataDocumentApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/documents/pm.receivable_payment',
      '/api/audit/entities',
    ])

    await openExistingReceivablePayment(page)
    await openMoreAction(page, 'Accounting entries / effects')

    await expect(page).toHaveURL(new RegExp(`/documents/pm\\.receivable_payment/${PM_TEST_IDS.receivablePaymentDocumentId}/effects(\\?.*)?$`))
    await expect(page.getByText('Accounting Entries (1)', { exact: true })).toBeVisible()
    await expect(page.getByText('Operational Registers (1)', { exact: true })).toBeVisible()
    await expect(page.getByText('Reference Registers (1)', { exact: true })).toBeVisible()
    await expect(page.getByText('1000 — Cash', { exact: true })).toBeVisible()
    await expect(page.getByText('1200 — Tenant A/R', { exact: true })).toBeVisible()

    await page.getByRole('tab', { name: 'Operational Registers (1)', exact: true }).click()
    await expect(page.getByText('Tenant Balances', { exact: true })).toBeVisible()

    await page.getByRole('tab', { name: 'Reference Registers (1)', exact: true }).click()
    await expect(page.getByText('Receivables Open Items', { exact: true })).toBeVisible()

    await page.getByTitle('Open document').click()
    await expect(page).toHaveURL(new RegExp(`${documentPath.replace(/\./g, '\\.')}(\\?.*)?$`))
  })

  test('opens the document flow page from the editor and returns to the source document', async ({ page }) => {
    await mockGenericMetadataDocumentApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/documents/pm.receivable_payment',
      '/api/audit/entities',
    ])

    await openExistingReceivablePayment(page)
    await openMoreAction(page, 'Document flow')

    await expect(page).toHaveURL(new RegExp(`/documents/pm\\.receivable_payment/${PM_TEST_IDS.receivablePaymentDocumentId}/flow(\\?.*)?$`))
    await expect(page.getByText('Receivable Payment RP-2026-0007', { exact: true }).first()).toBeVisible()
    await expect(page.getByText('Rent Charge RC-2026-0208', { exact: true })).toBeVisible()
    await expect(page.getByText('Receivable Apply RA-2026-0091', { exact: true })).toBeVisible()
    await expect(page.getByText('1,084.00', { exact: true }).first()).toBeVisible()

    await page.getByTitle('Open document').click()
    await expect(page).toHaveURL(new RegExp(`${documentPath.replace(/\./g, '\\.')}(\\?.*)?$`))
  })

  test('opens the print preview from the editor', async ({ page }) => {
    await page.addInitScript(() => {
      window.print = () => undefined
    })

    await mockGenericMetadataDocumentApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/documents/pm.receivable_payment',
      '/api/audit/entities',
    ])

    await openExistingReceivablePayment(page)
    await openMoreAction(page, 'Print')

    await expect(page).toHaveURL(new RegExp(`/documents/pm\\.receivable_payment/${PM_TEST_IDS.receivablePaymentDocumentId}/print(\\?.*)?$`))
    await expect(page.getByText('Print preview', { exact: true })).toBeVisible()
    await expect(page.getByText('Receivable Payment RP-2026-0007', { exact: true })).toBeVisible()
    await expect(page.getByText('Payment reference', { exact: true })).toBeVisible()
    await expect(page.getByText('LOCKBOX-2048', { exact: true })).toBeVisible()
    await expect(page.getByText('Memo', { exact: true })).toBeVisible()
    await expect(page.getByText('April rent payment', { exact: true })).toBeVisible()

    await page.getByRole('button', { name: 'Back' }).click()
    await expect(page).toHaveURL(new RegExp(`${documentPath.replace(/\./g, '\\.')}(\\?.*)?$`))
  })
})
