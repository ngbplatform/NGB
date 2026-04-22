import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import NgbReportSheet from '../../../../src/ngb/reporting/NgbReportSheet.vue'
import { ReportRowKind, type ReportSheetDto } from '../../../../src/ngb/reporting/types'

function makeSheet(rows: number): ReportSheetDto {
  return {
    columns: [
      { code: 'property', title: 'Property', dataType: 'string' },
      { code: 'manager', title: 'Portfolio Manager', dataType: 'string' },
      { code: 'units', title: 'Units', dataType: 'number' },
      { code: 'occupied', title: 'Occupied', dataType: 'number' },
      { code: 'vacant', title: 'Vacant', dataType: 'number' },
      { code: 'total', title: 'Total', dataType: 'number', semanticRole: 'pivot-total' },
    ],
    headerRows: [
      {
        rowKind: ReportRowKind.Header,
        cells: [
          { display: 'Property', value: 'Property', rowSpan: 2 },
          { display: 'Portfolio Manager', value: 'Portfolio Manager', rowSpan: 2 },
          { display: 'Occupancy Snapshot', value: 'Occupancy Snapshot', colSpan: 4 },
        ],
      },
      {
        rowKind: ReportRowKind.Header,
        cells: [
          { display: 'Units', value: 'Units' },
          { display: 'Occupied', value: 'Occupied' },
          { display: 'Vacant', value: 'Vacant' },
          { display: 'Total', value: 'Total' },
        ],
      },
    ],
    rows: Array.from({ length: rows }, (_, index) => ({
      rowKind: ReportRowKind.Detail,
      cells: [
        { display: `Riverfront Tower ${index + 1}`, value: `Riverfront Tower ${index + 1}`, valueType: 'string' },
        { display: 'Alex Carter', value: 'Alex Carter', valueType: 'string' },
        { display: '24', value: 24, valueType: 'decimal' },
        { display: '19', value: 19, valueType: 'decimal' },
        { display: '5', value: 5, valueType: 'decimal' },
        { display: '79.17', value: 79.17, valueType: 'decimal' },
      ],
    })),
    meta: {
      title: 'Occupancy Summary',
      isPivot: true,
      hasColumnGroups: true,
    },
  }
}

function makeStressSheet(rows: number, measureCount = 10): ReportSheetDto {
  const measureColumns = Array.from({ length: measureCount }, (_, index) => ({
    code: `measure_${index + 1}`,
    title: `Month ${index + 1}`,
    dataType: 'number',
    semanticRole: index === measureCount - 1 ? 'pivot-total' : undefined,
  }))

  return {
    columns: [
      { code: 'property', title: 'Property', dataType: 'string' },
      { code: 'manager', title: 'Manager', dataType: 'string' },
      ...measureColumns,
    ],
    rows: Array.from({ length: rows }, (_, index) => ({
      rowKind: ReportRowKind.Detail,
      cells: [
        { display: `Stress Property ${index + 1}`, value: `Stress Property ${index + 1}`, valueType: 'string' },
        { display: `Manager ${((index % 6) + 1).toString().padStart(2, '0')}`, value: `Manager ${index + 1}`, valueType: 'string' },
        ...measureColumns.map((_, measureIndex) => ({
          display: String(100 + index + measureIndex),
          value: 100 + index + measureIndex,
          valueType: 'decimal',
        })),
      ],
    })),
    meta: {
      title: 'Stress Report',
      isPivot: true,
      hasColumnGroups: false,
    },
  }
}

const mobileSheet = makeSheet(12)

const ReportSheetMobileHarness = defineComponent({
  setup() {
    return () => h(
      'div',
      {
        style: 'width: 375px; max-width: 375px; height: 720px; display: flex; min-width: 0; min-height: 0; overflow: hidden;',
      },
      [
        h(
          'div',
          {
            style: 'display: flex; flex: 1 1 auto; min-width: 0; min-height: 0; overflow: hidden;',
          },
          [
            h(NgbReportSheet, {
              sheet: mobileSheet,
              rowNoun: 'property',
            }),
          ],
        ),
      ],
    )
  },
})

