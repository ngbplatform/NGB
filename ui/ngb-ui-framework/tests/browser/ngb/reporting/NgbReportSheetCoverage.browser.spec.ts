import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, nextTick, ref } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import { configureNgbReporting } from '../../../../src/ngb/reporting/config'
import NgbReportSheet from '../../../../src/ngb/reporting/NgbReportSheet.vue'
import type { ReportDisplayRow } from '../../../../src/ngb/reporting/groupTree'
import { ReportRowKind, type ReportSheetDto, type ReportSheetRowDto } from '../../../../src/ngb/reporting/types'

const emptySheet: ReportSheetDto = {
  columns: [],
  rows: [],
}

function detailSheet(display = 'Only row'): ReportSheetDto {
  return {
    columns: [{ code: 'value', title: 'Value', dataType: 'string' }],
    rows: [{
      rowKind: ReportRowKind.Detail,
      cells: [{ display, value: display }],
    }],
  }
}

function lookupStoreStub() {
  return {
    searchCatalog: async () => [],
    searchCoa: async () => [],
    searchDocuments: async () => [],
    ensureCatalogLabels: async () => undefined,
    ensureCoaLabels: async () => undefined,
    ensureAnyDocumentLabels: async () => undefined,
    labelForCatalog: (_catalogType: string, id: string) => id,
    labelForCoa: (id: string) => id,
    labelForAnyDocument: (_documentTypes: string[], id: string) => id,
  }
}

configureNgbReporting({
  useLookupStore: lookupStoreStub,
  resolveCellActionUrl: action => action?.kind === 'coverage-target' ? '/target' : null,
})

async function renderHarness(component: object) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { render: () => h('div') } },
      { path: '/target', component: { render: () => h('div', { 'data-testid': 'coverage-target' }, 'Target') } },
    ],
  })

  await router.push('/')
  await router.isReady()

  const view = await render(component, {
    global: { plugins: [router] },
  })

  return { router, view }
}

function withoutIntersectionObserver() {
  const previous = globalThis.IntersectionObserver
  Object.defineProperty(globalThis, 'IntersectionObserver', {
    configurable: true,
    value: undefined,
  })

  return () => Object.defineProperty(globalThis, 'IntersectionObserver', {
    configurable: true,
    value: previous,
  })
}

function withoutResizeObserver() {
  const previous = globalThis.ResizeObserver
  Object.defineProperty(globalThis, 'ResizeObserver', { configurable: true, value: undefined })
  return () => Object.defineProperty(globalThis, 'ResizeObserver', { configurable: true, value: previous })
}

test('renders without ResizeObserver support', async () => {
  const restore = withoutResizeObserver()
  try {
    const { view } = await renderHarness(defineComponent({
      setup: () => () => h(NgbReportSheet, { sheet: detailSheet('No resize observer') }),
    }))
    await expect.element(view.getByText('No resize observer', { exact: true })).toBeVisible()
  } finally {
    restore()
  }
})

test('renders loading, default empty, custom empty, and a loaded sheet', async () => {
  const restoreObserver = withoutIntersectionObserver()

  try {
    const Harness = defineComponent({
      setup() {
        return () => h('div', [
          h(NgbReportSheet, { sheet: null, loading: true }),
          h(NgbReportSheet, { sheet: emptySheet }),
          h(NgbReportSheet, {
            sheet: emptySheet,
            emptyTitle: 'Nothing matched',
            emptyMessage: 'Broaden the report filters.',
          }),
          h(NgbReportSheet, { sheet: detailSheet('Loaded while refreshing'), loading: true }),
        ])
      },
    })

    await renderHarness(Harness)

    expect(document.body.textContent).toContain('Running report…')
    expect(document.body.textContent).toContain('No rows for this layout')
    expect(document.body.textContent).toContain('Adjust filters, grouping, or measures and run the report again.')
    expect(document.body.textContent).toContain('Nothing matched')
    expect(document.body.textContent).toContain('Broaden the report filters.')
    expect(document.body.textContent).toContain('Loaded while refreshing')
    expect(document.querySelectorAll('[data-testid="report-sheet-loading"]')).toHaveLength(1)
    expect(document.querySelectorAll('[data-testid="report-sheet-empty"]')).toHaveLength(2)
    expect(document.querySelectorAll('[data-testid="report-sheet-table"]')).toHaveLength(1)
  } finally {
    restoreObserver()
  }
})

