import { defineComponent, h, nextTick, ref } from 'vue'
import { render } from 'vitest-browser-vue'
import { afterEach, expect, it, vi } from 'vitest'
import { userEvent } from 'vitest/browser'
import type { QuotedLine } from '../../src/QuotedLinesEditor.vue'

vi.hoisted(() => vi.stubGlobal('__NGB_RUNTIME_CONFIG__', {
  VITE_KEYCLOAK_URL: 'http://localhost:18081',
  VITE_KEYCLOAK_REALM: 'component-tests',
  VITE_KEYCLOAK_CLIENT_ID: 'component-tests',
}))

afterEach(() => {
  document.body.innerHTML = ''
})

function line(id: string): QuotedLine {
  return { id, item: '', quantity: 1, unitPriceMinor: null, pricingDate: null, totalMinor: null }
}

async function fixture(initial: QuotedLine[] = [], loadItem?: (id: string, signal: AbortSignal) => Promise<{ preparationMinutes: number }>) {
  const rows = ref(initial)
  const date = ref('2026-11-01')
  const readonly = ref(false)
  const documentKey = ref('draft-one')
  const quote = vi.fn(async (_item: string, pricingDate: string, _signal: AbortSignal) => ({ unitPriceMinor: 1250, pricingDate }))
  const { default: Editor } = await import('../../src/QuotedLinesEditor.vue')
  const screen = render(defineComponent({
    setup: () => () => h(Editor, {
      modelValue: rows.value,
      businessDate: date.value,
      documentKey: documentKey.value,
      readonly: readonly.value,
      items: [{ value: null, label: 'Choose an item' }, { value: 'inspection', label: 'Inspection' }, { value: 'service', label: 'Service' }],
      quote,
      loadItem,
      'onUpdate:modelValue': value => { rows.value = value },
    }),
  }))
  async function select(label: string, index = 0) {
    await screen.getByRole('button', { name: 'Item', exact: true }).nth(index).click()
    await screen.getByRole('option', { name: label, exact: true }).click()
  }
  return { screen, rows, date, readonly, documentKey, quote, select }
}

it('defaults a new line and quotes before saving, then recalculates quantity and business date', async () => {
  const state = await fixture()
  await state.screen.getByRole('button', { name: 'Add line' }).click()
  expect(state.rows.value[0].quantity).toBe(1)
  await state.select('Inspection')
  await expect.poll(() => state.rows.value[0].totalMinor).toBe(1250)
  expect(state.rows.value[0].pricingDate).toBe('2026-11-01')
  await state.screen.getByRole('spinbutton', { name: 'Quantity' }).fill('3')
  await expect.poll(() => state.rows.value[0].totalMinor).toBe(3750)
  state.quote.mockResolvedValue({ unitPriceMinor: 1500, pricingDate: '2026-11-02' })
  state.date.value = '2026-11-02'
  await expect.poll(() => state.rows.value[0].totalMinor).toBe(4500)
  expect(state.rows.value[0].pricingDate).toBe('2026-11-02')
  await state.select('Choose an item')
  expect(state.rows.value[0].unitPriceMinor).toBeNull()
  expect(state.rows.value[0].totalMinor).toBeNull()
})

it('reopens saved rows, hydrates all reference details and updates the header after adding and removing lines', async () => {
  const stored = { ...line('saved'), item: 'inspection', unitPriceMinor: 900, pricingDate: '2026-10-10', totalMinor: 900 }
  const loadItem = vi.fn(async (id: string) => ({ preparationMinutes: id === 'inspection' ? 20 : 10 }))
  const state = await fixture([stored], loadItem)
  await expect.element(state.screen.getByText('Preparation (minutes): 20')).toBeVisible()
  expect(state.quote).not.toHaveBeenCalled()
  await state.screen.getByRole('button', { name: 'Add line' }).click()
  await expect.element(state.screen.getByText('Preparation (minutes): —')).toBeVisible()
  await state.select('Service', 1)
  await expect.element(state.screen.getByText('Preparation (minutes): 30')).toBeVisible()
  await state.screen.getByRole('spinbutton', { name: 'Quantity' }).nth(1).fill('3')
  await expect.element(state.screen.getByText('Preparation (minutes): 50')).toBeVisible()
  expect(state.rows.value[0]).toEqual(stored)
  expect(state.quote).toHaveBeenCalledTimes(1)
  await state.screen.getByRole('button', { name: 'Delete row 2' }).click()
  await expect.element(state.screen.getByText('Preparation (minutes): 20')).toBeVisible()
  expect(state.rows.value).toEqual([stored])
})