const ReportSheetLoadMoreHarness = defineComponent({
  setup() {
    return () => h(
      'div',
      {
        style: 'width: 480px; max-width: 480px; height: 680px; display: flex; flex-direction: column; min-width: 0; min-height: 0; overflow: hidden;',
      },
      [
        h(
          'div',
          {
            style: 'display: flex; flex: 1 1 auto; min-width: 0; min-height: 0; overflow: hidden;',
          },
          [
            h(NgbReportSheet, {
              sheet: makeSheet(6),
              canLoadMore: true,
              loadingMore: false,
              loadedCount: 6,
              totalCount: 18,
              rowNoun: 'property',
            }),
          ],
        ),
      ],
    )
  },
})

const ReportSheetObserverHarness = defineComponent({
  setup() {
    const reportSheetRef = ref<InstanceType<typeof NgbReportSheet> | null>(null)
    const events = ref<string[]>([])

    return () => h(
      'div',
      {
        style: 'width: 480px; max-width: 480px; height: 680px; display: flex; flex-direction: column; min-width: 0; min-height: 0; overflow: hidden;',
      },
      [
        h('button', {
          type: 'button',
          onClick: () => reportSheetRef.value?.restoreScrollTop(180),
        }, 'Restore scroll'),
        h('div', { 'data-testid': 'observer-events' }, events.value.join('|') || 'none'),
        h(
          'div',
          {
            style: 'display: flex; flex: 1 1 auto; min-width: 0; min-height: 0; overflow: hidden;',
          },
          [
            h(NgbReportSheet, {
              ref: reportSheetRef,
              sheet: makeSheet(8),
              canLoadMore: true,
              loadingMore: false,
              loadedCount: 8,
              totalCount: 18,
              rowNoun: 'property',
              onLoadMore: () => {
                events.value = [...events.value, 'load-more']
              },
              onScrollTopChange: (value: number) => {
                events.value = [...events.value, `scroll:${value}`]
              },
            }),
          ],
        ),
      ],
    )
  },
})

const ReportSheetObserverLifecycleHarness = defineComponent({
  setup() {
    const reportSheetRef = ref<InstanceType<typeof NgbReportSheet> | null>(null)
    const events = ref<string[]>([])
    const mounted = ref(true)
    const loadedRows = ref(8)
    const sheet = ref(makeSheet(8))

    function appendRows() {
      loadedRows.value += 4
      sheet.value = makeSheet(loadedRows.value)
    }

    function replaceSheet() {
      const next = makeSheet(loadedRows.value)
      next.meta = {
        ...next.meta,
        title: `Replacement ${loadedRows.value}`,
      }
      sheet.value = next
    }

    return () => h(
      'div',
      {
        style: 'width: 480px; max-width: 480px; height: 680px; display: flex; flex-direction: column; min-width: 0; min-height: 0; overflow: hidden;',
      },
      [
        h('div', { style: 'display: flex; gap: 8px; padding-bottom: 8px;' }, [
          h('button', {
            type: 'button',
            onClick: appendRows,
          }, 'Append rows'),
          h('button', {
            type: 'button',
            onClick: replaceSheet,
          }, 'Replace sheet'),
          h('button', {
            type: 'button',
            onClick: () => reportSheetRef.value?.restoreScrollTop(220),
          }, 'Restore scroll'),
          h('button', {
            type: 'button',
            onClick: () => {
              mounted.value = !mounted.value
            },
          }, mounted.value ? 'Unmount sheet' : 'Mount sheet'),
        ]),
        h('div', { 'data-testid': 'observer-lifecycle-events' }, events.value.join('|') || 'none'),
        h(
          'div',
          {
            style: 'display: flex; flex: 1 1 auto; min-width: 0; min-height: 0; overflow: hidden;',
          },
          [
            mounted.value
              ? h(NgbReportSheet, {
                  ref: reportSheetRef,
                  sheet: sheet.value,
                  canLoadMore: true,
                  loadingMore: false,
                  loadedCount: loadedRows.value,
                  totalCount: 24,
                  rowNoun: 'property',
                  onLoadMore: () => {
                    events.value = [...events.value, 'load-more']
                  },
                })
              : null,
          ],
        ),
      ],
    )
  },
})

