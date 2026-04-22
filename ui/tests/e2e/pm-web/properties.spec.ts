import { expect, test } from '@playwright/test'

import { expectCardsToStackVertically, expectNoHorizontalPageOverflow } from '../support/assertions'
import { mockPropertiesApis, rejectUnhandledApiRequests } from '../support/mockApi'
import { PM_TEST_ROUTES } from '../support/routes'

test.describe('pm-web properties mobile layout', () => {
  test('properties registers stay within the mobile viewport', async ({ page }) => {
    await mockPropertiesApis(page)
    await rejectUnhandledApiRequests(page, [
      '/api/main-menu',
      '/api/catalogs/pm.property',
      '/api/reports/pm.building.summary/execute',
    ])

    await page.goto(PM_TEST_ROUTES.properties)

    const propertiesPage = page.getByTestId('properties-page')
    const buildingsPanel = page.getByTestId('properties-buildings-panel')
    const unitsPanel = page.getByTestId('properties-units-panel')

    await expect(propertiesPage).toBeVisible()
    await expect(propertiesPage.getByText('Properties', { exact: true })).toBeVisible()
    await expect(buildingsPanel).toBeVisible()
    await expect(unitsPanel).toBeVisible()
    await expect(buildingsPanel.getByText('Riverfront Tower', { exact: true })).toBeVisible()

    await expectNoHorizontalPageOverflow(page)
    await expectCardsToStackVertically(page.locator('[data-testid="properties-panels"] > [data-testid^="properties-"]'))
  })
})
