import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import {
  StubDocumentPeriodFilter,
  StubRecycleBinFilter,
} from '../metadata/stubs'
import {
  StubEntityListPageHeader,
  StubRegisterGrid,
} from './stubs'

const gjeMocks = vi.hoisted(() => ({
  getPage: vi.fn(),
  navigateBack: vi.fn(),
}))

vi.mock('../../../../src/ngb/accounting/generalJournalEntryApi', () => ({
  getGeneralJournalEntryPage: gjeMocks.getPage,
}))

vi.mock('../../../../src/ngb/router/backNavigation', () => ({
  navigateBack: gjeMocks.navigateBack,
}))

vi.mock('../../../../src/ngb/metadata/NgbEntityListPageHeader.vue', () => ({
  default: StubEntityListPageHeader,
}))

vi.mock('../../../../src/ngb/metadata/NgbDocumentPeriodFilter.vue', () => ({
  default: StubDocumentPeriodFilter,
}))

vi.mock('../../../../src/ngb/metadata/NgbRecycleBinFilter.vue', () => ({
  default: StubRecycleBinFilter,
}))

vi.mock('../../../../src/ngb/components/register/NgbRegisterGrid.vue', () => ({
  default: StubRegisterGrid,
}))

import NgbGeneralJournalEntryListPage from '../../../../src/ngb/accounting/NgbGeneralJournalEntryListPage.vue'

function createDeferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((nextResolve, nextReject) => {
    resolve = nextResolve
    reject = nextReject
  })

  return { promise, resolve, reject }
}

async function flushUi() {
  await new Promise((resolve) => window.setTimeout(resolve, 40))
}

async function renderPage(initialUrl: string, props: Record<string, unknown> = {}) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/accounting/general-journal-entries',
        component: NgbGeneralJournalEntryListPage,
      },
      {
        path: '/accounting/general-journal-entries/new',
        component: {
          template: '<div data-testid="gje-create-page">Create page</div>',
        },
      },
      {
        path: '/accounting/general-journal-entries/:id',
        component: {
          template: '<div data-testid="gje-edit-page">Edit page</div>',
        },
      },
      {
        path: '/dashboard',
        component: {
          template: '<div>Dashboard</div>',
        },
      },
    ],
  })

  await router.push(initialUrl)
  await router.isReady()

  const view = await render(NgbGeneralJournalEntryListPage, {
    props: {
      backTarget: '/dashboard',
      ...props,
    },
    global: {
      plugins: [router],
    },
  })

  await flushUi()

  return {
    router,
    view,
  }
}

beforeEach(() => {
  vi.clearAllMocks()

  gjeMocks.getPage.mockResolvedValue({
    offset: 50,
    limit: 50,
    total: 2,
    items: [
      {
        id: 'gje-1',
        dateUtc: '2026-04-15T00:00:00Z',
        number: 'JE-001',
        display: null,
        documentStatus: 2,
        isMarkedForDeletion: false,
        journalType: 1,
        source: 2,
        approvalState: 3,
        memo: null,
        autoReverse: false,
      },
      {
        id: 'gje-2',
        dateUtc: '2026-04-18T00:00:00Z',
        number: 'JE-002',
        display: 'Adjustment Entry',
        documentStatus: 1,
        isMarkedForDeletion: true,
        journalType: 3,
        source: 1,
        approvalState: 4,
        memo: 'Accrual cleanup',
        autoReverse: false,
      },
      {
        id: 'gje-3',
        dateUtc: null,
        number: null,
        display: null,
        documentStatus: 1,
        isMarkedForDeletion: false,
        journalType: 1,
        source: 1,
        approvalState: 1,
        memo: undefined,
        autoReverse: false,
      },
      {
        id: 'gje-4',
        dateUtc: 'not-a-date',
        number: 'JE-004',
        display: null,
        documentStatus: 3,
        isMarkedForDeletion: false,
        journalType: 1,
        source: 1,
        approvalState: 1,
        memo: 'Invalid date boundary',
        autoReverse: false,
      },
    ],
  })
})