test('formats every supported cell shape and styles all non-pivot row kinds', async () => {
  const rows: ReportSheetRowDto[] = [
    {
      rowKind: ReportRowKind.Group,
      groupKey: 'group-a',
      outlineLevel: 2,
      cells: [
        { value: 1234.5, valueType: ' DECIMAL ' },
        { value: '2,345.6', valueType: 'DOUBLE' },
        { value: '', display: 'blank numeric value', valueType: 'float' },
        { value: Number.POSITIVE_INFINITY, display: '7.5', valueType: 'single' },
        { value: { amount: 1 }, display: 'not numeric', valueType: 'decimal' },
        { value: null, display: ' ' },
        { value: 'plain string', display: null },
        { value: 42, display: '' },
        { value: true, display: null },
        { value: { nested: 'object' }, display: null },
      ],
    },
    { rowKind: ReportRowKind.Detail, cells: [{ display: 'Detail row' }] },
    { rowKind: ReportRowKind.Subtotal, cells: [{ display: 'Subtotal row' }] },
    { rowKind: ReportRowKind.Total, cells: [{ display: 'Total row' }] },
  ]
  const sheet: ReportSheetDto = {
    columns: Array.from({ length: 10 }, (_, index) => ({
      code: `column-${index}`,
      title: `Column ${index}`,
      dataType: 'string',
    })),
    rows,
  }

  const Harness = defineComponent({
    setup: () => () => h(NgbReportSheet, { sheet }),
  })

  await renderHarness(Harness)

  const text = document.body.textContent ?? ''
  expect(text).toContain(new Intl.NumberFormat(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(1234.5))
  expect(text).toContain(new Intl.NumberFormat(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(2345.6))
  expect(text).toContain('blank numeric value')
  expect(text).toContain(new Intl.NumberFormat(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(7.5))
  expect(text).toContain('not numeric')
  expect(text).toContain('plain string')
  expect(text).toContain('42')
  expect(text).toContain('true')
  expect(text).toContain('{"nested":"object"}')
  expect(text).toContain('Group')
  expect(text).toContain('Subtotal')
  expect(text).toContain('Total')

  const renderedRows = [...document.querySelectorAll('tbody tr')]
  expect(renderedRows[0]?.className).toContain('var(--ngb-row-hover)')
  expect(renderedRows[1]?.className).toContain('bg-ngb-card')
  expect(renderedRows[2]?.className).toContain('rgba(11,60,93,.06)')
  expect(renderedRows[3]?.className).toContain('rgba(11,60,93,.10)')
  expect(document.querySelector('tbody td div')?.getAttribute('style')).toContain('padding-left: 32px')
})

test('renders grouped headers, pivot totals, and every grouped row kind', async () => {
  const groupedRows: ReportSheetRowDto[] = [
    { rowKind: ReportRowKind.Group, groupKey: 'g', cells: [{ display: 'Grouped' }, { display: '1' }] },
    { rowKind: ReportRowKind.Detail, cells: [{ display: 'Detail' }, { display: '2' }] },
    {
      rowKind: ReportRowKind.Subtotal,
      cells: [
        { display: 'Subtotal action', action: { kind: 'coverage-target' } },
        { display: '3' },
      ],
    },
    { rowKind: ReportRowKind.Total, cells: [{ display: 'Grand total' }, { display: '6' }] },
  ]
  const groupedSheet: ReportSheetDto = {
    columns: [
      { code: 'axis', title: 'Axis', dataType: 'string' },
      { code: 'total', title: 'Total', dataType: 'number', semanticRole: 'pivot-total' },
    ],
    headerRows: [
      {
        rowKind: ReportRowKind.Header,
        groupKey: 'top',
        cells: [
          { display: 'Axis', rowSpan: 2 },
          { display: 'Measures' },
        ],
      },
      {
        rowKind: ReportRowKind.Header,
        cells: [
          { display: 'Value' },
          { display: 'Total' },
        ],
      },
    ],
    rows: groupedRows,
  }
  const groupedWithoutTotal: ReportSheetDto = {
    columns: [{ code: 'axis', title: 'Axis', dataType: 'string' }],
    headerRows: [{ rowKind: ReportRowKind.Header, cells: [{ display: 'Axis' }] }],
    rows: [{ rowKind: ReportRowKind.Detail, cells: [{ display: 'No pivot total' }] }],
  }

  const Harness = defineComponent({
    setup: () => () => h('div', [
      h(NgbReportSheet, { sheet: groupedSheet }),
      h(NgbReportSheet, { sheet: groupedWithoutTotal }),
    ]),
  })

  await renderHarness(Harness)

  const reportTables = [...document.querySelectorAll('[data-testid="report-sheet-table"]')]
  const firstRows = [...reportTables[0]!.querySelectorAll('tbody tr')]
  expect(firstRows[0]?.className).toContain('bg-ngb-card font-medium')
  expect(firstRows[1]?.className).toContain('bg-ngb-card')
  expect(firstRows[2]?.className).toContain('rgba(11,60,93,.04)')
  expect(firstRows[3]?.className).toContain('rgba(11,60,93,.08)')
  expect(reportTables[0]!.querySelector('thead tr:first-child th:first-child')?.className).toContain('border-r-2')
  expect(reportTables[0]!.querySelector('thead tr:last-child th:last-child')?.className).toContain('border-l-2')
  expect(reportTables[0]!.querySelector('tbody tr:first-child td:first-child')?.className).toContain('border-r-2')
  expect(reportTables[0]!.querySelector('tbody tr:first-child td:last-child')?.className).toContain('border-l-2')
  expect(reportTables[1]!.textContent).toContain('No pivot total')
})

test('covers loading-more and end-of-list count boundaries and noun pluralization', async () => {
  const Harness = defineComponent({
    setup: () => () => h('div', [
      h(NgbReportSheet, {
        sheet: detailSheet('Loading case'),
        loadingMore: true,
        loadedCount: -1,
        rowNoun: ' ',
      }),
      h(NgbReportSheet, {
        sheet: detailSheet('Singular case'),
        canLoadMore: true,
        loadedCount: 1.9,
        rowNoun: 'item',
      }),
      h(NgbReportSheet, {
        sheet: detailSheet('Already plural case'),
        canLoadMore: true,
        loadedCount: 2,
        rowNoun: 'status',
      }),
      h(NgbReportSheet, {
        sheet: detailSheet('Total case'),
        showEndOfList: true,
        loadedCount: 2,
        totalCount: 3.8,
        rowNoun: 'city',
      }),
      h(NgbReportSheet, {
        sheet: detailSheet('Loaded case'),
        showEndOfList: true,
        loadedCount: 2,
        totalCount: 1,
        rowNoun: 'person',
      }),
      h(NgbReportSheet, {
        sheet: detailSheet('Invalid total case'),
        showEndOfList: true,
        loadedCount: null,
        totalCount: Number.POSITIVE_INFINITY,
        rowNoun: null,
      }),
    ]),
  })

  await renderHarness(Harness)

  const text = document.body.textContent ?? ''
  expect(text).toContain('Loading more rows…')
  expect(text).toContain('Loaded 1 item. Scroll to continue loading.')
  expect(text).toContain('Loaded 2 status. Scroll to continue loading.')
  expect(text).toContain('Loaded 3 cities. End of list.')
  expect(text).toContain('Loaded 2 persons. End of list.')
  expect(text).toContain('Loaded 1 row. End of list.')
})

test('does not navigate when an action becomes unavailable between render and activation', async () => {
  let actionAvailable = true
  configureNgbReporting({
    useLookupStore: lookupStoreStub,
    resolveCellActionUrl: action => action?.kind === 'coverage-target' && actionAvailable ? '/target' : null,
  })

  try {
    const sheet = detailSheet('Transient action')
    sheet.rows[0]!.cells[0]!.action = { kind: 'coverage-target' }
    const Harness = defineComponent({
      setup: () => () => h(NgbReportSheet, { sheet }),
    })
    const { router, view } = await renderHarness(Harness)

    await expect.element(view.getByRole('button', { name: 'Transient action' })).toBeVisible()
    actionAvailable = false
    await view.getByRole('button', { name: 'Transient action' }).click()

    expect(router.currentRoute.value.fullPath).toBe('/')
  } finally {
    configureNgbReporting({
      useLookupStore: lookupStoreStub,
      resolveCellActionUrl: action => action?.kind === 'coverage-target' ? '/target' : null,
    })
  }
})

test('keeps load-more disabled while loading and safely ignores scroll restoration without rows', async () => {
  const restoreObserver = withoutIntersectionObserver()
  const reportSheetRef = ref<InstanceType<typeof NgbReportSheet> | null>(null)
  const loadMoreCount = ref(0)
  const loading = ref(false)
  const sheet = ref<ReportSheetDto>(detailSheet('Guarded row'))

  const Harness = defineComponent({
    setup() {
      return () => h('div', [
        h('button', {
          type: 'button',
          onClick: () => { loading.value = true },
        }, 'Start loading'),
        h('button', {
          type: 'button',
          onClick: () => { sheet.value = emptySheet },
        }, 'Clear rows'),
        h('button', {
          type: 'button',
          onClick: () => reportSheetRef.value?.restoreScrollTop(-12.8),
        }, 'Restore without rows'),
        h('output', { 'data-testid': 'load-more-count' }, String(loadMoreCount.value)),
        h(NgbReportSheet, {
          ref: reportSheetRef,
          sheet: sheet.value,
          loading: loading.value,
          canLoadMore: true,
          onLoadMore: () => { loadMoreCount.value += 1 },
        }),
      ])
    },
  })

  try {
    const { view } = await renderHarness(Harness)
    await expect.element(view.getByTestId('load-more-count')).toHaveTextContent('0')
    await view.getByRole('button', { name: 'Start loading' }).click()
    await nextTick()
    await view.getByRole('button', { name: 'Load more' }).click()
    await expect.element(view.getByTestId('load-more-count')).toHaveTextContent('0')

    await view.getByRole('button', { name: 'Clear rows' }).click()
    await view.getByRole('button', { name: 'Restore without rows' }).click()
    await expect.element(view.getByTestId('report-sheet-loading')).toBeVisible()
  } finally {
    restoreObserver()
  }
})

test('restores the visible row anchor after rows are prepended and ignores removed anchors', async () => {
  const restoreResize = withoutResizeObserver()
  const restoreIntersection = withoutIntersectionObserver()
  const handle = ref<InstanceType<typeof NgbReportSheet> | null>(null)
  const sheet = ref<ReportSheetDto>({ ...detailSheet(), rows: Array.from({ length: 300 }, (_, i) => ({
    rowKind: ReportRowKind.Detail, viewKey: `row-${i}`, cells: [{ display: `Row ${i}` }],
  })) })
  try {
    await renderHarness(defineComponent({ setup: () => () => h('div', { style: 'display:flex;height:300px' }, [
      h(NgbReportSheet, { ref: handle, sheet: sheet.value }),
    ]) }))
    handle.value!.restoreScrollTop(502)
    // Browsers may quantize CSS scroll positions to fractional device pixels.
    const initialScrollTop = handle.value!.getScrollTop()
    expect(Math.abs(initialScrollTop - 502)).toBeLessThan(1)
    const anchor = handle.value!.captureAnchor()!
    expect(anchor).toEqual({ key: 'row-10', offset: 490 - initialScrollTop })
    expect(handle.value!.prefixHeight(2)).toBe(98)
    expect(handle.value!.prefixHeight(-1)).toBe(0)
    sheet.value = { ...sheet.value, rows: [{ rowKind: ReportRowKind.Detail, viewKey: 'inserted', cells: [{ display: 'Inserted row' }] } as ReportDisplayRow, ...sheet.value.rows] }
    await nextTick()
    handle.value!.restoreAnchor(anchor)
    const restoredScrollTop = handle.value!.getScrollTop()
    expect(Math.abs(restoredScrollTop - Math.floor(initialScrollTop + 49))).toBeLessThan(1)
    const restoredAnchor = handle.value!.captureAnchor()!
    expect(restoredAnchor.key).toBe(anchor.key)
    // Allow one pixel for integer normalization and one for browser quantization.
    expect(Math.abs(restoredAnchor.offset - anchor.offset)).toBeLessThan(2)
    handle.value!.restoreAnchor({ key: 'missing', offset: 0 })
    expect(handle.value!.getScrollTop()).toBe(restoredScrollTop)
    sheet.value = emptySheet
    await nextTick()
    expect(handle.value!.captureAnchor()).toBeNull()
    handle.value!.restoreAnchor(anchor)
    expect(handle.value!.prefixHeight(10)).toBe(0)
  } finally { restoreResize(); restoreIntersection() }
})

test('requests previous rows near the top only when both loading states are idle', async () => {
  const restore = withoutIntersectionObserver()
  const loading = ref(false)
  const loadingMore = ref(false)
  const onLoadPrevious = vi.fn()
  try {
    const { view } = await renderHarness(defineComponent({ setup: () => () => h(NgbReportSheet, {
      sheet: detailSheet(), canLoadPrevious: true, loading: loading.value, loadingMore: loadingMore.value, onLoadPrevious,
    }) }))
    const host = view.getByTestId('report-sheet-scroll').element()
    host.dispatchEvent(new Event('scroll'))
    expect(onLoadPrevious).toHaveBeenCalledOnce()
    loadingMore.value = true
    await nextTick()
    host.dispatchEvent(new Event('scroll'))
    loadingMore.value = false
    loading.value = true
    await nextTick()
    host.dispatchEvent(new Event('scroll'))
    expect(onLoadPrevious).toHaveBeenCalledOnce()
    loading.value = false
    await nextTick()
    host.dispatchEvent(new Event('scroll'))
    expect(onLoadPrevious).toHaveBeenCalledTimes(2)
  } finally { restore() }
})

test('deduplicates group sentinel notifications and handles a sheet without a scroll host', async () => {
  let notify!: IntersectionObserverCallback
  const observer = { observe: vi.fn(), disconnect: vi.fn(), unobserve: vi.fn() }
  vi.stubGlobal('IntersectionObserver', class {
    constructor(callback: IntersectionObserverCallback) { notify = callback; return observer }
  })
  const sheet = ref<ReportSheetDto | null>(null)
  const onGroupAction = vi.fn()
  try {
    await renderHarness(defineComponent({ setup: () => () => h(NgbReportSheet, { sheet: sheet.value, onGroupAction }) }))
    expect(observer.observe).not.toHaveBeenCalled()
    sheet.value = { ...detailSheet(), rows: [{ rowKind: ReportRowKind.Detail, cells: [], viewKey: 'control',
      control: { id: 'group', text: 'Load group', action: 'next', autoLoad: true },
    } as ReportDisplayRow] }
    await nextTick()
    const target = document.querySelector('[data-group-next]')!
    const entries = [target, target, document.createElement('div')].map(target => ({ target, isIntersecting: true }) as IntersectionObserverEntry)
    notify(entries, observer as unknown as IntersectionObserver)
    expect(onGroupAction).toHaveBeenCalledExactlyOnceWith('group', 'next')
    expect((target as HTMLElement).style.paddingLeft).toBe('16px')
  } finally { vi.unstubAllGlobals() }
})
