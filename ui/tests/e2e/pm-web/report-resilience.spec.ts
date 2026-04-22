import { expect, test, type Page } from '@playwright/test'

import {
  occupancySummaryReportEmptyExecutionFixture,
  occupancySummaryReportPagedFirstExecutionFixture,
  occupancySummaryReportPagedSecondExecutionFixture,
} from '../fixtures/pmReports'
import { mockOccupancySummaryReportApis, rejectUnhandledApiRequests } from '../support/mockApi'
import { PM_TEST_ROUTES } from '../support/routes'

async function openCreateVariantDialog(page: Page) {
  await page.getByTitle('Composer').click()

  const drawer = page.getByTestId('drawer-panel')
  await expect(drawer).toBeVisible()
  await drawer.getByRole('tab', { name: 'Variant' }).click()
  await drawer.getByTitle('Create variant').click()

  return page.getByRole('dialog', { name: 'Create variant' })
}

test.describe('pm-web report resilience', () => {
  test('shows an execution error banner when the initial report run fails', async ({ page }) => {
    await mockOccupancySummaryReportApis(page, {
      executeFailure: {
        detail: 'Occupancy cube timed out during execution.',
      },
    })
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/report-definitions/pm.occupancy.summary',
      '/api/reports/pm.occupancy.summary',
    ])

    await page.goto(PM_TEST_ROUTES.occupancySummaryReport)

    await expect(page.getByTestId('report-page')).toBeVisible()
    await expect(page.getByText('Occupancy cube timed out during execution.', { exact: true })).toBeVisible()
    await expect(page.getByTestId('report-sheet-empty')).toBeVisible()
    await expect(page.getByTestId('report-sheet-empty').getByText('No rows for this layout', { exact: true })).toBeVisible()
  })

  test('renders the empty report state when execution returns no rows', async ({ page }) => {
    await mockOccupancySummaryReportApis(page, {
      executionResponse: occupancySummaryReportEmptyExecutionFixture,
    })
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/report-definitions/pm.occupancy.summary',
      '/api/reports/pm.occupancy.summary',
    ])

    await page.goto(PM_TEST_ROUTES.occupancySummaryReport)

    const emptyState = page.getByTestId('report-sheet-empty')
    await expect(emptyState).toBeVisible()
    await expect(emptyState.getByText('No rows for this layout', { exact: true })).toBeVisible()
    await expect(emptyState.getByText('Run the report to review current portfolio occupancy.', { exact: true })).toBeVisible()
    await expect(page.getByText('Riverfront Tower', { exact: true })).toHaveCount(0)
  })

  test('loads the next report page and reaches end-of-list state', async ({ page }) => {
    await mockOccupancySummaryReportApis(page, {
      executionResponse: occupancySummaryReportPagedFirstExecutionFixture,
      appendResponsesByCursor: {
        'cursor:page:2': occupancySummaryReportPagedSecondExecutionFixture,
      },
    })
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/report-definitions/pm.occupancy.summary',
      '/api/reports/pm.occupancy.summary',
    ])

    await page.goto(PM_TEST_ROUTES.occupancySummaryReport)

    const reportSheet = page.getByTestId('report-sheet-scroll')
    await expect(reportSheet).toBeVisible()
    await expect(reportSheet.getByText('Riverfront Tower', { exact: true })).toBeVisible()

    await expect(reportSheet.getByText('Harbor View Plaza', { exact: true })).toBeVisible()
    await expect(page.getByText('Loaded 2 properties. End of list.', { exact: true })).toBeVisible()
  })

  test('shows a load-more error while keeping the already loaded rows visible', async ({ page }) => {
    await mockOccupancySummaryReportApis(page, {
      executionResponse: occupancySummaryReportPagedFirstExecutionFixture,
      appendFailuresByCursor: {
        'cursor:page:2': {
          detail: 'Could not load the next page of occupancy rows.',
        },
      },
    })
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/report-definitions/pm.occupancy.summary',
      '/api/reports/pm.occupancy.summary',
    ])

    await page.goto(PM_TEST_ROUTES.occupancySummaryReport)

    const reportSheet = page.getByTestId('report-sheet-scroll')
    await expect(reportSheet.getByText('Riverfront Tower', { exact: true })).toBeVisible()

    await expect(page.getByText('Could not load the next page of occupancy rows.', { exact: true })).toBeVisible()
    await expect(reportSheet.getByText('Riverfront Tower', { exact: true })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Load more', exact: true })).toBeVisible()
  })

  test('keeps the create variant dialog open when saving a variant fails', async ({ page }) => {
    await mockOccupancySummaryReportApis(page, {
      saveVariantFailure: {
        detail: 'Variant name already exists in the shared workspace.',
      },
    })
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/report-definitions/pm.occupancy.summary',
      '/api/reports/pm.occupancy.summary',
    ])

    await page.goto(PM_TEST_ROUTES.occupancySummaryReport)

    const dialog = await openCreateVariantDialog(page)
    const input = dialog.getByRole('textbox', { name: 'Month-end ledger' })

    await input.evaluate((element, value) => {
      const host = element as HTMLInputElement
      host.value = String(value ?? '')
      host.dispatchEvent(new Event('input', { bubbles: true }))
      host.dispatchEvent(new Event('change', { bubbles: true }))
    }, 'Month-end Snapshot')
    await expect(input).toHaveValue('Month-end Snapshot')
    await dialog.getByRole('button', { name: 'Create', exact: true }).click()

    await expect(page.getByText('Create variant', { exact: true }).last()).toBeVisible()
    await expect(page.getByText('Variant name already exists in the shared workspace.', { exact: true })).toBeVisible()
    await expect(input).toHaveValue('Month-end Snapshot')
  })
})