test('loads journal entries from route filters, formats row labels, and opens entries on row activation', async () => {
  await page.viewport(1280, 900)

  const { router, view } = await renderPage('/accounting/general-journal-entries?offset=50&periodFrom=2026-03&periodTo=2026-04&trash=deleted')

  expect(gjeMocks.getPage).toHaveBeenCalledWith({
    offset: 50,
    limit: 50,
    dateFrom: '2026-03-01',
    dateTo: '2026-04-30',
    trash: 'deleted',
  })

  await expect.element(view.getByText('title:Journal Entries')).toBeVisible()
  await expect.element(view.getByText('from:2026-03')).toBeVisible()
  await expect.element(view.getByText('to:2026-04')).toBeVisible()
  await expect.element(view.getByText('storage:ngb:accounting:gje:list:/accounting/general-journal-entries')).toBeVisible()
  const standardRow = view.getByRole('button', { name: /display=JE-001/ })
  await expect.element(standardRow).toHaveTextContent('journalType=Standard')
  await expect.element(standardRow).toHaveTextContent('approvalState=Approved')
  await expect.element(standardRow).toHaveTextContent('source=System')
  await expect.element(standardRow).toHaveTextContent('memo=—')
  const adjustmentRow = view.getByRole('button', { name: /display=Adjustment Entry/ })
  await expect.element(adjustmentRow).toHaveTextContent('journalType=Adjusting')
  await expect.element(adjustmentRow).toHaveTextContent('approvalState=Rejected')
  await expect.element(adjustmentRow).toHaveTextContent('source=Manual')
  await expect.element(view.getByText(/display=gje-3/)).toBeVisible()
  await expect.element(view.getByText(/dateUtc=—/)).toBeVisible()
  await expect.element(view.getByText(/display=JE-004/)).toBeVisible()
  await expect.element(view.getByText(/dateUtc=not-a-date/)).toBeVisible()

  await view.getByRole('button', { name: /display=JE-001/ }).click()
  await flushUi()
  expect(router.currentRoute.value.fullPath).toBe('/accounting/general-journal-entries/gje-1')
})

test('updates month and trash filters, pages through results, refreshes, and backs out correctly', async () => {
  await page.viewport(1280, 900)

  const { router, view } = await renderPage('/accounting/general-journal-entries?offset=1&limit=1')

  await view.getByRole('button', { name: 'Set from month' }).click()
  await view.getByRole('button', { name: 'Set to month' }).click()
  await view.getByTestId('stub-recycle-bin-filter').click()
  await flushUi()

  expect(router.currentRoute.value.query.periodFrom).toBe('2026-03')
  expect(router.currentRoute.value.query.periodTo).toBe('2026-04')
  expect(router.currentRoute.value.query.trash).toBe('deleted')
  expect(router.currentRoute.value.query.offset).toBe('0')
  expect(gjeMocks.getPage).toHaveBeenLastCalledWith({
    offset: 0,
    limit: 1,
    dateFrom: '2026-03-01',
    dateTo: '2026-04-30',
    trash: 'deleted',
  })

  await view.getByRole('button', { name: 'Header next' }).click()
  await flushUi()
  expect(router.currentRoute.value.query.offset).toBe('1')
  expect(gjeMocks.getPage).toHaveBeenLastCalledWith({
    offset: 1,
    limit: 1,
    dateFrom: '2026-03-01',
    dateTo: '2026-04-30',
    trash: 'deleted',
  })

  await view.getByRole('button', { name: 'Header prev' }).click()
  await view.getByRole('button', { name: 'Header refresh' }).click()
  await view.getByRole('button', { name: 'Header back' }).click()

  expect(gjeMocks.navigateBack).toHaveBeenCalledTimes(1)
  expect(gjeMocks.navigateBack.mock.calls[0]?.[2]).toBe('/dashboard')
  expect(gjeMocks.getPage).toHaveBeenCalledTimes(7)
})

