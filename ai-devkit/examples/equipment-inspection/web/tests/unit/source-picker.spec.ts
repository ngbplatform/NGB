import { defineComponent, h, nextTick, ref } from 'vue'
import { render } from 'vitest-browser-vue'
import { afterEach, expect, it, vi } from 'vitest'
import '@ngbplatform/ui/styles'

const state = vi.hoisted(() => ({ get: vi.fn() }))
vi.hoisted(() => vi.stubGlobal('__NGB_RUNTIME_CONFIG__', {
  VITE_KEYCLOAK_URL: 'http://localhost:18081', VITE_KEYCLOAK_REALM: 'component-tests', VITE_KEYCLOAK_CLIENT_ID: 'component-tests',
}))
vi.mock('keycloak-js', () => ({
  default: class {
    authenticated = true
    token = 'component-test-token'
    async init() {
      return true
    }

    async updateToken() {
      return false
    }
  },
}))
vi.mock('@ngbplatform/ui', async original => ({ ...await original<typeof import('@ngbplatform/ui')>(), httpGet: state.get }))
afterEach(() => vi.resetAllMocks())

function deferred() {
  let resolve!: (value: unknown) => void
  let reject!: (error: unknown) => void
  const promise = new Promise((accept, fail) => {
    resolve = accept
    reject = fail
  })
  return { promise, resolve, reject }
}

async function fixture() {
  const equipmentId = ref('equipment-one')
  const sources = [{ id: 'source', number: 'I-2026-000001', result: 'Passed' }]
  state.get.mockImplementation(async (url: string) => url.includes('/source?') ? sources[0]
    : url.includes('/api/catalogs/') ? { display: 'Inspection bench' } : sources)
  const { default: Picker } = await import('../../src/InspectionSourcePicker.vue')
  const screen = render(defineComponent({ setup: () => () => h(Picker, { equipmentId: equipmentId.value }) }))
  await vi.waitFor(() => expect(state.get).toHaveBeenCalled())
  async function select() {
    await screen.getByRole('button', { name: 'Show options' }).click()
    await screen.getByRole('option', { name: sources[0].number, exact: true }).click()
  }
  return { equipmentId, screen, select, sources }
}

it('loads authorized choices, searches on the server, previews selection and clears its result', async () => {
  const value = await fixture()
  await value.select()
  await expect.element(value.screen.getByRole('status')).toHaveTextContent('Passed')
  await value.screen.getByRole('button', { name: 'Clear', exact: true }).click()
  await expect.element(value.screen.getByRole('status')).toHaveTextContent('Select an available inspection')
  await value.screen.getByRole('combobox').fill('2026')
  await vi.waitFor(() => expect(state.get.mock.calls.some(([url]) => url.includes('query=2026'))).toBe(true))
})

it('discards old search successes and failures when equipment changes and on unmount', async () => {
  const value = await fixture()
  for (const fail of [false, true]) {
    const pending = deferred()
    state.get.mockImplementationOnce(() => pending.promise)
    value.equipmentId.value += '-next'
    await nextTick()
    const signal = state.get.mock.calls.at(-1)![2].signal as AbortSignal
    value.equipmentId.value += '-current'
    await nextTick()
    expect(signal.aborted).toBe(true)
    if (fail) pending.reject(new Error('old search'))
    else pending.resolve([{ id: 'old', number: 'Old inspection' }])
    await nextTick()
    await expect.element(value.screen.getByRole('alert')).not.toBeInTheDocument()
  }
  const pending = deferred()
  state.get.mockImplementationOnce(() => pending.promise)
  value.equipmentId.value = 'last'
  await nextTick()
  const signal = state.get.mock.calls.at(-1)![2].signal as AbortSignal
  await value.screen.unmount()
  pending.reject(new Error('unmounted'))
  expect(signal.aborted).toBe(true)
})

