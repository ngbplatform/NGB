import { test } from '@playwright/test'

import { AGENCY_BILLING_SMOKE_PROFILE } from '../support/verticalSmokeApi'
import {
  verifyVerticalWorkCenter,
  verifyVerticalWorkCenterPreferences,
} from '../support/workCenterScenarios'

test.describe('agency-billing-web Work Center', () => {
  test('wires the shared Work Center feed and mutations to the Agency Billing vertical', async ({ page }) => {
    await verifyVerticalWorkCenter(page, AGENCY_BILLING_SMOKE_PROFILE, 'ab')
  })

  test('keeps Agency Billing task and notification preferences independently configurable', async ({ page }) => {
    await verifyVerticalWorkCenterPreferences(page, AGENCY_BILLING_SMOKE_PROFILE, 'ab')
  })
})
