import { defineComponent, h, nextTick, ref } from 'vue'
import { render } from 'vitest-browser-vue'
import { afterEach, expect, it, vi } from 'vitest'
import type { DerivedDocument } from '../../src/DerivedLinesEditor.vue'
import '@ngbplatform/ui/styles'

vi.hoisted(() => vi.stubGlobal('__NGB_RUNTIME_CONFIG__', {
  VITE_KEYCLOAK_URL: 'http://localhost:18081',
  VITE_KEYCLOAK_REALM: 'component-tests',
  VITE_KEYCLOAK_CLIENT_ID: 'component-tests',
}))

// Keep the HTTP client and controls real; this bounded component suite has no SSO server.
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

afterEach(() => vi.restoreAllMocks())

function empty(): DerivedDocument {
  return { source: null, customer: null, lines: [], feeMinor: null, totalMinor: null }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((accept, fail) => {
    resolve = accept
    reject = fail
  })
  return { promise, resolve, reject }
}

async function fixture(initial = empty()) {
  const value = ref(initial)
  const date = ref('2026-11-01')
  const readonly = ref(false)
  const documentKey = ref('draft-one')
  const canOpen = ref(true)
  const sources = [{ id: 'one', label: 'Order one' }, { id: 'two', label: 'Order two' }]
  const result = {
    customer: { id: 'customer', label: 'North services' },
    lines: [{ id: 'line', item: { id: 'service', label: 'Inspection', meta: 'S-001' }, amountMinor: 1200 }],
  }
  const loadSource = vi.fn(async (_id: string, _date: string, _signal: AbortSignal) => result)
  const loadFee = vi.fn(async (_lines: typeof result.lines, _date: string, _signal: AbortSignal) => 80)
  const openItem = vi.fn()
  const { default: Editor } = await import('../../src/DerivedLinesEditor.vue')
  const screen = render(defineComponent({
    setup: () => () => h(Editor, {
      modelValue: value.value,
      documentKey: documentKey.value,
      businessDate: date.value,
      readonly: readonly.value,
      eligibleSources: sources,
      canOpenItem: () => canOpen.value,
      loadSource,
      loadFee,
      onOpenItem: openItem,
      'onUpdate:modelValue': next => {
        value.value = next
      },
    }),
  }))
  async function select(name = 'Order one') {
    await screen.getByRole('button', { name: 'Show options' }).first().click()
    await screen.getByRole('option', { name, exact: true }).click()
  }
  return { screen, value, date, readonly, documentKey, canOpen, sources, result, loadSource, loadFee, openItem, select }
}

it('fills readonly charges from an eligible source, opens the item once and clears the entire chain', async () => {
  const state = await fixture()
  await state.select()
  await expect.element(state.screen.getByText('Total (cents): 1280')).toBeVisible()
  await expect.element(state.screen.getByRole('combobox').nth(1)).toHaveValue('Inspection')
  await state.screen.getByRole('button', { name: 'Open', exact: true }).click()
  expect(state.openItem).toHaveBeenCalledExactlyOnceWith(state.result.lines[0].item)
  await expect.element(state.screen.getByRole('button', { name: 'Add row' })).not.toBeInTheDocument()
  await expect.element(state.screen.getByRole('button', { name: /Delete row/ })).not.toBeInTheDocument()
  await state.screen.getByRole('button', { name: 'Clear', exact: true }).click()
  expect(state.value.value).toEqual(empty())
  await expect.element(state.screen.getByText('Total (cents): —')).toBeVisible()
})

it('builds a readonly reference target from its ID with return navigation through the public adapter', async () => {
  const { NgbLookup, configureNgbLookup, createDefaultNgbLookupConfig } = await import('@ngbplatform/ui')
  const { metadataFormBehavior } = await import('../../src/metadata')
  configureNgbLookup(createDefaultNgbLookupConfig())
  const item = { id: 'equipment-identity', label: 'Inspection bench', meta: 'EQ-001' }
  const target = ref<string | null>(null)
  const open = vi.fn(async () => {
    target.value = await metadataFormBehavior.buildLookupTargetUrl!({
      hint: { kind: 'catalog', catalogType: 'example.equipment' },
      value: item,
      routeFullPath: '/documents/example.inspection/draft-one',
    })
  })
  const screen = render(defineComponent({
    setup: () => () => h(NgbLookup, { modelValue: item, items: [], readonly: true, showOpen: true, onOpen: open }),
  }))
  await screen.getByRole('button', { name: 'Open', exact: true }).click()
  await expect.poll(() => target.value).toContain('/catalogs/example.equipment/equipment-identity?back=')
  expect(open).toHaveBeenCalledOnce()
  expect(target.value).not.toContain('EQ-001')
})

