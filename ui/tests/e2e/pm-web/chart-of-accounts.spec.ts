import { expect, test } from '@playwright/test'

import { chartOfAccountsBaseItemsFixture } from '../fixtures/pmAccounting'
import { mockChartOfAccountsApis, rejectUnhandledApiRequests } from '../support/mockApi'
import { PM_TEST_ROUTES } from '../support/routes'

test.describe('pm-web chart of accounts', () => {
  test('renders the register and exposes create action', async ({ page }) => {
    await mockChartOfAccountsApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/chart-of-accounts',
    ])

    await page.goto(PM_TEST_ROUTES.chartOfAccounts)

    await expect(page.getByText('Chart of Accounts', { exact: true })).toBeVisible()
    await expect(page.getByText('Cash', { exact: true })).toBeVisible()
    await expect(page.getByText('Accounts Payable', { exact: true })).toBeVisible()
    await expect(page.getByTitle('Create')).toBeVisible()
  })

  test('opens the create drawer from route state and saves a new account', async ({ page }) => {
    await mockChartOfAccountsApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/chart-of-accounts',
    ])

    await page.goto(`${PM_TEST_ROUTES.chartOfAccounts}?panel=new`)

    await expect(page).toHaveURL(/\/admin\/chart-of-accounts\?panel=new$/)

    const drawer = page.getByTestId('drawer-panel')
    await expect(drawer).toBeVisible()
    await expect(drawer.getByTestId('chart-of-account-editor')).toBeVisible()
    await expect(drawer.getByText('New account', { exact: true })).toBeVisible()

    await drawer.getByPlaceholder('e.g. 1000').fill('1250')
    await drawer.getByPlaceholder('e.g. Cash').fill('Security Deposit Clearing')
    await drawer.getByTitle('Save').click()

    await expect(page.getByTestId('drawer-panel')).toBeHidden()
    await expect(page.getByText('1250', { exact: true })).toBeVisible()
    await expect(page.getByText('Security Deposit Clearing', { exact: true })).toBeVisible()
  })

  test('opens the edit drawer when an account row is activated', async ({ page }) => {
    await mockChartOfAccountsApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/chart-of-accounts',
    ])

    await page.goto(PM_TEST_ROUTES.chartOfAccounts)

    await page.getByText('Cash', { exact: true }).click()

    await expect(page).toHaveURL(new RegExp([
      '/admin/chart-of-accounts\\?.*panel=edit.*id=',
      chartOfAccountsBaseItemsFixture[0]!.accountId,
      '|/admin/chart-of-accounts\\?.*id=',
      chartOfAccountsBaseItemsFixture[0]!.accountId,
      '.*panel=edit',
    ].join('')))

    const drawer = page.getByTestId('drawer-panel')
    await expect(drawer).toBeVisible()
    await expect(drawer.getByText('1000 — Cash', { exact: true })).toBeVisible()
    await expect(drawer.getByPlaceholder('e.g. 1000')).toHaveValue('1000')
    await expect(drawer.getByPlaceholder('e.g. Cash')).toHaveValue('Cash')
  })

  test('saves changes for an existing account', async ({ page }) => {
    await mockChartOfAccountsApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/chart-of-accounts',
    ])

    await page.goto(`${PM_TEST_ROUTES.chartOfAccounts}?panel=edit&id=${chartOfAccountsBaseItemsFixture[0]!.accountId}`)

    const drawer = page.getByTestId('drawer-panel')
    const nameInput = drawer.getByPlaceholder('e.g. Cash')

    await expect(drawer).toBeVisible()
    await expect(nameInput).toHaveValue('Cash')

    await nameInput.fill('Cash Operating')
    await drawer.getByTitle('Save').click()

    await expect(page.getByTestId('drawer-panel')).toHaveCount(0)
    await expect(page.getByText('Cash Operating', { exact: true })).toBeVisible()
  })
})
