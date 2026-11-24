import { nextTick } from 'vue'
import { render } from 'vitest-browser-vue'
import { afterEach, expect, it, vi } from 'vitest'

const state = vi.hoisted(() => ({ get: vi.fn(), push: vi.fn() }))
vi.hoisted(() => vi.stubGlobal('__NGB_RUNTIME_CONFIG__', {
  VITE_KEYCLOAK_URL: 'http://localhost:18081', VITE_KEYCLOAK_REALM: 'component-tests', VITE_KEYCLOAK_CLIENT_ID: 'component-tests',
}))
vi.mock('@ngbplatform/ui', async original => ({ ...await original<typeof import('@ngbplatform/ui')>(), httpGet: state.get }))
vi.mock('vue-router', async original => ({ ...await original<typeof import('vue-router')>(), useRouter: () => ({ push: state.push }) }))
afterEach(() => vi.resetAllMocks())

const snapshot = { asOf: '2026-04-03', parameters: { to_utc: '2026-04-03' }, total: 5, followUp: 1, rejected: 2, passed: 2 }

it('opens each measured report and retains the chosen day across a failed refresh', async () => {
  const { default: Dashboard } = await import('../../src/pages/InspectionDashboardPage.vue')
  const { ApiError } = await import('@ngbplatform/ui')
  state.get.mockResolvedValue(snapshot)
  const screen = render(Dashboard)
  await expect.element(screen.getByText(/Through/)).toBeVisible()
  for (const name of [/flagged for follow-up/, /Rejected inspections/, /Completed inspections/, /Marked as passed/]) {
    await screen.getByRole('button', { name }).click()
  }
  expect(state.push.mock.calls.map(([route]) => route.path)).toEqual(Array(4).fill('/reports/example.inspection-summary'))
  const refresh = screen.getByRole('button', { name: 'Refresh' })
  for (const failure of [new Error('service'), new ApiError({ message: 'denied', status: 403, url: '/api/inspection-dashboard' })]) {
    state.get.mockRejectedValueOnce(failure)
    await refresh.click()
    await expect.element(screen.getByText(failure instanceof ApiError ? /permission/ : /Unable to load/)).toBeVisible()
    await refresh.click()
    await expect.element(screen.getByText(/Through/)).toBeVisible()
    expect(state.get.mock.lastCall![0]).toBe('/api/inspection-dashboard?asOf=2026-04-03')
  }
  state.get.mockResolvedValue({ ...snapshot, followUp: 0, rejected: 0 })
  await refresh.click()
  await expect.element(screen.getByText('Clear').first()).toBeVisible()
})

it.each(['success', 'failure'])('discards late %s after leaving while the dashboard loads', async outcome => {
  const { default: Dashboard } = await import('../../src/pages/InspectionDashboardPage.vue')
  const pending = Promise.withResolvers<unknown>()
  state.get.mockReturnValue(pending.promise)
  const screen = render(Dashboard)
  await expect.element(screen.getByRole('status')).toHaveTextContent('Loading')
  const signal = state.get.mock.lastCall![2].signal as AbortSignal
  await screen.unmount()
  expect(signal.aborted).toBe(true)
  if (outcome === 'success') pending.resolve(snapshot)
  else pending.reject(new Error('late response'))
  await nextTick()
})