it('hydrates stored values without repricing and keeps readonly documents unchanged', async () => {
  const stored = { ...line('saved'), item: 'inspection', unitPriceMinor: 900, pricingDate: '2026-10-10', totalMinor: 900 }
  const state = await fixture([stored])
  expect(state.quote).not.toHaveBeenCalled()
  state.readonly.value = true
  state.date.value = '2026-11-03'
  await nextTick()
  await expect.element(state.screen.getByRole('button', { name: 'Item', exact: true })).toBeDisabled()
  await expect.element(state.screen.getByRole('spinbutton', { name: 'Quantity' })).toHaveAttribute('readonly')
  expect(state.rows.value).toEqual([stored])
  expect(state.quote).not.toHaveBeenCalled()
})

it('ignores a superseded response even when the transport resolves after cancellation', async () => {
  const state = await fixture([line('one')])
  let complete!: (value: { unitPriceMinor: number, pricingDate: string }) => void
  state.quote.mockImplementationOnce(() => new Promise(resolve => { complete = resolve }))
  await state.select('Inspection')
  const signal = state.quote.mock.calls[0][2]
  await state.select('Service')
  await expect.poll(() => state.rows.value[0].totalMinor).toBe(1250)
  expect(signal.aborted).toBe(true)
  complete({ unitPriceMinor: 9999, pricingDate: '2026-01-01' })
  await nextTick()
  expect(state.rows.value[0].totalMinor).toBe(1250)
  expect(state.rows.value[0].item).toBe('service')
})

it('keeps asynchronous results attached to row identity across reordering and removal', async () => {
  const state = await fixture([line('one'), line('two')])
  let complete!: (value: { unitPriceMinor: number, pricingDate: string }) => void
  state.quote.mockImplementationOnce(() => new Promise(resolve => { complete = resolve }))
  await state.select('Inspection')
  await state.screen.getByRole('button', { name: 'Reorder row 1' }).click()
  await userEvent.keyboard('{Alt>}{ArrowDown}{/Alt}')
  expect(state.rows.value.map(row => row.id)).toEqual(['two', 'one'])
  complete({ unitPriceMinor: 700, pricingDate: '2026-11-01' })
  await expect.poll(() => state.rows.value[1].totalMinor).toBe(700)
  expect(state.rows.value[0].totalMinor).toBeNull()
  state.quote.mockImplementationOnce(() => new Promise(resolve => { complete = resolve }))
  await state.select('Service', 1)
  const signal = state.quote.mock.calls[1][2]
  await state.screen.getByRole('button', { name: 'Delete row 2' }).click()
  expect(signal.aborted).toBe(true)
  complete({ unitPriceMinor: 999, pricingDate: '2026-11-01' })
  await nextTick()
  expect(state.rows.value.map(row => row.id)).toEqual(['two'])
})

it('keeps the selected item and date when retrying a failed quote', async () => {
  const state = await fixture([line('one')])
  state.quote.mockRejectedValueOnce(new Error('No approved value for this date.'))
  await state.select('Inspection')
  await expect.element(state.screen.getByRole('status')).toHaveTextContent('Price unavailable')
  expect(state.rows.value[0].totalMinor).toBeNull()
  state.quote.mockResolvedValueOnce({ unitPriceMinor: 0, pricingDate: '2026-11-01' })
  await state.screen.getByRole('button', { name: 'Retry price' }).click()
  expect(state.quote.mock.calls[1].slice(0, 2)).toEqual(['inspection', '2026-11-01'])
  await expect.element(state.screen.getByRole('status')).toHaveTextContent('cannot be represented')
  await state.screen.getByRole('button', { name: 'Retry price' }).click()
  await expect.poll(() => state.rows.value[0].totalMinor).toBe(1250)
  await state.screen.getByRole('spinbutton', { name: 'Quantity' }).fill('-1')
  await expect.element(state.screen.getByRole('status')).toHaveTextContent('positive whole quantity')
  expect(state.rows.value[0].quantity).toBe(1)
})

