import { expect, test, type Page } from '@playwright/test'

import { payablesOpenItemsFixture, receivablesOpenItemsFixture } from '../fixtures/pmWeb'
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

// Exercise the real page, workflow, and HTTP client against server limit rules.
// One credit covers 26 separate charges, requiring two atomic batches.
for (const area of ['receivables', 'payables'] as const) {
  test(`${area} applies 26 charges in batches of 25 and 1 without exceeding API limits`, async ({ page }) => {
    const setup = async (page: Page) => {
      if (area === 'receivables') {
        const details = structuredClone(receivablesOpenItemsFixture)
        details.charges = Array.from({ length: 26 }, (_, index) => ({
          ...details.charges[0]!,
          chargeDocumentId: `aaaaaaaa-aaaa-4aaa-8aaa-${String(index + 1).padStart(12, '0')}`,
          number: `RC-${index + 1}`,
          originalAmount: 1,
          outstandingAmount: 1,
        }))
        details.credits = [{ ...details.credits[0]!, originalAmount: 26, availableCredit: 26 }]
        details.allocations = []
        details.totalOutstanding = 26
        details.totalCredit = 26
        return mockReceivablesOpenItemsWorkflowApis(page, { initialDetails: details })
      }

      const details = structuredClone(payablesOpenItemsFixture)
      details.charges = Array.from({ length: 26 }, (_, index) => ({
        ...details.charges[0]!,
        chargeDocumentId: `bbbbbbbb-bbbb-4bbb-8bbb-${String(index + 1).padStart(12, '0')}`,
        number: `BILL-${index + 1}`,
        originalAmount: 1,
        outstandingAmount: 1,
      }))
      details.credits = [{ ...details.credits[0]!, originalAmount: 26, availableCredit: 26 }]
      details.allocations = []
      details.totalOutstanding = 26
      details.totalCredit = 26
      return mockPayablesOpenItemsWorkflowApis(page, { initialDetails: details })
    }
    const api = await setup(page)
    const suggestionLimits: number[] = []
    const suggestPath = `/api/${area}/apply/fifo/suggest${area === 'receivables' ? '/lease' : ''}`
    await page.route(`**${suggestPath}`, async (route) => {
      const request = route.request().postDataJSON()
      suggestionLimits.push(request.limit)
      if (request.limit > 100) {
        await route.fulfill({ status: 400, json: { title: 'Limit must not exceed 100.', status: 400 } })
        return
      }
      expect(request.createDrafts).toBe(false)
      const details = api.getDetails()
      const credit = details.credits[0]!
      const charges = details.charges.filter((charge) => charge.outstandingAmount > 0).slice(0, request.limit)
      const creditDate = 'receivedOnUtc' in credit ? credit.receivedOnUtc : credit.creditDocumentDateUtc
      const remaining = details.totalOutstanding - charges.length
      await route.fulfill({ json: {
        ...details,
        totalApplied: charges.length,
        remainingOutstanding: remaining,
        remainingCredit: remaining,
        suggestedApplies: charges.map((charge, index) => ({
          applyId: null,
          creditDocumentId: credit.creditDocumentId,
          creditDocumentType: credit.documentType,
          creditDocumentDisplay: credit.creditDocumentDisplay,
          creditDocumentDateUtc: creditDate,
          creditAmountBefore: credit.availableCredit - index,
          creditAmountAfter: credit.availableCredit - index - 1,
          chargeDocumentId: charge.chargeDocumentId,
          chargeDisplay: charge.number,
          chargeDueOnUtc: charge.dueOnUtc,
          chargeOutstandingBefore: 1,
          chargeOutstandingAfter: 0,
          amount: 1,
          applyPayload: { fields: {
            credit_document_id: credit.creditDocumentId,
            charge_document_id: charge.chargeDocumentId,
            applied_on_utc: creditDate,
            amount: 1,
          } },
        })),
        warnings: remaining > 0 ? [{ code: 'limit_reached', message: 'The suggestion limit was reached. Some items may remain unapplied.' }] : [],
      } })
    })
    await page.route(`**/api/${area}/apply/batch`, async (route) => {
      if (route.request().postDataJSON().applies.length > 25) {
        await route.fulfill({ status: 400, json: { title: 'You can apply at most 25 items at a time.', status: 400 } })
        return
      }
      await route.fallback()
    })
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu', '/api/documents/pm.lease', '/api/catalogs/pm.party', '/api/catalogs/pm.property',
      `/api/${area}/open-items/details`, `/api/${area}/apply`,
    ])
    await page.goto(area === 'receivables' ? PM_TEST_ROUTES.receivablesOpenItems : PM_TEST_ROUTES.payablesOpenItems)

    const drawer = page.getByTestId('drawer-panel')
    const pageResult = page.getByTestId('open-items-page-result')
    const confirmLabel = area === 'receivables' ? 'Confirm & Apply' : 'Execute Apply'
    for (const batchSize of [25, 1]) {
      await page.getByTitle('Apply', { exact: true }).click()
      await expect(drawer).toBeVisible()
      await expect(drawer.getByText(`${batchSize} ${batchSize === 1 ? 'apply' : 'applies'} totaling ${batchSize.toFixed(2)}`, { exact: true })).toBeVisible()
      await drawer.getByRole('button', { name: confirmLabel, exact: true }).click()
      await expect(drawer).not.toBeVisible()
      await expect(pageResult.getByText(`Created ${batchSize} ${batchSize === 1 ? 'apply' : 'applies'}`, { exact: true })).toBeVisible()
      await expect.poll(() => api.getDetails().totalOutstanding).toBe(batchSize === 25 ? 1 : 0)
      await expect.poll(() => api.getDetails().totalCredit).toBe(batchSize === 25 ? 1 : 0)
    }
    expect(suggestionLimits).toEqual([25, 25])
    expect(api.getExecuteRequests().map((request) => request.applies.length)).toEqual([25, 1])
    const details = api.getDetails()
    expect(details.allocations).toHaveLength(26)
    expect(new Set(details.allocations.map((item) => item.chargeDocumentId)).size).toBe(26)
    expect(details.charges.every((item) => item.outstandingAmount === 0)).toBe(true)
  })
}
