import { defineComponent, h } from 'vue'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createMemoryHistory, createRouter, RouterView } from 'vue-router'

import type {
  ReconciliationPageDefinition,
  ReconciliationReport,
  ReconciliationRow,
} from '../../../../src/features/reconciliation/types'

const mocks = vi.hoisted(() => ({
  convertMonth: true,
  load: vi.fn(),
}))

vi.mock('@ngbplatform/ui', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@ngbplatform/ui')>()
  const Badge = defineComponent({
    props: { tone: { type: String, default: 'neutral' } },
    setup(props, { slots }) {
      return () => h('span', { 'data-testid': `badge-${props.tone}` }, slots.default?.())
    },
  })
  const Icon = defineComponent({
    props: { name: { type: String, required: true } },
    setup(props) {
      return () => h('span', { 'data-testid': `icon-${props.name}` })
    },
  })
  const PageHeader = defineComponent({
    props: { title: { type: String, required: true } },
    emits: ['back'],
    setup(props, { emit, slots }) {
      return () => h('header', [
        h('h1', props.title),
        h('button', { type: 'button', onClick: () => emit('back') }, 'Header back'),
        slots.secondary?.(),
        slots.actions?.(),
      ])
    },
  })
  const PeriodFilter = defineComponent({
    props: {
      fromMonth: { type: String, required: true },
      toMonth: { type: String, required: true },
      disabled: { type: Boolean, default: false },
    },
    emits: ['update:fromMonth', 'update:toMonth'],
    setup(props, { emit }) {
      return () => h('section', { 'data-testid': 'period-filter' }, [
        h('input', {
          'aria-label': 'From month',
          value: props.fromMonth,
          disabled: props.disabled,
          onInput: (event: Event) => emit('update:fromMonth', (event.target as HTMLInputElement).value),
        }),
        h('input', {
          'aria-label': 'To month',
          value: props.toMonth,
          disabled: props.disabled,
          onInput: (event: Event) => emit('update:toMonth', (event.target as HTMLInputElement).value),
        }),
      ])
    },
  })

  return {
    ...actual,
    NgbBadge: Badge,
    NgbDocumentPeriodFilter: PeriodFilter,
    NgbIcon: Icon,
    NgbPageHeader: PageHeader,
    monthValueToDateOnly: (value: string) => mocks.convertMonth ? `${value}-01` : null,
    relativeMonthValue: (offset: number) => offset < 0 ? '2026-07' : '2026-08',
  }
})

import ReconciliationPage from '../../../../src/features/reconciliation/ReconciliationPage.vue'

const AppRoot = defineComponent({
  setup() {
    return () => h(RouterView)
  },
})

function row(overrides: Partial<ReconciliationRow>): ReconciliationRow {
  return {
    key: 'row',
    rowKind: 'Matched',
    hasDiff: false,
    primaryLabel: 'Party',
    secondaryLabel: 'Property',
    tertiaryLabel: null,
    ledgerNet: 100,
    openItemsNet: 100,
    diff: 0,
    openTarget: null,
    ...overrides,
  }
}

function report(rows: ReconciliationRow[] = []): ReconciliationReport {
  return {
    totalLedgerNet: 440.126,
    totalOpenItemsNet: 400.004,
    totalDiff: 40.122,
    rowCount: rows.length,
    mismatchRowCount: rows.filter((entry) => entry.rowKind !== 'Matched').length,
    filteredRowCount: rows.length,
    glOnlyRowCount: rows.filter((entry) => entry.rowKind === 'GlOnly').length,
    openItemsOnlyRowCount: rows.filter((entry) => entry.rowKind === 'OpenItemsOnly').length,
    rows,
    offset: 0,
    limit: 100,
    hasMore: false,
    nextCursor: null,
  }
}

