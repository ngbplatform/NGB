import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import { withBackTarget } from '../../../../src/ngb/router/backNavigation'
import {
  StubBadge,
  StubIcon,
  StubInput,
  StubLookup,
  StubSelect,
} from './stubs'

const linesEditorMocks = vi.hoisted(() => ({
  getAccountContext: vi.fn(),
  lookupConfig: {
    buildCatalogUrl: vi.fn(),
    buildCoaUrl: vi.fn(),
    buildDocumentUrl: vi.fn(),
    loadDocumentItem: vi.fn(),
    loadDocumentItemsByIds: vi.fn(),
  },
  lookupStore: {
    searchCatalog: vi.fn(),
    searchCoa: vi.fn(),
    searchDocuments: vi.fn(),
  },
}))

vi.mock('../../../../src/ngb/lookup/store', () => ({
  useLookupStore: () => linesEditorMocks.lookupStore,
}))

vi.mock('../../../../src/ngb/lookup/config', () => ({
  getConfiguredNgbLookup: () => linesEditorMocks.lookupConfig,
}))

vi.mock('../../../../src/ngb/accounting/generalJournalEntryApi', () => ({
  getGeneralJournalEntryAccountContext: linesEditorMocks.getAccountContext,
}))

vi.mock('../../../../src/ngb/primitives/NgbBadge.vue', () => ({
  default: StubBadge,
}))

vi.mock('../../../../src/ngb/primitives/NgbIcon.vue', () => ({
  default: StubIcon,
}))

vi.mock('../../../../src/ngb/primitives/NgbInput.vue', () => ({
  default: StubInput,
}))

vi.mock('../../../../src/ngb/primitives/NgbLookup.vue', () => ({
  default: StubLookup,
}))

vi.mock('../../../../src/ngb/primitives/NgbSelect.vue', () => ({
  default: StubSelect,
}))

import NgbGeneralJournalEntryLinesEditor from '../../../../src/ngb/accounting/NgbGeneralJournalEntryLinesEditor.vue'

type LookupItem = {
  id: string
  label: string
  meta?: string | null
}

type EditorLine = {
  clientKey: string
  side: number
  account: LookupItem | null
  amount: string
  memo: string
  dimensions: Record<string, LookupItem | null>
}

type AccountContext = {
  accountId: string
  code: string
  name: string
  dimensionRules: Array<{
    dimensionId: string
    dimensionCode: string
    ordinal: number
    isRequired: boolean
    lookup?: {
      kind: 'catalog' | 'document' | 'coa'
      catalogType?: string
      documentTypes?: string[]
    } | null
  }>
}

function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T
}

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
  await new Promise((resolve) => window.setTimeout(resolve, 60))
}

function queryLookupRoot(index: number): HTMLElement {
  const root = document.querySelectorAll('[data-testid="stub-lookup"]')[index]
  if (!(root instanceof HTMLElement)) throw new Error(`Lookup ${index} not found.`)
  return root
}

async function queryLookup(index: number, value: string) {
  const input = queryLookupRoot(index).querySelector('input')
  if (!(input instanceof HTMLInputElement)) throw new Error(`Lookup ${index} input not found.`)
  input.value = value
  input.dispatchEvent(new Event('input', { bubbles: true }))
  await flushUi()
}

function clickLookupAction(index: number, action: string) {
  const button = queryLookupRoot(index).querySelector(`button[data-action="${action}"]`)
  if (!(button instanceof HTMLButtonElement)) throw new Error(`Lookup ${index} action "${action}" not found.`)
  button.click()
}

function deleteButton(index: number): HTMLButtonElement {
  const button = document.querySelectorAll('button[title="Delete"]')[index]
  if (!(button instanceof HTMLButtonElement)) throw new Error(`Delete button ${index} not found.`)
  return button
}

function numberInput(index: number): HTMLInputElement {
  const input = document.querySelectorAll('input[type="number"]')[index]
  if (!(input instanceof HTMLInputElement)) throw new Error(`Number input ${index} not found.`)
  return input
}

function setNumberInput(index: number, value: string) {
  const input = numberInput(index)
  input.value = value
  input.dispatchEvent(new Event('input', { bubbles: true }))
  input.dispatchEvent(new Event('change', { bubbles: true }))
}

function selectControl(index: number): HTMLSelectElement {
  const select = document.querySelectorAll('select')[index]
  if (!(select instanceof HTMLSelectElement)) throw new Error(`Select ${index} not found.`)
  return select
}

