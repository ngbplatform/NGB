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

async function renderPage(initialUrl: string) {
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
  await expect.element(view.getByText(/display=JE-001/)).toBeVisible()
  await expect.element(view.getByText(/journalType=Standard/)).toBeVisible()
  await expect.element(view.getByText(/approvalState=Approved/)).toBeVisible()
  await expect.element(view.getByText(/source=System/)).toBeVisible()
  await expect.element(view.getByText(/memo=—/)).toBeVisible()
  await expect.element(view.getByText(/display=Adjustment Entry/)).toBeVisible()
  await expect.element(view.getByText(/journalType=Adjusting/)).toBeVisible()
  await expect.element(view.getByText(/approvalState=Rejected/)).toBeVisible()
  await expect.element(view.getByText(/source=Manual/)).toBeVisible()

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