test('navigates to the creation route from the list header', async () => {
  await page.viewport(1280, 900)

  const { router, view } = await renderPage('/accounting/general-journal-entries')

  await view.getByRole('button', { name: 'Header create' }).click()
  await flushUi()

  expect(router.currentRoute.value.fullPath).toBe('/accounting/general-journal-entries/new')
})

test('ignores stale journal-entry pages when overlapping route changes resolve out of order', async () => {
  await page.viewport(1280, 900)

  const first = createDeferred<{
    offset: number
    limit: number
    total: number
    items: Array<Record<string, unknown>>
  }>()
  const second = createDeferred<{
    offset: number
    limit: number
    total: number
    items: Array<Record<string, unknown>>
  }>()

  gjeMocks.getPage.mockImplementation(async (args: { offset: number }) => {
    if (args.offset === 0) return await first.promise
    return await second.promise
  })

  const { router, view } = await renderPage('/accounting/general-journal-entries?offset=0')

  const secondNavigation = router.push('/accounting/general-journal-entries?offset=50')
  await flushUi()

  second.resolve({
    offset: 50,
    limit: 50,
    total: 1,
    items: [
      {
        id: 'gje-2',
        dateUtc: '2026-04-18T00:00:00Z',
        number: 'JE-050',
        display: 'Fifty',
        documentStatus: 2,
        isMarkedForDeletion: false,
        journalType: 1,
        source: 2,
        approvalState: 3,
        memo: null,
        autoReverse: false,
      },
    ],
  })
  await secondNavigation
  await flushUi()

  await expect.element(view.getByText(/display=Fifty/)).toBeVisible()
  expect(document.body.textContent).not.toContain('display=JE-001')

  first.resolve({
    offset: 0,
    limit: 50,
    total: 1,
    items: [
      {
        id: 'gje-1',
        dateUtc: '2026-04-15T00:00:00Z',
        number: 'JE-001',
        display: null,
        documentStatus: 2,
        isMarkedForDeletion: false,
        journalType: 1,
        source: 2,
        approvalState: 3,
        memo: null,
        autoReverse: false,
      },
    ],
  })
  await flushUi()

  await expect.element(view.getByText(/display=Fifty/)).toBeVisible()
  expect(document.body.textContent).not.toContain('display=JE-001')
  expect(router.currentRoute.value.query.offset).toBe('50')
})

test('shows loading and API errors while preserving null-safe header values', async () => {
  await page.viewport(1280, 900)

  const pending = createDeferred<never>()
  gjeMocks.getPage.mockReturnValue(pending.promise)

  const { view } = await renderPage('/accounting/general-journal-entries', {
    backTarget: null,
    storageKey: ' custom-journal-list ',
  })

  await expect.element(view.getByText('Loading…')).toBeVisible()
  await expect.element(view.getByText('storage:custom-journal-list')).toBeVisible()

  pending.reject(new Error('Journal service unavailable'))
  await expect.element(view.getByText('Journal service unavailable')).toBeVisible()
  expect(document.body.textContent).not.toContain('Loading…')

  await view.getByRole('button', { name: 'Header back' }).click()
  expect(gjeMocks.navigateBack.mock.calls[0]?.[2]).toBe('/')
})

test('ignores a stale journal-entry failure after a newer route load succeeds', async () => {
  await page.viewport(1280, 900)

  const stale = createDeferred<never>()
  gjeMocks.getPage.mockImplementation(async (args: { offset: number }) => {
    if (args.offset === 0) return await stale.promise
    return {
      offset: 50,
      limit: 50,
      total: 0,
      items: [],
    }
  })

  const { router, view } = await renderPage('/accounting/general-journal-entries?offset=0')
  await router.push('/accounting/general-journal-entries?offset=50')
  await flushUi()

  stale.reject(new Error('obsolete failure'))
  await flushUi()

  expect(document.body.textContent).not.toContain('obsolete failure')
  expect(document.body.textContent).not.toContain('Loading…')
  expect(router.currentRoute.value.query.offset).toBe('50')
  await expect.element(view.getByTestId('journal-entry-list-page')).toBeVisible()
})