it('clears previous charges before a replacement request fails and shows the public HTTP reason', async () => {
  const state = await fixture()
  await state.select()
  await expect.poll(() => state.value.value.totalMinor).toBe(1280)
  const { httpGet } = await import('@ngbplatform/ui')
  const response = deferred<Response>()
  vi.spyOn(globalThis, 'fetch').mockImplementationOnce(() => response.promise)
  state.loadSource.mockImplementationOnce((_id, _date, signal) => httpGet('/example/quote', null, { signal }))
  await state.select('Order two')
  expect(state.value.value).toEqual({ ...empty(), source: state.sources[1] })
  await expect.element(state.screen.getByRole('status')).toHaveTextContent('Calculating')
  response.resolve(new Response(JSON.stringify({
    status: 409,
    detail: 'Complete the outstanding source operation before calculating charges.',
    error: { code: 'example.source.incomplete', kind: 'Conflict' },
  }), { status: 409, headers: { 'Content-Type': 'application/problem+json' } }))
  await expect.element(state.screen.getByRole('alert')).toHaveTextContent('Complete the outstanding source operation')
  expect(state.value.value.lines).toEqual([])
  await state.screen.getByRole('button', { name: 'Retry', exact: true }).click()
  await expect.poll(() => state.value.value.totalMinor).toBe(1280)
})

it.each(['resolve', 'reject'] as const)('ignores a stale source %s after a newer selection succeeds', async outcome => {
  const state = await fixture()
  const old = deferred<typeof state.result>()
  state.loadSource.mockImplementationOnce(() => old.promise)
  await state.select()
  const signal = state.loadSource.mock.calls[0][2]
  await state.select('Order two')
  await expect.poll(() => state.value.value.totalMinor).toBe(1280)
  expect(signal.aborted).toBe(true)
  if (outcome === 'resolve') {
    old.resolve({ ...state.result, lines: [] })
  } else {
    old.reject(new Error('Stale private error'))
  }
  await nextTick()
  expect(state.value.value.source?.id).toBe('two')
  expect(state.value.value.totalMinor).toBe(1280)
  expect(state.loadFee).toHaveBeenCalledTimes(1)
  await expect.element(state.screen.getByRole('alert')).not.toBeInTheDocument()
})

it.each(['clear', 'replace', 'readonly', 'unmount'] as const)('invalidates an in-flight second calculation on %s', async action => {
  const state = await fixture()
  const fee = deferred<number>()
  state.loadFee.mockImplementationOnce(() => fee.promise)
  await state.select()
  await expect.poll(() => state.loadFee.mock.calls.length).toBe(1)
  const signal = state.loadFee.mock.calls[0][2]
  if (action === 'clear') await state.screen.getByRole('button', { name: 'Clear', exact: true }).click()
  if (action === 'replace') {
    state.value.value = empty()
    state.documentKey.value = 'draft-two'
  }
  if (action === 'readonly') state.readonly.value = true
  if (action === 'unmount') state.screen.unmount()
  await nextTick()
  const before = JSON.parse(JSON.stringify(state.value.value))
  expect(signal.aborted).toBe(true)
  fee.resolve(9999)
  await nextTick()
  expect(state.value.value).toEqual(before)
})

it('preserves saved snapshots on reopen, recalculates date edits and hides unauthorized object links', async () => {
  const stored: DerivedDocument = {
    source: { id: 'one', label: 'Order one' },
    customer: { id: 'saved', label: 'Original customer' },
    lines: [{ id: 'saved-line', item: { id: 'old', label: 'Original item' }, amountMinor: 900 }],
    feeMinor: 25,
    totalMinor: 925,
  }
  const state = await fixture(stored)
  expect(state.loadSource).not.toHaveBeenCalled()
  await expect.element(state.screen.getByText('Total (cents): 925')).toBeVisible()
  state.canOpen.value = false
  state.readonly.value = true
  state.date.value = '2026-11-02'
  await nextTick()
  await expect.element(state.screen.getByRole('button', { name: 'Open', exact: true })).not.toBeInTheDocument()
  await expect.element(state.screen.getByRole('combobox').first()).toBeDisabled()
  expect(state.loadSource).not.toHaveBeenCalled()
  state.readonly.value = false
  state.date.value = '2026-11-03'
  await expect.poll(() => state.value.value.totalMinor).toBe(1280)
  state.date.value = ''
  await expect.poll(() => state.value.value.lines.length).toBe(0)
  expect(state.value.value.totalMinor).toBeNull()
})

it.each(['network', 'amount', 'server'] as const)('keeps failed %s calculations empty without exposing diagnostics', async failure => {
  const state = await fixture()
  if (failure === 'network') state.loadSource.mockRejectedValueOnce(new Error('Private provider detail'))
  if (failure === 'amount') state.loadFee.mockResolvedValueOnce(NaN)
  if (failure === 'server') {
    const { ApiError } = await import('@ngbplatform/ui')
    state.loadSource.mockRejectedValueOnce(new ApiError({ status: 500, url: '/example/quote', message: 'Private provider detail' }))
  }
  await state.select()
  await expect.element(state.screen.getByRole('alert')).toHaveTextContent('Unable to calculate')
  expect(state.value.value.lines).toEqual([])
  await expect.element(state.screen.getByText('Private provider detail')).not.toBeInTheDocument()
})