function renderStateText(rows: EditorLine[]): string {
  return rows.map((row) => [
    row.clientKey,
    String(row.side),
    row.account?.id ?? 'none',
    row.amount,
    row.memo,
    Object.entries(row.dimensions ?? {})
      .map(([key, item]) => `${key}:${item?.id ?? 'none'}`)
      .join('|'),
  ].join(';')).join(' / ')
}

async function renderHarness(args: {
  rows: EditorLine[]
  readonly?: boolean
  preloadedAccountContexts?: Record<string, AccountContext | null>
}) {
  const Harness = defineComponent({
    setup() {
      const rows = ref(clone(args.rows))

      return () => h('div', [
        h(NgbGeneralJournalEntryLinesEditor, {
          modelValue: rows.value,
          readonly: args.readonly ?? false,
          preloadedAccountContexts: args.preloadedAccountContexts ?? {},
          'onUpdate:modelValue': (next: EditorLine[]) => {
            rows.value = next
          },
        }),
        h('div', { 'data-testid': 'rows-state' }, renderStateText(rows.value)),
        h('div', { 'data-testid': 'rows-count' }, `rows:${rows.value.length}`),
      ])
    },
  })

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/accounting/general-journal-entries/new',
        component: Harness,
      },
      {
        path: '/lookups/coa/:id',
        component: { template: '<div>CoA</div>' },
      },
      {
        path: '/lookups/catalog/:catalogType/:id',
        component: { template: '<div>Catalog</div>' },
      },
      {
        path: '/lookups/doc/:documentType/:id',
        component: { template: '<div>Document</div>' },
      },
    ],
  })

  await router.push('/accounting/general-journal-entries/new')
  await router.isReady()

  const view = await render(Harness, {
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

  linesEditorMocks.lookupStore.searchCoa.mockResolvedValue([])
  linesEditorMocks.lookupStore.searchCatalog.mockResolvedValue([])
  linesEditorMocks.lookupStore.searchDocuments.mockResolvedValue([])
  linesEditorMocks.lookupConfig.buildCatalogUrl.mockImplementation((catalogType: string, id: string) => `/lookups/catalog/${catalogType}/${id}`)
  linesEditorMocks.lookupConfig.buildCoaUrl.mockImplementation((id: string) => `/lookups/coa/${id}`)
  linesEditorMocks.lookupConfig.buildDocumentUrl.mockImplementation((documentType: string, id: string) => `/lookups/doc/${documentType}/${id}`)
  linesEditorMocks.lookupConfig.loadDocumentItem.mockResolvedValue({ id: 'doc-1', label: 'Doc 1' })
  linesEditorMocks.lookupConfig.loadDocumentItemsByIds.mockResolvedValue([])
  linesEditorMocks.getAccountContext.mockResolvedValue({
    accountId: 'fallback-account',
    code: 'fallback-account',
    name: 'Fallback',
    dimensionRules: [],
  })
})

test('updates totals, supports add/remove flow, and keeps at least one blank line', async () => {
  await page.viewport(1280, 900)

  linesEditorMocks.getAccountContext.mockResolvedValue({
    accountId: 'cash-id',
    code: '1100',
    name: 'Cash',
    dimensionRules: [],
  })

  const { view } = await renderHarness({
    rows: [
      {
        clientKey: 'row-1',
        side: 1,
        account: { id: 'cash-id', label: '1100 Cash' },
        amount: '100.00',
        memo: 'Debit',
        dimensions: {},
      },
      {
        clientKey: 'row-2',
        side: 2,
        account: { id: 'revenue-id', label: '4100 Revenue' },
        amount: '40.00',
        memo: 'Credit',
        dimensions: {},
      },
    ],
  })

  await expect.element(view.getByText('Debit: 100.00')).toBeVisible()
  await expect.element(view.getByText('Credit: 40.00')).toBeVisible()
  await expect.element(view.getByText('Difference: 60.00')).toBeVisible()

  setNumberInput(1, '100.00')
  await flushUi()
  await expect.element(view.getByText('Difference: 0.00')).toBeVisible()

  await view.getByRole('button', { name: 'Add line' }).click()
  await flushUi()
  await expect.element(view.getByText('rows:3')).toBeVisible()

  deleteButton(0).click()
  await flushUi()
  deleteButton(0).click()
  await flushUi()
  deleteButton(0).click()
  await flushUi()

  await expect.element(view.getByText('rows:1')).toBeVisible()
  expect(view.getByTestId('rows-state').element().textContent ?? '').toContain('none;;;')
})

