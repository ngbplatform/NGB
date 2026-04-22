import { expect, test } from '@playwright/test'

import { mockGeneralJournalEntryApis, rejectUnhandledApiRequests } from '../support/mockApi'
import { PM_TEST_ROUTES } from '../support/routes'

test.describe('pm-web general journal entries', () => {
  test('renders the journal entry register and exposes create action', async ({ page }) => {
    await mockGeneralJournalEntryApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/accounting/general-journal-entries',
    ])

    await page.goto(PM_TEST_ROUTES.generalJournalEntries)

    await expect(page.getByTestId('journal-entry-list-page')).toBeVisible()
    await expect(page.getByText('Journal Entries')).toBeVisible()
    await expect(page.getByText('GJE-2026-0042')).toBeVisible()
    await expect(page.getByTitle('Create')).toBeVisible()
  })

  test('saves a new draft and lands on the document route', async ({ page }) => {
    await mockGeneralJournalEntryApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/accounting/general-journal-entries',
    ])

    await page.goto(PM_TEST_ROUTES.newGeneralJournalEntry)

    await expect(page.getByTestId('journal-entry-edit-page')).toBeVisible()
    await expect(page.getByText('New General Journal Entry')).toBeVisible()

    await page.getByPlaceholder('Explain the journal entry').fill('Quarter-end accrual shell')
    await page.getByPlaceholder('External ticket, import id, or source ref').fill('Q2-CLOSE')

    await page.getByTitle('Save').click()

    await expect(page.getByText('Draft was saved.', { exact: true })).toBeVisible()
    await expect(page).toHaveURL(/\/accounting\/general-journal-entries\/44444444-dddd-4ddd-8ddd-444444444444$/)
    await expect(page.getByPlaceholder('Explain the journal entry')).toHaveValue('Quarter-end accrual shell')
    await expect(page.getByText('GJE-2026-0042', { exact: true })).toBeVisible()
  })

  test('opens an existing journal entry from the register and returns to the list', async ({ page }) => {
    await mockGeneralJournalEntryApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/accounting/general-journal-entries',
    ])

    await page.goto(PM_TEST_ROUTES.generalJournalEntries)

    await page.getByText('GJE-2026-0042', { exact: true }).click()

    await expect(page.getByTestId('journal-entry-edit-page')).toBeVisible()
    await expect(page).toHaveURL(/\/accounting\/general-journal-entries\/44444444-dddd-4ddd-8ddd-444444444444$/)
    await expect(page.getByText('GJE-2026-0042', { exact: true })).toBeVisible()

    await page.getByRole('tab', { name: 'Workflow' }).click()
    await expect(page.getByText('Workflow actors are captured automatically from the authenticated current user.', { exact: true })).toBeVisible()

    await page.getByTitle('Close').click()

    await expect(page.getByTestId('journal-entry-list-page')).toBeVisible()
    await expect(page).toHaveURL(/\/accounting\/general-journal-entries(\?.*)?$/)
  })
})