function definition(overrides: Partial<ReconciliationPageDefinition> = {}): ReconciliationPageDefinition {
  return {
    title: 'Receivables reconciliation',
    ledgerNetLabel: 'AR Net',
    ledgerNetSummaryDescription: (mode) => `Ledger ${mode.toLowerCase()}`,
    diffSummaryDescription: 'Ledger less open items',
    groupedByDescription: 'Grouped by party, property, and lease.',
    rowsDescription: 'One row per reconciliation key.',
    noRowsMessage: 'No reconciliation rows.',
    primaryColumnTitle: 'Party',
    secondaryColumnTitle: 'Property',
    tertiaryColumnTitle: 'Lease',
    balanceNotes: ['Balance note'],
    movementNotes: ['Movement note'],
    describeMode: ({ mode, fromMonth, toMonth }) => `${mode}: ${fromMonth} to ${toMonth}`,
    explainRow: (value) => `Explanation for ${value.key}`,
    load: mocks.load,
    ...overrides,
  }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

async function flushUi() {
  await new Promise((resolvePromise) => window.setTimeout(resolvePromise, 50))
}

async function renderPage(url: string, pageDefinition = definition()) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/reconciliation',
        component: ReconciliationPage,
        props: { definition: pageDefinition },
      },
      {
        path: '/open-items/:id',
        component: defineComponent({
          setup() {
            return () => h('div', { 'data-testid': 'open-target' }, 'Open items target')
          },
        }),
      },
    ],
  })
  await router.push(url)
  await router.isReady()
  const view = await render(AppRoot, { global: { plugins: [router] } })
  await flushUi()
  return { router, view }
}

function setMonth(label: 'From month' | 'To month', value: string) {
  const input = document.querySelector(`input[aria-label="${label}"]`)
  if (!(input instanceof HTMLInputElement)) throw new Error(`${label} input not found.`)
  input.value = value
  input.dispatchEvent(new Event('input', { bubbles: true }))
}

beforeEach(() => {
  mocks.convertMonth = true
  mocks.load.mockReset()
  mocks.load.mockResolvedValue(report())
})