const ReportSheetStressHarness = defineComponent({
  setup() {
    const loadedRows = ref(80)

    return () => h(
      'div',
      {
        style: 'width: 680px; max-width: 680px; height: 720px; display: flex; flex-direction: column; min-width: 0; min-height: 0; overflow: hidden;',
      },
      [
        h(
          'div',
          {
            style: 'display: flex; flex: 1 1 auto; min-width: 0; min-height: 0; overflow: hidden;',
          },
          [
            h(NgbReportSheet, {
              sheet: makeStressSheet(loadedRows.value, 12),
              canLoadMore: loadedRows.value < 160,
              loadingMore: false,
              loadedCount: loadedRows.value,
              totalCount: 160,
              rowNoun: 'property',
              onLoadMore: () => {
                loadedRows.value = Math.min(160, loadedRows.value + 40)
              },
            }),
          ],
        ),
      ],
    )
  },
})

function mockIntersectionObserver() {
  const previous = globalThis.IntersectionObserver
  const state = {
    callback: null as IntersectionObserverCallback | null,
    observed: [] as Element[],
    disconnectCount: 0,
  }

  class MockIntersectionObserver implements IntersectionObserver {
    readonly root = null
    readonly rootMargin = '0px 0px 320px 0px'
    readonly thresholds = [0.01]

    constructor(callback: IntersectionObserverCallback) {
      state.callback = callback
    }

    disconnect() {
      state.disconnectCount += 1
    }

    observe(target: Element) {
      state.observed.push(target)
    }

    takeRecords(): IntersectionObserverEntry[] {
      return []
    }

    unobserve() {}
  }

  Object.defineProperty(globalThis, 'IntersectionObserver', {
    configurable: true,
    value: MockIntersectionObserver,
  })

  return {
    state,
    restore() {
      Object.defineProperty(globalThis, 'IntersectionObserver', {
        configurable: true,
        value: previous,
      })
    },
  }
}

function intersectLastObserved(observer: ReturnType<typeof mockIntersectionObserver>) {
  const target = observer.state.observed.at(-1)
  if (!target) return

  observer.state.callback?.([
    {
      isIntersecting: true,
      target,
    } as IntersectionObserverEntry,
  ], {} as IntersectionObserver)
}

async function renderWithRouter(component: object) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/',
        component: {
          template: '<div />',
        },
      },
    ],
  })

  await router.push('/')
  await router.isReady()

  return await render(component, {
    global: {
      plugins: [router],
    },
  })
}

test('keeps a wide pivot report inside its own mobile scroll host', async () => {
  await page.viewport(375, 812)

  const view = await renderWithRouter(ReportSheetMobileHarness)
  const scrollHost = view.getByTestId('report-sheet-scroll')

  await expect.element(scrollHost).toBeVisible()
  await expect.element(view.getByTestId('report-sheet-table')).toBeVisible()

  const metrics = scrollHost.element() as HTMLElement
  const table = view.getByTestId('report-sheet-table').element() as HTMLElement

  expect(table.scrollWidth).toBeGreaterThan(metrics.clientWidth)
  expect(metrics.scrollHeight).toBeGreaterThan(metrics.clientHeight)
  expect(document.documentElement.scrollWidth <= window.innerWidth + 1).toBe(true)
})

test('shows a load-more footer without breaking the report shell contract', async () => {
  await page.viewport(480, 800)

  const view = await renderWithRouter(ReportSheetLoadMoreHarness)

  await expect.element(view.getByText('Loaded 6 properties. Scroll to continue loading.', { exact: true })).toBeVisible()
  await expect.element(view.getByRole('button', { name: 'Load more' })).toBeVisible()
  expect(document.documentElement.scrollWidth <= window.innerWidth + 1).toBe(true)
})

