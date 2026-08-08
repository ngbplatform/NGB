import { test } from '@playwright/test'

import { TRADE_SMOKE_PROFILE } from '../support/verticalSmokeApi'
import {
  verifyVerticalWorkCenter,
  verifyVerticalWorkCenterPreferences,
} from '../support/workCenterScenarios'

test.describe('trade-web Work Center', () => {
  test('wires the shared Work Center feed and mutations to the Trade vertical', async ({ page }) => {
    await verifyVerticalWorkCenter(page, TRADE_SMOKE_PROFILE, 'trade')
  })

  test('keeps Trade task and notification preferences independently configurable', async ({ page }) => {
    await verifyVerticalWorkCenterPreferences(page, TRADE_SMOKE_PROFILE, 'trade')
  })
})