test('searches and selects accounts and dimensions, loads account context, and opens lookup routes', async () => {
  await page.viewport(1280, 900)

  linesEditorMocks.lookupStore.searchCoa.mockImplementation(async (query: string) => (
    query === 'cash'
      ? [{ id: 'cash-id', label: '1100 Cash' }]
      : []
  ))
  linesEditorMocks.lookupStore.searchCatalog.mockImplementation(async (catalogType: string, query: string) => (
    catalogType === 'pm.property' && query === 'tower'
      ? [{ id: 'property-1', label: 'Riverfront Tower' }]
      : []
  ))
  linesEditorMocks.getAccountContext.mockImplementation(async (accountId: string) => ({
    accountId,
    code: '1100',
    name: 'Cash',
    dimensionRules: [
      {
        dimensionId: 'property_id',
        dimensionCode: 'pm.property_id',
        ordinal: 1,
        isRequired: true,
        lookup: {
          kind: 'catalog',
          catalogType: 'pm.property',
        },
      },
    ],
  }))

  const { router, view } = await renderHarness({
    rows: [
      {
        clientKey: 'row-1',
        side: 1,
        account: null,
        amount: '',
        memo: '',
        dimensions: {},
      },
    ],
  })

  await queryLookup(0, 'cash')
  expect(linesEditorMocks.lookupStore.searchCoa).toHaveBeenCalledWith('cash')
  clickLookupAction(0, 'select-first')
  await flushUi()

  expect(linesEditorMocks.getAccountContext).toHaveBeenCalledWith('cash-id')
  await expect.element(view.getByText('Property Id')).toBeVisible()
  await expect.element(view.getByText('Required')).toBeVisible()
  expect(view.getByTestId('rows-state').element().textContent ?? '').toContain('cash-id')

  await queryLookup(1, 'tower')
  expect(linesEditorMocks.lookupStore.searchCatalog).toHaveBeenCalledWith('pm.property', 'tower')
  clickLookupAction(1, 'select-first')
  await flushUi()
  expect(view.getByTestId('rows-state').element().textContent ?? '').toContain('property_id:property-1')

  clickLookupAction(1, 'open')
  await flushUi()
  expect(router.currentRoute.value.fullPath).toBe(
    withBackTarget('/lookups/catalog/pm.property/property-1', '/accounting/general-journal-entries/new'),
  )

  await router.push('/accounting/general-journal-entries/new')
  await flushUi()

  clickLookupAction(0, 'open')
  await flushUi()
  expect(router.currentRoute.value.fullPath).toBe(
    withBackTarget('/lookups/coa/cash-id', '/accounting/general-journal-entries/new'),
  )
})

test('resolves multi-document dimension targets through the first matching document type before opening the lookup route', async () => {
  await page.viewport(1280, 900)

  linesEditorMocks.lookupStore.searchCoa.mockImplementation(async (query: string) => (
    query === 'cash'
      ? [{ id: 'cash-id', label: '1100 Cash' }]
      : []
  ))
  linesEditorMocks.lookupStore.searchDocuments.mockImplementation(async (documentTypes: string[], query: string) => (
    documentTypes.join('|') === 'pm.invoice|pm.credit_note' && query === 'credit'
      ? [{ id: 'doc-1', label: 'Credit Memo CM-001' }]
      : []
  ))
  linesEditorMocks.getAccountContext.mockImplementation(async (accountId: string) => ({
    accountId,
    code: '1100',
    name: 'Cash',
    dimensionRules: [
      {
        dimensionId: 'source_doc',
        dimensionCode: 'pm.source_doc',
        ordinal: 1,
        isRequired: false,
        lookup: {
          kind: 'document',
          documentTypes: ['pm.invoice', 'pm.credit_note'],
        },
      },
    ],
  }))
  linesEditorMocks.lookupConfig.loadDocumentItemsByIds.mockImplementation(async (documentTypes: string[], ids: string[]) => {
    expect(documentTypes).toEqual(['pm.invoice', 'pm.credit_note'])
    expect(ids).toEqual(['doc-1'])

    return [{ id: 'doc-1', label: 'Credit Memo CM-001', documentType: 'pm.credit_note' }]
  })

  const { router, view } = await renderHarness({
    rows: [
      {
        clientKey: 'row-1',
        side: 1,
        account: null,
        amount: '',
        memo: '',
        dimensions: {},
      },
    ],
  })

  await queryLookup(0, 'cash')
  clickLookupAction(0, 'select-first')
  await flushUi()

  await queryLookup(1, 'credit')
  expect(linesEditorMocks.lookupStore.searchDocuments).toHaveBeenCalledWith(
    ['pm.invoice', 'pm.credit_note'],
    'credit',
  )
  clickLookupAction(1, 'select-first')
  await flushUi()

  expect(view.getByTestId('rows-state').element().textContent ?? '').toContain('source_doc:doc-1')

  clickLookupAction(1, 'open')
  await flushUi()

  expect(linesEditorMocks.lookupConfig.loadDocumentItemsByIds).toHaveBeenCalledWith(
    ['pm.invoice', 'pm.credit_note'],
    ['doc-1'],
  )
  expect(router.currentRoute.value.fullPath).toBe(
    withBackTarget('/lookups/doc/pm.credit_note/doc-1', '/accounting/general-journal-entries/new'),
  )
})