test('emits scroll state, restores scroll position, and requests more rows through the observer contract', async () => {
  await page.viewport(480, 800)
  const observer = mockIntersectionObserver()

  try {
    const view = await renderWithRouter(ReportSheetObserverHarness)
    const scrollHost = view.getByTestId('report-sheet-scroll').element() as HTMLDivElement

    expect(observer.state.observed.length).toBeGreaterThan(0)
    await expect.element(view.getByTestId('observer-events')).toHaveTextContent('scroll:0')

    scrollHost.scrollTop = 96
    scrollHost.dispatchEvent(new Event('scroll'))
    await expect.poll(() => view.getByTestId('observer-events').element().textContent ?? '').toContain(`scroll:${scrollHost.scrollTop}`)

    await view.getByRole('button', { name: 'Restore scroll' }).click()
    expect(scrollHost.scrollTop).toBe(Math.min(180, Math.max(0, scrollHost.scrollHeight - scrollHost.clientHeight)))

    observer.state.callback?.([
      {
        isIntersecting: true,
        target: observer.state.observed[0]!,
      } as IntersectionObserverEntry,
    ], {} as IntersectionObserver)

    await expect.poll(() => view.getByTestId('observer-events').element().textContent ?? '').toContain('load-more')
  } finally {
    observer.restore()
  }
})

test('handles wide appendable report datasets without leaking page overflow', async () => {
  await page.viewport(680, 900)

  const view = await renderWithRouter(ReportSheetStressHarness)
  const scrollHost = view.getByTestId('report-sheet-scroll')
  const table = view.getByTestId('report-sheet-table')

  await expect.element(scrollHost).toBeVisible()
  await expect.element(view.getByText('Loaded 80 properties. Scroll to continue loading.', { exact: true })).toBeVisible()
  expect((table.element() as HTMLElement).scrollWidth).toBeGreaterThan((scrollHost.element() as HTMLElement).clientWidth)
  expect(document.documentElement.scrollWidth <= window.innerWidth + 1).toBe(true)

  await view.getByRole('button', { name: 'Load more' }).click()

  await expect.element(view.getByText('Loaded 120 properties. Scroll to continue loading.', { exact: true })).toBeVisible()
  expect(document.querySelectorAll('tbody tr').length).toBe(120)
  expect(document.body.textContent).toContain('Stress Property 120')
  expect(document.documentElement.scrollWidth <= window.innerWidth + 1).toBe(true)
})

test('suppresses duplicate observer load-more signals until the sheet changes and keeps scroll restoration working after replacement', async () => {
  await page.viewport(480, 800)
  const observer = mockIntersectionObserver()

  try {
    const view = await renderWithRouter(ReportSheetObserverLifecycleHarness)
    const scrollHost = view.getByTestId('report-sheet-scroll').element() as HTMLDivElement

    expect(observer.state.observed.length).toBeGreaterThan(0)

    intersectLastObserved(observer)
    intersectLastObserved(observer)

    await expect.element(view.getByTestId('observer-lifecycle-events')).toHaveTextContent('load-more')

    await view.getByRole('button', { name: 'Append rows' }).click()
    await expect.element(view.getByText('Loaded 12 properties. Scroll to continue loading.', { exact: true })).toBeVisible()

    intersectLastObserved(observer)
    await expect.element(view.getByTestId('observer-lifecycle-events')).toHaveTextContent('load-more|load-more')

    scrollHost.scrollTop = 48
    await view.getByRole('button', { name: 'Replace sheet' }).click()
    await view.getByRole('button', { name: 'Restore scroll' }).click()
    expect(scrollHost.scrollTop).toBe(Math.min(220, Math.max(0, scrollHost.scrollHeight - scrollHost.clientHeight)))

    intersectLastObserved(observer)
    await expect.element(view.getByTestId('observer-lifecycle-events')).toHaveTextContent('load-more|load-more|load-more')
  } finally {
    observer.restore()
  }
})

test('disconnects the load-more observer when the report sheet unmounts', async () => {
  await page.viewport(480, 800)
  const observer = mockIntersectionObserver()

  try {
    const view = await renderWithRouter(ReportSheetObserverLifecycleHarness)

    expect(observer.state.observed.length).toBeGreaterThan(0)
    expect(observer.state.disconnectCount).toBe(0)

    await view.getByRole('button', { name: 'Unmount sheet' }).click()

    await expect.poll(() => observer.state.disconnectCount).toBe(1)
  } finally {
    observer.restore()
  }
})
