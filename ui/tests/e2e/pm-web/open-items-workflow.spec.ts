import { expect, test } from '@playwright/test'

import { receivablesOpenItemsFixture } from '../fixtures/pmWeb'
import {
  mockPayablesOpenItemsWorkflowApis,
  mockReceivablesOpenItemsWorkflowApis,
  rejectUnhandledApiRequests,
} from '../support/mockApi'
import { PM_TEST_ROUTES } from '../support/routes'

test.describe('pm-web open items workflow', () => {
  test('executes a receivables apply suggestion and refreshes the Applied tab', async ({ page }) => {
    const api = await mockReceivablesOpenItemsWorkflowApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/documents/pm.lease',
      '/api/receivables/open-items/details',
      '/api/receivables/apply',
    ])

    await page.goto(PM_TEST_ROUTES.receivablesOpenItems)

    await page.getByTitle('Apply').click()

    const drawer = page.getByTestId('drawer-panel')
    await expect(drawer).toBeVisible()
    await expect(drawer.getByText('Preview only', { exact: true })).toBeVisible()
    await expect(drawer.getByText('Some charges will remain open', { exact: true })).toBeVisible()
    await drawer.getByRole('button', { name: 'Confirm & Apply', exact: true }).click()

    const pageResult = page.getByTestId('open-items-page-result')
    await expect(pageResult).toBeVisible()
    await expect(pageResult.getByText('Created 1 apply', { exact: true })).toBeVisible()
    await expect(pageResult.getByText('PMT-2001 → RC-1001', { exact: true })).toBeVisible()
    await expect(page.getByText('Applied (2)', { exact: true })).toBeVisible()

    const appliedPanel = page.getByTestId('open-items-applied-panel')
    await expect(appliedPanel).toBeVisible()
    await expect.poll(() => api.getDetails().allocations.length).toBe(2)
    await expect.poll(() => api.getDetails().totalOutstanding).toBe(650)
    await expect.poll(() => api.getDetails().totalCredit).toBe(0)
  })

  test('keeps the receivables apply action disabled when no credit remains', async ({ page }) => {
    const noCreditDetails = structuredClone(receivablesOpenItemsFixture)
    noCreditDetails.credits = noCreditDetails.credits.map((item) => ({
      ...item,
      availableCredit: 0,
    }))
    noCreditDetails.totalCredit = 0

    await mockReceivablesOpenItemsWorkflowApis(page, {
      initialDetails: noCreditDetails,
    })
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/documents/pm.lease',
      '/api/receivables/open-items/details',
      '/api/receivables/apply',
    ])

    await page.goto(PM_TEST_ROUTES.receivablesOpenItems)
    await page.getByTitle('Apply').click()

    const drawer = page.getByTestId('drawer-panel')
    await expect(drawer).toBeVisible()
    await expect(drawer.getByText('No available credits', { exact: true })).toBeVisible()
    await expect(drawer.getByText('Nothing to apply. There are no matching credit sources and outstanding charges.', { exact: true })).toBeVisible()
    await expect(drawer.getByRole('button', { name: 'Confirm & Apply', exact: true })).toBeDisabled()
  })

  test('unapplies an existing receivables allocation from the Applied tab', async ({ page }) => {
    const api = await mockReceivablesOpenItemsWorkflowApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/documents/pm.lease',
      '/api/receivables/open-items/details',
      '/api/receivables/apply',
    ])

    await page.goto(PM_TEST_ROUTES.receivablesOpenItems)
    await page.getByText('Applied (1)', { exact: true }).click()

    const appliedPanel = page.getByTestId('open-items-applied-panel')
    await expect(appliedPanel).toBeVisible()
    await appliedPanel.getByTitle('Unapply').click()

    await expect(page.getByText('Unapply this allocation?', { exact: true })).toBeVisible()
    await expect(page.getByText('Unapply payment PMT-2001 from charge RC-1001 for 675.00?')).toBeVisible()
    await page.getByRole('button', { name: 'Unapply', exact: true }).last().click()

    await expect(page.getByText('Applied (0)', { exact: true })).toBeVisible()
    await expect(appliedPanel.getByText(
      'No applied allocations yet for this lease. Once a credit source is applied to a charge, it will appear here.',
      { exact: true },
    )).toBeVisible()
    await expect.poll(() => api.getDetails().allocations.length).toBe(0)
    await expect.poll(() => api.getDetails().totalOutstanding).toBe(1950)
    await expect.poll(() => api.getDetails().totalCredit).toBe(1300)
  })

  test('executes a payables apply suggestion and allows dismissing the result banner', async ({ page }) => {
    const api = await mockPayablesOpenItemsWorkflowApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/catalogs/pm.party',
      '/api/catalogs/pm.property',
      '/api/payables/open-items/details',
      '/api/payables/apply',
    ])

    await page.goto(PM_TEST_ROUTES.payablesOpenItems)
    await page.getByTitle('Apply').click()

    const drawer = page.getByTestId('drawer-panel')
    await expect(drawer).toBeVisible()
    await expect(drawer.getByText('Some charges will remain open', { exact: true })).toBeVisible()
    await drawer.getByRole('button', { name: 'Execute Apply', exact: true }).click()

    const pageResult = page.getByTestId('open-items-page-result')
    await expect(pageResult).toBeVisible()
    await expect(pageResult.getByText('Created 1 apply', { exact: true })).toBeVisible()
    await expect(pageResult.getByText('CHK-7100 → BILL-4100', { exact: true })).toBeVisible()
    await expect(page.getByText('Applied (2)', { exact: true })).toBeVisible()
    await pageResult.getByRole('button', { name: 'Dismiss', exact: true }).click()
    await expect(pageResult).toHaveCount(0)

    await expect.poll(() => api.getDetails().allocations.length).toBe(2)
    await expect.poll(() => api.getDetails().totalOutstanding).toBe(1600)
    await expect.poll(() => api.getDetails().totalCredit).toBe(0)
  })
})