test('loads, formats, sorts, labels, and opens a complete reconciliation report', async () => {
  const pending = deferred<ReconciliationReport>()
  mocks.load.mockImplementationOnce(async () => await pending.promise)
  const rows = [
    row({ key: 'matched', rowKind: 'Matched', primaryLabel: 'Zulu', secondaryLabel: 'P2' }),
    row({ key: 'open-only', rowKind: 'OpenItemsOnly', hasDiff: true, primaryLabel: 'Alpha', diff: -20, ledgerNet: 0, openItemsNet: 20 }),
    row({ key: 'gl-only', rowKind: 'GlOnly', hasDiff: true, primaryLabel: 'Alpha', secondaryLabel: 'P1', diff: 20, openItemsNet: 0 }),
    row({ key: 'mismatch-b', rowKind: 'Mismatch', hasDiff: true, primaryLabel: 'Beta', diff: 10 }),
    row({ key: 'mismatch-a2', rowKind: 'Mismatch', hasDiff: true, primaryLabel: 'Alpha', secondaryLabel: 'P2', tertiaryLabel: 'Lease B', diff: 10 }),
    row({
      key: 'mismatch-a1b',
      rowKind: 'Mismatch',
      hasDiff: true,
      primaryLabel: 'Alpha',
      secondaryLabel: 'P1',
      tertiaryLabel: 'Lease B',
      diff: 10,
    }),
    row({
      key: 'mismatch-a1-null',
      rowKind: 'Mismatch',
      hasDiff: true,
      primaryLabel: 'Alpha',
      secondaryLabel: 'P1',
      tertiaryLabel: null,
      diff: 10,
    }),
    row({
      key: 'mismatch-a1a',
      rowKind: 'Mismatch',
      hasDiff: true,
      primaryLabel: 'Alpha',
      secondaryLabel: 'P1',
      tertiaryLabel: 'Lease A',
      diff: 10,
      openTarget: '/open-items/mismatch-a1a',
    }),
    row({ key: 'unknown', rowKind: 'Unknown' as never, primaryLabel: 'Omega' }),
  ]

  const renderPromise = renderPage('/reconciliation?fromMonth=2026-01&toMonth=2026-03&mode=Balance')
  await flushUi()
  await expect.element(document.body.querySelector('[data-testid="reconciliation-page"]') as HTMLElement).toBeVisible()
  await expect.element(document.body).toHaveTextContent('Loading reconciliation…')
  pending.resolve(report(rows))
  const { router, view } = await renderPromise

  expect(mocks.load).toHaveBeenCalledWith({
    fromMonthInclusive: '2026-01-01',
    toMonthInclusive: '2026-03-01',
    mode: 'Balance',
    status: 'All',
    offset: 0,
    limit: 100,
    cursor: null,
  }, { signal: expect.any(AbortSignal) })
  await expect.element(view.getByText('440.13')).toBeVisible()
  await expect.element(view.getByText('40.12')).toBeVisible()
  const table = view.getByTestId('reconciliation-table-wrap')
  await expect.element(table.getByText('GL only', { exact: true })).toBeVisible()
  await expect.element(table.getByText('Open Items only', { exact: true })).toBeVisible()
  await expect.element(table.getByText('Unknown', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Explanation for mismatch-a1a')).toBeVisible()

  const rowOrder = Array.from(document.querySelectorAll('tbody tr')).map((entry) => entry.textContent ?? '')
  expect(rowOrder.findIndex((text) => text.includes('mismatch-a1a'))).toBeLessThan(rowOrder.findIndex((text) => text.includes('Zulu')))

  await view.getByTitle('Open Items').click()
  await expect.element(view.getByTestId('open-target')).toBeVisible()
  expect(router.currentRoute.value.fullPath).toBe('/open-items/mismatch-a1a')
})

test('updates mode and every status filter through clean route query state', async () => {
  const rows = [
    row({ key: 'matched', rowKind: 'Matched' }),
    row({ key: 'mismatch', rowKind: 'Mismatch', hasDiff: true, diff: 5 }),
    row({ key: 'gl', rowKind: 'GlOnly', hasDiff: true, diff: 3 }),
    row({ key: 'open', rowKind: 'OpenItemsOnly', hasDiff: true, diff: 2 }),
  ]
  mocks.load.mockImplementation(async (request) => {
    const filtered = request.status === 'Matched'
      ? rows.filter((entry) => entry.rowKind === 'Matched')
      : request.status === 'Mismatch'
        ? rows.filter((entry) => entry.rowKind !== 'Matched')
        : request.status === 'GlOnly'
          ? rows.filter((entry) => entry.rowKind === 'GlOnly')
          : request.status === 'OpenItemsOnly'
            ? rows.filter((entry) => entry.rowKind === 'OpenItemsOnly')
            : rows
    return {
      ...report(filtered),
      rowCount: rows.length,
      mismatchRowCount: 3,
      filteredRowCount: filtered.length,
      glOnlyRowCount: 1,
      openItemsOnlyRowCount: 1,
      offset: request.offset,
      limit: request.limit,
    }
  })
  const { router, view } = await renderPage('/reconciliation?fromMonth=2026-01&toMonth=2026-03&mode=Balance')

  await view.getByRole('button', { name: 'Movement' }).click()
  await flushUi()
  expect(router.currentRoute.value.query.mode).toBe('Movement')
  expect(mocks.load).toHaveBeenLastCalledWith(
    expect.objectContaining({ mode: 'Movement' }),
    { signal: expect.any(AbortSignal) },
  )
  await expect.element(view.getByText('Movement note')).toBeVisible()
  await expect.element(view.getByText('Operational register movement')).toBeVisible()

  await view.getByRole('button', { name: 'Matched (1)' }).click()
  await flushUi()
  expect(router.currentRoute.value.query.status).toBe('matched')
  await expect.element(view.getByText('1 / 4 rows shown')).toBeVisible()

  await view.getByRole('button', { name: 'Mismatches (3)' }).click()
  await flushUi()
  expect(router.currentRoute.value.query.status).toBe('mismatch')
  await expect.element(view.getByText('3 / 4 rows shown')).toBeVisible()

  await view.getByRole('button', { name: 'GL only (1)' }).click()
  await flushUi()
  expect(router.currentRoute.value.query.status).toBe('gl-only')
  await view.getByRole('button', { name: 'Open Items only (1)' }).click()
  await flushUi()
  expect(router.currentRoute.value.query.status).toBe('open-items-only')
  await view.getByRole('button', { name: 'All (4)' }).click()
  await flushUi()
  expect(router.currentRoute.value.query.status).toBeUndefined()
  expect(router.currentRoute.value.query.rows).toBeUndefined()
})

test('rejects an invalid month range before I/O and recovers after the range is corrected', async () => {
  const { router, view } = await renderPage('/reconciliation?fromMonth=2026-05&toMonth=2026-04')
  await expect.element(view.getByText('From month must be earlier than or equal to To month.')).toBeVisible()
  expect(mocks.load).not.toHaveBeenCalled()

  setMonth('To month', '2026-06')
  await flushUi()
  expect(router.currentRoute.value.query.toMonth).toBe('2026-06')
  expect(mocks.load).toHaveBeenCalledOnce()
  expect(document.body.textContent ?? '').not.toContain('From month must be earlier')

  setMonth('From month', '2026-02')
  await flushUi()
  expect(router.currentRoute.value.query.fromMonth).toBe('2026-02')
  expect(mocks.load).toHaveBeenCalledTimes(2)
})

test('shows Error and non-Error load failures and recovers through refresh', async () => {
  mocks.load
    .mockRejectedValueOnce(new Error('Reconciliation unavailable'))
    .mockRejectedValueOnce('Gateway offline')
    .mockResolvedValueOnce(report([row({ key: 'recovered' })]))
  const { view } = await renderPage('/reconciliation')
  await expect.element(view.getByText('Reconciliation unavailable')).toBeVisible()

  await view.getByTitle('Refresh').click()
  await flushUi()
  await expect.element(view.getByText('Gateway offline')).toBeVisible()
  await view.getByTitle('Refresh').click()
  await flushUi()
  await expect.element(view.getByText('1 / 1 rows shown')).toBeVisible()
})

test('uses relative month and date fallbacks and renders an empty two-column definition', async () => {
  mocks.convertMonth = false
  mocks.load.mockResolvedValue(report())
  const { view } = await renderPage('/reconciliation?fromMonth=invalid&toMonth=invalid', definition({
    tertiaryColumnTitle: null,
  }))

  expect(mocks.load).toHaveBeenCalledWith({
    fromMonthInclusive: '2026-07-01',
    toMonthInclusive: '2026-08-01',
    mode: 'Balance',
    status: 'All',
    offset: 0,
    limit: 100,
    cursor: null,
  }, { signal: expect.any(AbortSignal) })
  await expect.element(view.getByText('No reconciliation rows.')).toBeVisible()
  await expect.element(view.getByText('Balance note')).toBeVisible()
  await expect.element(view.getByText('Largest visible diff')).toBeVisible()

  mocks.load.mockResolvedValueOnce(report([row({ key: 'without-tertiary' })]))
  await view.getByTitle('Refresh').click()
  await flushUi()
  expect(document.querySelector('th:nth-child(4)')?.textContent).toBe('Why')
})

test('navigates back through the page header', async () => {
  const { router, view } = await renderPage('/reconciliation')
  const backSpy = vi.spyOn(router, 'back').mockImplementation(() => {})
  await view.getByRole('button', { name: 'Header back' }).click()
  expect(backSpy).toHaveBeenCalledOnce()
})

test('bounds large reconciliation results to one DOM page', async () => {
  const rows = Array.from({ length: 101 }, (_, index) => row({
    key: `row-${index + 1}`,
    primaryLabel: `Party ${String(index + 1).padStart(3, '0')}`,
  }))
  mocks.load.mockImplementation(async (request) => {
    const pageRows = rows.slice(request.offset, request.offset + request.limit)
    return {
      ...report(pageRows),
      rowCount: rows.length,
      filteredRowCount: rows.length,
      offset: request.offset,
      limit: request.limit,
      hasMore: request.offset + request.limit < rows.length,
      nextCursor: request.offset === 0 ? 'page-2' : null,
    }
  })

  const { view } = await renderPage('/reconciliation')

  await expect.element(view.getByText('Rows 1–100 of 101')).toBeVisible()
  expect(document.querySelectorAll('[data-testid="reconciliation-table-wrap"] tbody tr')).toHaveLength(100)
  expect(document.body.textContent).toContain('Party 001')
  expect(document.body.textContent).not.toContain('Party 101')

  await view.getByRole('button', { name: 'Next' }).click()
  await expect.element(view.getByText('Rows 101–101 of 101')).toBeVisible()
  expect(document.querySelectorAll('[data-testid="reconciliation-table-wrap"] tbody tr')).toHaveLength(1)
  expect(document.body.textContent).toContain('Party 101')
  expect(document.body.textContent).not.toContain('Party 001')

  await view.getByRole('button', { name: 'Previous' }).click()
  await expect.element(view.getByText('Rows 1–100 of 101')).toBeVisible()
})
