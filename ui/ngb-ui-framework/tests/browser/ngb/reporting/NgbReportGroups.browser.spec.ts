import { expect, test, vi } from 'vitest'
import { page } from 'vitest/browser'
import { render } from 'vitest-browser-vue'
import { computed, defineComponent, h, ref } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import NgbReportSheet from '../../../../src/ngb/reporting/NgbReportSheet.vue'
import { ReportGroupTree } from '../../../../src/ngb/reporting/groupTree'
import { ReportRowKind, type ReportExecutionResponseDto, type ReportSheetRowDto } from '../../../../src/ngb/reporting/types'

const row = (label: string, value = 1): ReportSheetRowDto => ({ rowKind: ReportRowKind.Detail, cells: [{ display: label }, { value, valueType: 'decimal' }] })
function response(rows: ReportSheetRowDto[], cursor: string | null = null): ReportExecutionResponseDto {
  return { sheet: { columns: [{ code: 'label', title: 'Account', dataType: 'string', width: 280 }, { code: 'amount', title: 'Amount', dataType: 'decimal', width: 160 }], rows }, offset: 0, limit: 100, hasMore: !!cursor, nextCursor: cursor }
}
async function renderTree(loader: Parameters<ReportGroupTree['reset']>[1]) {
  const version = ref(0)
  const tree = new ReportGroupTree(() => { version.value++ })
  tree.reset(response([
    { ...row('Assets', 300), rowKind: ReportRowKind.Group, childrenPath: [1] },
    { ...row('Liabilities', 20), rowKind: ReportRowKind.Group, childrenPath: [2] },
    { ...row('Grand total', 320), rowKind: ReportRowKind.Total },
  ]).sheet, loader)
  const sheet = computed(() => { void version.value; return tree.sheet })
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: { render: () => null } }] })
  await router.push('/')
  const view = render(defineComponent({ setup: () => () => h('div', { style: 'display:flex;height:500px;width:700px;min-height:0' }, [
    h(NgbReportSheet, { sheet: sheet.value, onGroupAction: (id, action) => tree.act(id, action) }),
  ]) }), { global: { plugins: [router] } })
  return { view, tree, router }
}

test('expands in place with accessible controls and loads only the scrolled branch in one virtual table', async () => {
  await page.viewport(1000, 750)
  const loader = vi.fn(async (path: unknown[], cursor: string | null) => {
    const start = Number(cursor ?? 0)
    return response(Array.from({ length: 100 }, (_, i) => row(`Account ${start + i}`)), start < 200 ? String(start + 100) : null)
  })
  const { view, tree, router } = await renderTree(loader)
  expect(loader).not.toHaveBeenCalled()
  await view.getByRole('button', { name: 'Expand group Assets' }).click()
  await expect.element(view.getByRole('button', { name: 'Collapse group Assets' })).toHaveAttribute('aria-expanded', 'true')
  await expect.element(view.getByText('Account 0', { exact: true })).toBeVisible()
  expect(loader).toHaveBeenCalledTimes(1)
  expect(router.currentRoute.value.fullPath).toBe('/')
  const host = view.getByTestId('report-sheet-scroll').element() as HTMLElement
  for (let call = 2; call <= 3; call++) {
    host.scrollTop = host.scrollHeight
    host.dispatchEvent(new Event('scroll'))
    await expect.poll(() => loader.mock.calls.length).toBe(call)
    await expect.poll(() => tree.sheet!.rows.filter(row => !row.control).length).toBe(call * 100 + 3)
  }
  expect(loader.mock.calls.map(call => call.slice(0, 2))).toEqual([[[1], null], [[1], '100'], [[1], '200']])
  expect(document.querySelectorAll('table')).toHaveLength(1)
  expect(document.querySelectorAll('tbody tr').length).toBeLessThan(100)
  expect(tree.sheet!.rows.find(row => row.cells[0]?.display === 'Grand total')?.cells[1]?.value).toBe(320)
  host.scrollTop = 0
  host.dispatchEvent(new Event('scroll'))
  await view.getByRole('button', { name: 'Collapse group Assets' }).click()
  await expect.element(view.getByText('Account 0', { exact: true })).not.toBeInTheDocument()
  await view.getByRole('button', { name: 'Expand group Assets' }).click()
  await expect.element(view.getByText('Account 0', { exact: true })).toBeVisible()
  expect(loader).toHaveBeenCalledTimes(3)
})

test('keeps group errors inline and retries without losing the parent or other groups', async () => {
  const loader = vi.fn().mockRejectedValueOnce(new Error('Group temporarily unavailable')).mockResolvedValueOnce(response([row('Cash')]))
  const { view } = await renderTree(loader)
  await view.getByRole('button', { name: 'Expand group Assets' }).click()
  await expect.element(view.getByRole('alert')).toHaveTextContent('Group temporarily unavailable')
  await expect.element(view.getByRole('button', { name: 'Expand group Liabilities' })).toBeVisible()
  await view.getByRole('button', { name: 'Retry' }).click()
  await expect.element(view.getByText('Cash', { exact: true })).toBeVisible()
  await expect.element(view.getByRole('alert')).not.toBeInTheDocument()
  expect(loader).toHaveBeenCalledTimes(2)
})
