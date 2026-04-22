import { expect, test } from '@playwright/test'

import {
  mockCommonPmApis,
  mockGeneralJournalEntryApis,
  rejectUnhandledApiRequests,
} from '../support/mockApi'
import { PM_TEST_IDS, PM_TEST_ROUTES } from '../support/routes'

test.describe('pm-web router redirects', () => {
  test('redirects the receivable apply create route into receivables open items', async ({ page }) => {
    await mockCommonPmApis(page)
    await rejectUnhandledApiRequests(page, ['/api/main-menu'])

    await page.goto(PM_TEST_ROUTES.legacyReceivableApplyCreate)

    await expect(page).toHaveURL(/\/receivables\/open-items$/)
    await expect(page.getByTestId('open-items-page')).toBeVisible()
    await expect(page.getByText('Open Items', { exact: true })).toBeVisible()
  })

  test('redirects the payable apply create route into payables open items', async ({ page }) => {
    await mockCommonPmApis(page)
    await rejectUnhandledApiRequests(page, ['/api/main-menu'])

    await page.goto(PM_TEST_ROUTES.legacyPayableApplyCreate)

    await expect(page).toHaveURL(/\/payables\/open-items$/)
    await expect(page.getByTestId('open-items-page')).toBeVisible()
    await expect(page.getByText('Open Items', { exact: true })).toBeVisible()
  })

  for (const aliasBase of [
    PM_TEST_ROUTES.legacyGeneralJournalEntries,
    PM_TEST_ROUTES.legacyAccountingGeneralJournalEntries,
  ]) {
    test(`redirects ${aliasBase} into the journal entry register`, async ({ page }) => {
      await mockGeneralJournalEntryApis(page)
      await rejectUnhandledApiRequests(page, [
        '/api/main-menu',
        '/api/accounting/general-journal-entries',
      ])

      await page.goto(aliasBase)

      await expect(page).toHaveURL(/\/accounting\/general-journal-entries$/)
      await expect(page.getByTestId('journal-entry-list-page')).toBeVisible()
      await expect(page.getByText('Journal Entries', { exact: true })).toBeVisible()
    })

    test(`redirects ${aliasBase}/new into the modern journal entry create route`, async ({ page }) => {
      await mockGeneralJournalEntryApis(page)
      await rejectUnhandledApiRequests(page, [
        '/api/main-menu',
        '/api/accounting/general-journal-entries',
      ])

      await page.goto(`${aliasBase}/new`)

      await expect(page).toHaveURL(/\/accounting\/general-journal-entries\/new$/)
      await expect(page.getByTestId('journal-entry-edit-page')).toBeVisible()
      await expect(page.getByText('New General Journal Entry', { exact: true })).toBeVisible()
    })

    test(`redirects ${aliasBase}/:id into the modern journal entry edit route`, async ({ page }) => {
      await mockGeneralJournalEntryApis(page)
      await rejectUnhandledApiRequests(page, [
        '/api/main-menu',
        '/api/accounting/general-journal-entries',
      ])

      await page.goto(`${aliasBase}/${PM_TEST_IDS.generalJournalEntryId}`)

      await expect(page).toHaveURL(PM_TEST_ROUTES.existingGeneralJournalEntry)
      await expect(page.getByTestId('journal-entry-edit-page')).toBeVisible()
      await expect(page.getByText('GJE-2026-0042', { exact: true })).toBeVisible()
    })
  }
})
