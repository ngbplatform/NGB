import { defineComponent, h, nextTick, ref } from 'vue'
import { render } from 'vitest-browser-vue'
import { expect, it, vi } from 'vitest'
import Summary from '../../src/LineReferenceSummary.vue'

function fixture() {
  const rows = ref([{ item: 'one', quantity: 1 }])
  const documentKey = ref('draft-one')
  const loadItem = vi.fn(async (_id: string, _signal: AbortSignal) => ({ preparationMinutes: 20 }))
  const screen = render(defineComponent({
    setup: () => () => h(Summary, { rows: rows.value, documentKey: documentKey.value, loadItem }),
  }))
  return { screen, rows, documentKey, loadItem }
}

it('deduplicates reference reads, recalculates quantities locally and hydrates a replacement document', async () => {
  const state = fixture()
  await expect.element(state.screen.getByRole('status')).toHaveTextContent('20')
  state.rows.value = [{ item: 'one', quantity: 2 }, { item: 'one', quantity: 3 }]
  await expect.element(state.screen.getByRole('status')).toHaveTextContent('100')
  expect(state.loadItem).toHaveBeenCalledTimes(1)
  state.documentKey.value = 'draft-two'
  state.loadItem.mockResolvedValueOnce({ preparationMinutes: 10 })
  await expect.element(state.screen.getByRole('status')).toHaveTextContent('50')
  state.rows.value = [{ item: 'one', quantity: Number.MAX_SAFE_INTEGER }]
  await expect.element(state.screen.getByRole('status')).toHaveTextContent('—')
  state.rows.value = []
  await expect.element(state.screen.getByRole('status')).toHaveTextContent('0')
})

it.each(['failure', 'invalid'] as const)('clears unavailable reference totals on %s and allows retry', async failure => {
  const state = fixture()
  await expect.element(state.screen.getByRole('status')).toHaveTextContent('20')
  if (failure === 'failure') {
    state.loadItem.mockRejectedValueOnce(new Error('Private reference error'))
  } else {
    state.loadItem.mockResolvedValueOnce({ preparationMinutes: -1 })
  }
  state.rows.value = [{ item: 'two', quantity: 2 }]
  await expect.element(state.screen.getByRole('alert')).toHaveTextContent('Preparation details are unavailable')
  await expect.element(state.screen.getByRole('status')).toHaveTextContent('—')
  await state.screen.getByRole('button', { name: 'Retry details' }).click()
  await expect.element(state.screen.getByRole('status')).toHaveTextContent('40')
  await expect.element(state.screen.getByRole('alert')).not.toBeInTheDocument()
})

it.each(['resolve', 'reject'] as const)('ignores a stale reference %s after replacing rows', async outcome => {
  const state = fixture()
  await expect.element(state.screen.getByRole('status')).toHaveTextContent('20')
  let resolve!: (value: { preparationMinutes: number }) => void
  let reject!: (reason: unknown) => void
  state.loadItem.mockImplementationOnce(() => new Promise((accept, fail) => {
    resolve = accept
    reject = fail
  }))
  state.rows.value = [{ item: 'old', quantity: 1 }]
  await nextTick()
  const signal = state.loadItem.mock.calls[1][1]
  state.rows.value = [{ item: 'new', quantity: 3 }]
  await expect.element(state.screen.getByRole('status')).toHaveTextContent('60')
  expect(signal.aborted).toBe(true)
  if (outcome === 'resolve') {
    resolve({ preparationMinutes: 99 })
  } else {
    reject(new Error('Old error'))
  }
  await nextTick()
  await expect.element(state.screen.getByRole('status')).toHaveTextContent('60')
  await expect.element(state.screen.getByRole('alert')).not.toBeInTheDocument()
  state.screen.unmount()
  expect(state.loadItem.mock.calls[2][1].aborted).toBe(true)
})