it('cancels work when the document is replaced or becomes readonly', async () => {
  const state = await fixture([line('one')])
  state.quote.mockImplementation(() => new Promise(() => {}))
  await state.select('Inspection')
  state.documentKey.value = 'draft-two'
  await nextTick()
  expect(state.quote.mock.calls[0][2].aborted).toBe(true)
  await state.select('Service')
  state.readonly.value = true
  await nextTick()
  expect(state.quote.mock.calls[1][2].aborted).toBe(true)
  state.readonly.value = false
  await nextTick()
  await state.select('Inspection')
  expect(state.quote).toHaveBeenCalledTimes(3)
})

it('keeps an unpriced quantity, reports transport failures and ignores failures from cancelled work', async () => {
  const state = await fixture([line('one')])
  await state.screen.getByRole('spinbutton', { name: 'Quantity' }).fill('2')
  expect(state.rows.value[0].quantity).toBe(2)
  expect(state.rows.value[0].totalMinor).toBeNull()
  state.date.value = ''
  await nextTick()
  await state.select('Inspection')
  expect(state.quote).not.toHaveBeenCalled()
  state.quote.mockRejectedValueOnce('offline')
  state.date.value = '2026-11-02'
  await expect.element(state.screen.getByRole('status')).toHaveTextContent('Price unavailable')

  let reject!: (error: Error) => void
  state.quote.mockImplementationOnce(() => new Promise((_resolve, fail) => { reject = fail }))
  await state.select('Service')
  await state.select('Inspection')
  await expect.poll(() => state.rows.value[0].totalMinor).toBe(2500)
  reject(new Error('Obsolete error'))
  await nextTick()
  await expect.element(state.screen.getByRole('status')).not.toBeInTheDocument()
})

it('aborts requests when parent hydration removes a row or the editor is unmounted', async () => {
  const state = await fixture([line('one'), line('two')])
  state.quote.mockImplementation(() => new Promise(() => {}))
  await state.select('Inspection')
  await state.select('Service', 1)
  state.rows.value = [state.rows.value[1]]
  await nextTick()
  expect(state.quote.mock.calls[0][2].aborted).toBe(true)
  expect(state.quote.mock.calls[1][2].aborted).toBe(false)
  state.screen.unmount()
  expect(state.quote.mock.calls[1][2].aborted).toBe(true)
})

it('rejects fractional prices and unsafe amount multiplication', async () => {
  const state = await fixture([line('one')])
  state.quote.mockResolvedValueOnce({ unitPriceMinor: 1.5, pricingDate: '2026-11-01' })
  await state.select('Inspection')
  await expect.element(state.screen.getByRole('status')).toHaveTextContent('cannot be represented')
  await state.screen.getByRole('spinbutton', { name: 'Quantity' }).fill('2')
  state.quote.mockResolvedValueOnce({ unitPriceMinor: Number.MAX_SAFE_INTEGER, pricingDate: '2026-11-01' })
  await state.select('Service')
  await expect.element(state.screen.getByRole('status')).toHaveTextContent('cannot be represented')
  await state.select('Inspection')
  await expect.poll(() => state.rows.value[0].totalMinor).toBe(2500)
  await state.screen.getByRole('spinbutton', { name: 'Quantity' }).fill(String(Number.MAX_SAFE_INTEGER))
  await expect.element(state.screen.getByRole('status')).toHaveTextContent('supported amount range')
  expect(state.rows.value[0].quantity).toBe(2)
  await state.screen.getByRole('spinbutton', { name: 'Quantity' }).fill('1.5')
  await expect.element(state.screen.getByRole('status')).toHaveTextContent('positive whole quantity')
  expect(state.rows.value[0].quantity).toBe(2)
})
