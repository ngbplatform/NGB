import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'

import { StubIcon } from './stubs'

vi.mock('../../../../src/ngb/primitives/NgbIcon.vue', () => ({
  default: StubIcon,
}))

import NgbDashboardStatusBanner from '../../../../src/ngb/site/NgbDashboardStatusBanner.vue'

const WarningHarness = defineComponent({
  setup() {
    return () => h(NgbDashboardStatusBanner, {
      warnings: [' Late data ', 'Stale occupancy cache', 'Late data', '', 'Blocked ledger sync'],
      warningLimit: 2,
      warningTitle: 'Partial dashboard data',
    })
  },
})

const ErrorHarness = defineComponent({
  setup() {
    return () => h(NgbDashboardStatusBanner, {
      error: 'Dashboard request failed',
      warnings: ['Late data'],
      errorTitle: 'Dashboard error',
    })
  },
})

test('renders warning mode with deduped warnings up to the configured limit', async () => {
  await page.viewport(1024, 800)

  const view = await render(WarningHarness)

  await expect.element(view.getByText('Partial dashboard data')).toBeVisible()
  await expect.element(view.getByText('Late data')).toBeVisible()
  await expect.element(view.getByText('Stale occupancy cache')).toBeVisible()
  expect(document.body.textContent ?? '').not.toContain('Blocked ledger sync')
  await expect.element(view.getByTestId('icon-help-circle')).toBeVisible()
})

test('renders error mode with priority over warnings', async () => {
  await page.viewport(1024, 800)

  const view = await render(ErrorHarness)

  await expect.element(view.getByText('Dashboard error')).toBeVisible()
  await expect.element(view.getByText('Dashboard request failed')).toBeVisible()
  expect(document.body.textContent ?? '').not.toContain('Late data')
  await expect.element(view.getByTestId('icon-circle-x')).toBeVisible()
})