test('keeps the editor usable after account-context failures and allows a later successful retry', async () => {
  await page.viewport(1280, 900)

  linesEditorMocks.lookupStore.searchCoa.mockImplementation(async (query: string) => {
    if (query === 'cash') return [{ id: 'cash-id', label: '1100 Cash' }]
    if (query === 'rent') return [{ id: 'rent-id', label: '4100 Rent Revenue' }]
    return []
  })
  linesEditorMocks.getAccountContext
    .mockRejectedValueOnce(new Error('context failed'))
    .mockResolvedValueOnce({
      accountId: 'rent-id',
      code: '4100',
      name: 'Rent Revenue',
      dimensionRules: [
        {
          dimensionId: 'department_id',
          dimensionCode: 'pm.department_id',
          ordinal: 1,
          isRequired: false,
          lookup: {
            kind: 'coa',
          },
        },
      ],
    })

  const { view } = await renderHarness({
    rows: [
      {
        clientKey: 'row-1',
        side: 1,
        account: null,
        amount: '',
        memo: '',
        dimensions: {},
      },
    ],
  })

  await queryLookup(0, 'cash')
  clickLookupAction(0, 'select-first')
  await flushUi()

  expect(linesEditorMocks.getAccountContext).toHaveBeenCalledWith('cash-id')
  expect(document.body.textContent).not.toContain('Loading dimension rules…')
  expect(document.body.textContent).not.toContain('Property Id')
  expect(document.body.textContent).not.toContain('Department Id')

  await queryLookup(0, 'rent')
  clickLookupAction(0, 'select-first')
  await flushUi()

  expect(linesEditorMocks.getAccountContext).toHaveBeenCalledWith('rent-id')
  await expect.element(view.getByText('Department Id')).toBeVisible()
})

test('ignores stale account contexts when the user switches accounts before the first context load resolves', async () => {
  await page.viewport(1280, 900)

  const first = createDeferred<AccountContext>()
  const second = createDeferred<AccountContext>()

  linesEditorMocks.lookupStore.searchCoa.mockImplementation(async (query: string) => {
    if (query === 'cash') return [{ id: 'cash-id', label: '1100 Cash' }]
    if (query === 'rent') return [{ id: 'rent-id', label: '4100 Rent Revenue' }]
    return []
  })
  linesEditorMocks.getAccountContext.mockImplementation(async (accountId: string) => {
    if (accountId === 'cash-id') return await first.promise
    return await second.promise
  })

  const { view } = await renderHarness({
    rows: [
      {
        clientKey: 'row-1',
        side: 1,
        account: null,
        amount: '',
        memo: '',
        dimensions: {},
      },
    ],
  })

  await queryLookup(0, 'cash')
  clickLookupAction(0, 'select-first')
  await flushUi()

  await queryLookup(0, 'rent')
  clickLookupAction(0, 'select-first')
  await flushUi()

  second.resolve({
    accountId: 'rent-id',
    code: '4100',
    name: 'Rent Revenue',
    dimensionRules: [
      {
        dimensionId: 'department_id',
        dimensionCode: 'pm.department_id',
        ordinal: 1,
        isRequired: false,
        lookup: {
          kind: 'coa',
        },
      },
    ],
  })
  await flushUi()

  await expect.element(view.getByText('Department Id')).toBeVisible()

  first.resolve({
    accountId: 'cash-id',
    code: '1100',
    name: 'Cash',
    dimensionRules: [
      {
        dimensionId: 'property_id',
        dimensionCode: 'pm.property_id',
        ordinal: 1,
        isRequired: true,
        lookup: {
          kind: 'catalog',
          catalogType: 'pm.property',
        },
      },
    ],
  })
  await flushUi()

  await expect.element(view.getByText('Department Id')).toBeVisible()
  expect(document.body.textContent).not.toContain('Property Id')
})

