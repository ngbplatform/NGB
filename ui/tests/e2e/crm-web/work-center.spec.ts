import { test } from '@playwright/test'

import { CRM_SMOKE_PROFILE } from '../support/verticalSmokeApi'
import {
  verifyVerticalWorkCenter,
  verifyVerticalWorkCenterPreferences,
} from '../support/workCenterScenarios'

test.describe('crm-web Work Center', () => {
  test('wires the shared Work Center feed and mutations to the CRM vertical', async ({ page }) => {
    await verifyVerticalWorkCenter(page, CRM_SMOKE_PROFILE, 'crm')
  })

  test('keeps CRM task and notification preferences independently configurable', async ({ page }) => {
    await verifyVerticalWorkCenterPreferences(page, CRM_SMOKE_PROFILE, 'crm')
  })
})