it('shows safe search errors and retries without restoring stale choices', async () => {
  const value = await fixture()
  state.get.mockRejectedValueOnce(new Error('private provider detail'))
  value.equipmentId.value = 'different'
  await nextTick()
  await expect.element(value.screen.getByRole('alert')).toHaveTextContent('Unable to load available inspections')
  const failedUrl = state.get.mock.lastCall![0]
  await value.screen.getByRole('button', { name: 'Retry search' }).click()
  await vi.waitFor(() => expect(state.get.mock.lastCall![0]).toBe(failedUrl))
  await value.screen.getByRole('button', { name: 'Show options' }).click()
  await expect.element(value.screen.getByRole('option', { name: value.sources[0].number })).toBeVisible()
  await expect.element(value.screen.getByRole('alert')).not.toBeInTheDocument()
})

it('shows a public conflict but sanitizes unexpected preview failures', async () => {
  const value = await fixture()
  const { ApiError } = await import('@ngbplatform/ui')
  for (const failure of [new ApiError({ message: 'Source consumed', status: 409, url: '/api/inspection-sources/source' }), new Error('private provider detail')]) {
    state.get.mockImplementation(async (url: string) => {
      if (url.includes('/source?')) throw failure
      return value.sources
    })
    await value.select()
    await expect.element(value.screen.getByRole('alert')).toHaveTextContent(
      failure instanceof ApiError ? 'Source consumed' : 'Unable to read this inspection')
    await expect.element(value.screen.getByRole('combobox')).toHaveValue(failure instanceof ApiError ? '' : value.sources[0].number)
    if (!(failure instanceof ApiError)) {
      state.get.mockResolvedValueOnce(value.sources[0]).mockResolvedValueOnce({ display: 'Inspection bench' })
      await value.screen.getByRole('button', { name: 'Retry preview' }).click()
      await expect.element(value.screen.getByRole('status')).toHaveTextContent('Passed')
      await expect.element(value.screen.getByRole('alert')).not.toBeInTheDocument()
    }
  }
})

it('ignores a late preview success or failure after clearing and leaving the editor', async () => {
  const value = await fixture()
  for (const fail of [false, true]) {
    const pending = deferred()
    state.get.mockImplementation(async (url: string) => url.includes('/source?') ? pending.promise : value.sources)
    await value.select()
    await value.screen.getByRole('button', { name: 'Clear', exact: true }).click()
    if (fail) pending.reject(new Error('late error'))
    else pending.resolve(value.sources[0])
    await nextTick()
    await expect.element(value.screen.getByRole('status')).toHaveTextContent('Select an available inspection')
    await expect.element(value.screen.getByRole('alert')).not.toBeInTheDocument()
  }
  const pending = deferred()
  state.get.mockImplementation(async (url: string) => url.includes('/source?') ? pending.promise : value.sources)
  await value.select()
  const signal = state.get.mock.calls.findLast(([url]) => url.includes('/source?'))![2].signal as AbortSignal
  await value.screen.unmount()
  pending.resolve(value.sources[0])
  expect(signal.aborted).toBe(true)
})


it('invalidates the final dependent display request on clear and ignores its late success or failure', async () => {
  const value = await fixture()
  for (const fail of [false, true]) {
    const pending = deferred()
    state.get.mockImplementation(async (url: string) => url.includes('/api/catalogs/') ? pending.promise
      : url.includes('/source?') ? value.sources[0] : value.sources)
    await value.select()
    await vi.waitFor(() => expect(state.get.mock.calls.some(([url]) => url.includes('/api/catalogs/'))).toBe(true))
    await value.screen.getByRole('button', { name: 'Clear', exact: true }).click()
    if (fail) pending.reject(new Error('late dependent failure'))
    else pending.resolve({ display: 'Stale equipment' })
    await nextTick()
    await expect.element(value.screen.getByTestId('source-equipment')).toHaveTextContent('Equipment: —')
    await expect.element(value.screen.getByRole('status')).toHaveTextContent('Select an available inspection')
    await expect.element(value.screen.getByRole('alert')).not.toBeInTheDocument()
    state.get.mockClear()
  }
})