test('clears selected dimensions when the account changes and drops old dimension UI after deleting the row', async () => {
  await page.viewport(1280, 900)

  linesEditorMocks.lookupStore.searchCoa.mockImplementation(async (query: string) => {
    if (query === 'cash') return [{ id: 'cash-id', label: '1100 Cash' }]
    if (query === 'rent') return [{ id: 'rent-id', label: '4100 Rent Revenue' }]
    return []
  })
  linesEditorMocks.lookupStore.searchCatalog.mockImplementation(async (catalogType: string, query: string) => (
    catalogType === 'pm.property' && query === 'tower'
      ? [{ id: 'property-1', label: 'Riverfront Tower' }]
      : []
  ))
  linesEditorMocks.getAccountContext.mockImplementation(async (accountId: string) => {
    if (accountId === 'cash-id') {
      return {
        accountId,
        code: '1100',
        name: 'Cash',
        dimensionRules: [
          {
            dimensionId: 'property_id',
            dimensionCode: 'pm.property_id',
            ordinal: 1,
            isRequired: true,
            lookup: {
              kind: 'catalog',
              catalogType: 'pm.property',
            },
          },
        ],
      }
    }

    return {
      accountId,
      code: '4100',
      name: 'Rent Revenue',
      dimensionRules: [
        {
          dimensionId: 'department_id',
          dimensionCode: 'pm.department_id',
          ordinal: 1,
          isRequired: false,
          lookup: {
            kind: 'coa',
          },
        },
      ],
    }
  })

  const { view } = await renderHarness({
    rows: [
      {
        clientKey: 'row-1',
        side: 1,
        account: null,
        amount: '',
        memo: '',
        dimensions: {},
      },
    ],
  })

  await queryLookup(0, 'cash')
  clickLookupAction(0, 'select-first')
  await flushUi()
  await expect.element(view.getByText('Property Id')).toBeVisible()

  await queryLookup(1, 'tower')
  clickLookupAction(1, 'select-first')
  await flushUi()
  expect(view.getByTestId('rows-state').element().textContent ?? '').toContain('property_id:property-1')

  await queryLookup(0, 'rent')
  clickLookupAction(0, 'select-first')
  await flushUi()

  await expect.element(view.getByText('Department Id')).toBeVisible()
  expect(document.body.textContent).not.toContain('Property Id')
  expect(view.getByTestId('rows-state').element().textContent ?? '').not.toContain('property_id:property-1')

  deleteButton(0).click()
  await flushUi()

  await expect.element(view.getByText('rows:1')).toBeVisible()
  expect(document.body.textContent).not.toContain('Department Id')
})

test('uses preloaded account contexts and keeps controls disabled in readonly mode', async () => {
  await page.viewport(1280, 900)

  const { view } = await renderHarness({
    readonly: true,
    rows: [
      {
        clientKey: 'row-1',
        side: 1,
        account: { id: 'cash-id', label: '1100 Cash' },
        amount: '55.00',
        memo: 'Locked',
        dimensions: {},
      },
    ],
    preloadedAccountContexts: {
      'cash-id': {
        accountId: 'cash-id',
        code: '1100',
        name: 'Cash',
        dimensionRules: [
          {
            dimensionId: 'department_id',
            dimensionCode: 'pm.department_id',
            ordinal: 1,
            isRequired: false,
            lookup: {
              kind: 'coa',
            },
          },
        ],
      },
    },
  })

  expect(linesEditorMocks.getAccountContext).not.toHaveBeenCalled()
  await expect.element(view.getByText('Department Id')).toBeVisible()

  expect(selectControl(0).disabled).toBe(true)
  expect(numberInput(0).disabled).toBe(true)
  expect(deleteButton(0).disabled).toBe(true)
  expect((view.getByRole('button', { name: /Add line/ }).element() as HTMLButtonElement).disabled).toBe(true)
  expect(queryLookupRoot(0).querySelector('input') instanceof HTMLInputElement).toBe(true)
  expect((queryLookupRoot(0).querySelector('input') as HTMLInputElement).disabled).toBe(true)
})
