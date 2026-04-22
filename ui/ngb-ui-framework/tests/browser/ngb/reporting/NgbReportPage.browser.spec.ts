import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createMemoryHistory, createRouter, RouterView } from 'vue-router'
import { defineComponent, h } from 'vue'

import {
  StubBadge,
  StubDatePicker,
  StubDateRangeFilter,
  StubDialog,
  StubDrawer,
  StubIcon,
  StubInput,
  StubLookup,
  StubPageHeader,
  StubReportComposerPanel,
  StubReportSheet,
  StubSwitch,
} from './stubs'

type CommandPaletteContext = {
  entityType?: string
  title?: string | null
  actions: Array<{
    key: string
    title: string
    perform?: (() => void | Promise<void>) | null
  }>
}

const reportPageMocks = vi.hoisted(() => ({
  deleteReportVariant: vi.fn(),
  executeReport: vi.fn(),
  exportReportXlsx: vi.fn(),
  getReportDefinition: vi.fn(),
  getReportVariants: vi.fn(),
  resolveLookupTarget: vi.fn(),
  saveReportVariant: vi.fn(),
  useCommandPalettePageContext: vi.fn(),
  state: {
    definition: null as Record<string, unknown> | null,
    lookupItems: [
      { id: '11111111-1111-1111-1111-111111111111', label: 'Riverfront Tower' },
      { id: '22222222-2222-2222-2222-222222222222', label: 'North Square' },
    ],
    lookupQueries: [] as Array<{ catalogType: string; query: string }>,
    variants: [] as Array<Record<string, unknown>>,
    executeRequests: [] as Array<Record<string, unknown>>,
    exportRequests: [] as Array<Record<string, unknown>>,
    executeResponses: [] as Array<Record<string, unknown>>,
    deletedVariants: [] as string[],
    commandPaletteResolver: null as null | (() => CommandPaletteContext | null | undefined),
  },
}))

vi.mock('../../../../src/ngb/reporting/api', () => ({
  deleteReportVariant: reportPageMocks.deleteReportVariant,
  executeReport: reportPageMocks.executeReport,
  exportReportXlsx: reportPageMocks.exportReportXlsx,
  getReportDefinition: reportPageMocks.getReportDefinition,
  getReportVariants: reportPageMocks.getReportVariants,
  saveReportVariant: reportPageMocks.saveReportVariant,
}))

vi.mock('../../../../src/ngb/command-palette/useCommandPalettePageContext', () => ({
  useCommandPalettePageContext: reportPageMocks.useCommandPalettePageContext,
}))

vi.mock('../../../../src/ngb/primitives/NgbBadge.vue', () => ({
  default: StubBadge,
}))

vi.mock('../../../../src/ngb/primitives/NgbDatePicker.vue', () => ({
  default: StubDatePicker,
}))

vi.mock('../../../../src/ngb/components/NgbDialog.vue', () => ({
  default: StubDialog,
}))

vi.mock('../../../../src/ngb/components/NgbDrawer.vue', () => ({
  default: StubDrawer,
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

vi.mock('../../../../src/ngb/primitives/NgbSwitch.vue', () => ({
  default: StubSwitch,
}))

vi.mock('../../../../src/ngb/site/NgbPageHeader.vue', () => ({
  default: StubPageHeader,
}))

vi.mock('../../../../src/ngb/reporting/NgbReportComposerPanel.vue', () => ({
  default: StubReportComposerPanel,
}))

vi.mock('../../../../src/ngb/reporting/NgbReportDateRangeFilter.vue', () => ({
  default: StubDateRangeFilter,
}))

vi.mock('../../../../src/ngb/reporting/NgbReportSheet.vue', () => ({
  default: StubReportSheet,
}))

import { configureNgbReporting } from '../../../../src/ngb/reporting/config'
import { createDefaultNgbReportingConfig } from '../../../../src/ngb/reporting/defaultConfig'
import {
  decodeReportRouteContextParam,
  encodeReportRouteContextParam,
  encodeReportSourceTrailParam,
} from '../../../../src/ngb/reporting/navigation'
import { saveReportPageExecutionSnapshot, saveReportPageScrollTop } from '../../../../src/ngb/reporting/pageSession'
import NgbReportPage from '../../../../src/ngb/reporting/NgbReportPage.vue'
import { ReportExecutionMode, ReportFieldKind, ReportRowKind, type ReportDefinitionDto, type ReportExecutionRequestDto, type ReportExecutionResponseDto, type ReportExportRequestDto, type ReportFilterFieldDto, type ReportVariantDto } from '../../../../src/ngb/reporting/types'
import { decodeBackTarget, encodeBackTarget, withBackTarget } from '../../../../src/ngb/router/backNavigation'
import { configureNgbLookup } from '../../../../src/ngb/lookup/config'
import { stableStringify } from '../../../../src/ngb/utils/stableValue'

const baseDefinition: ReportDefinitionDto = {
  reportCode: 'pm.occupancy.summary',
  name: 'Occupancy Summary',
  description: 'Portfolio occupancy by property.',
  mode: ReportExecutionMode.Composable,
  capabilities: {
    allowsFilters: true,
    allowsVariants: true,
    allowsXlsxExport: true,
    allowsMeasures: true,
    allowsRowGroups: true,
  },
  dataset: {
    datasetCode: 'pm.occupancy.summary',
    fields: [
      {
        code: 'property',
        label: 'Property',
        dataType: 'String',
        kind: ReportFieldKind.Attribute,
        isGroupable: true,
        isSelectable: true,
        isSortable: true,
        lookup: {
          kind: 'catalog',
          catalogType: 'pm.property',
        },
      },
      {
        code: 'status',
        label: 'Status',
        dataType: 'String',
        kind: ReportFieldKind.Dimension,
        isFilterable: true,
      },
    ],
    measures: [
      {
        code: 'occupied_units',
        label: 'Occupied Units',
        dataType: 'Decimal',
        supportedAggregations: [1],
      },
    ],
  },
  defaultLayout: {
    detailFields: [],
    measures: [
      { measureCode: 'occupied_units', aggregation: 1 },
    ],
    showDetails: true,
    showSubtotals: true,
    showGrandTotals: true,
  },
  parameters: [
    {
      code: 'as_of_utc',
      label: 'As of',
      dataType: 'Date Only',
      isRequired: true,
    },
  ],
  filters: [
    {
      fieldCode: 'property',
      label: 'Property',
      dataType: 'Guid',
      isRequired: true,
      lookup: {
        kind: 'catalog',
        catalogType: 'pm.property',
      },
      supportsIncludeDescendants: true,
    },
    {
      fieldCode: 'status',
      label: 'Status',
      dataType: 'String',
      options: [
        { value: 'open', label: 'Open' },
        { value: 'posted', label: 'Posted' },
      ],
    },
  ],
  presentation: {
    initialPageSize: 2,
    rowNoun: 'property',
    emptyStateMessage: 'Run the report to review occupancy.',
  },
}

const baseVariants: ReportVariantDto[] = [
  {
    variantCode: 'portfolio-view',
    reportCode: 'pm.occupancy.summary',
    name: 'Portfolio View',
    filters: {
      status: {
        value: 'open',
      },
    },
    parameters: {
      as_of_utc: '2026-04-01',
    },
    layout: null,
    isDefault: true,
    isShared: true,
  },
  {
    variantCode: 'audit-view',
    reportCode: 'pm.occupancy.summary',
    name: 'Audit View',
    filters: {
      property: {
        value: '22222222-2222-2222-2222-222222222222',
      },
      status: {
        value: 'posted',
      },
    },
    parameters: {
      as_of_utc: '2026-01-31',
    },
    layout: null,
    isDefault: false,
    isShared: true,
  },
]

const AppRoot = defineComponent({
  setup() {
    return () => h(RouterView)
  },
})

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

  return {
    promise,
    resolve,
    reject,
  }
}

function buildResponse(options?: {
  rows?: string[]
  total?: number
  hasMore?: boolean
  nextCursor?: string | null
}): ReportExecutionResponseDto {
  const rows = options?.rows ?? ['Riverfront Tower']

  return {
    sheet: {
      columns: [
        { code: 'property', title: 'Property', dataType: 'string' },
      ],
      rows: rows.map((label) => ({
        rowKind: ReportRowKind.Detail,
        cells: [
          { display: label, value: label, valueType: 'string' },
        ],
      })),
      meta: {
        title: 'Occupancy Summary',
      },
    },
    offset: 0,
    limit: 2,
    total: options?.total ?? rows.length,
    hasMore: options?.hasMore ?? false,
    nextCursor: options?.nextCursor ?? null,
  }
}

function normalizeVariantDefaults(variants: ReportVariantDto[]): ReportVariantDto[] {
  let seenDefault = false
  return variants.map((variant) => {
    if (!variant.isDefault || seenDefault) return { ...variant, isDefault: !!variant.isDefault && !seenDefault }
    seenDefault = true
    return { ...variant, isDefault: true }
  })
}

function createLookupStore() {
  return {
    searchCatalog: vi.fn(async (catalogType: string, query: string) => {
      reportPageMocks.state.lookupQueries.push({ catalogType, query })
      return clone(
        reportPageMocks.state.lookupItems.filter((item) =>
          item.label.toLowerCase().includes(query.trim().toLowerCase()),
        ),
      )
    }),
    searchCoa: vi.fn(async () => []),
    searchDocuments: vi.fn(async () => []),
    ensureCatalogLabels: vi.fn(async () => undefined),
    ensureCoaLabels: vi.fn(async () => undefined),
    ensureAnyDocumentLabels: vi.fn(async () => undefined),
    labelForCatalog: vi.fn((catalogType: string, id: unknown) => {
      const match = reportPageMocks.state.lookupItems.find((item) => item.id === String(id))
      return match?.label ?? `${catalogType}:${String(id)}`
    }),
    labelForCoa: vi.fn((id: unknown) => String(id)),
    labelForAnyDocument: vi.fn((documentTypes: string[], id: unknown) => `${documentTypes.join('|')}:${String(id)}`),
  }
}

function createScenarioLookupStore(args: {
  items: Array<{ id: string; label: string }>
  kind: 'catalog' | 'coa' | 'document'
}) {
  return {
    searchCatalog: vi.fn(async (catalogType: string, query: string) => {
      reportPageMocks.state.lookupQueries.push({ catalogType, query })
      if (args.kind !== 'catalog') return []

      return clone(
        args.items.filter((item) =>
          item.label.toLowerCase().includes(query.trim().toLowerCase()),
        ),
      )
    }),
    searchCoa: vi.fn(async (query: string) => {
      if (args.kind !== 'coa') return []

      return clone(
        args.items.filter((item) =>
          item.label.toLowerCase().includes(query.trim().toLowerCase()),
        ),
      )
    }),
    searchDocuments: vi.fn(async (_documentTypes: string[], query: string) => {
      if (args.kind !== 'document') return []

      return clone(
        args.items.filter((item) =>
          item.label.toLowerCase().includes(query.trim().toLowerCase()),
        ),
      )
    }),
    ensureCatalogLabels: vi.fn(async () => undefined),
    ensureCoaLabels: vi.fn(async () => undefined),
    ensureAnyDocumentLabels: vi.fn(async () => undefined),
    labelForCatalog: vi.fn((catalogType: string, id: unknown) => {
      const match = args.items.find((item) => item.id === String(id))
      return match?.label ?? `${catalogType}:${String(id)}`
    }),
    labelForCoa: vi.fn((id: unknown) => {
      const match = args.items.find((item) => item.id === String(id))
      return match?.label ?? String(id)
    }),
    labelForAnyDocument: vi.fn((documentTypes: string[], id: unknown) => {
      const match = args.items.find((item) => item.id === String(id))
      return match?.label ?? `${documentTypes.join('|')}:${String(id)}`
    }),
  }
}

function buildLookupOnlyDefinition(filter: ReportFilterFieldDto): ReportDefinitionDto {
  return clone({
    ...baseDefinition,
    capabilities: {
      ...baseDefinition.capabilities,
      allowsVariants: false,
    },
    filters: [filter],
  })
}

function configureDefaultLookupScenario(args: {
  lookupStore: ReturnType<typeof createScenarioLookupStore>
  loadDocumentItem?: (documentType: string, id: string) => Promise<{ id: string; label: string } | null>
  loadDocumentItemsByIds?: (
    documentTypes: string[],
    ids: string[],
  ) => Promise<Array<{ id: string; label: string; documentType: string }>>
}) {
  configureNgbLookup({
    loadCatalogItemsByIds: vi.fn(async (catalogType: string, ids: string[]) =>
      ids.map((id) => ({
        id,
        label: args.lookupStore.labelForCatalog(catalogType, id),
      })),
    ),
    searchCatalog: vi.fn(async () => []),
    loadCoaItemsByIds: vi.fn(async (ids: string[]) =>
      ids.map((id) => ({
        id,
        label: args.lookupStore.labelForCoa(id),
      }))),
    loadCoaItem: vi.fn(async (id: string) => ({ id, label: args.lookupStore.labelForCoa(id) })),
    searchCoa: vi.fn(async () => []),
    loadDocumentItem: vi.fn(args.loadDocumentItem ?? (async (documentType: string, id: string) => ({
      id,
      label: args.lookupStore.labelForAnyDocument([documentType], id),
    }))),
    loadDocumentItemsByIds: vi.fn(args.loadDocumentItemsByIds ?? (async (documentTypes: string[], ids: string[]) =>
      ids.map((id) => ({
        id,
        label: args.lookupStore.labelForAnyDocument(documentTypes, id),
        documentType: documentTypes[0] ?? '',
      })))),
    searchDocument: vi.fn(async () => []),
    searchDocumentsAcrossTypes: vi.fn(async () => []),
    buildCatalogUrl: (catalogType: string, id: string) => `/catalogs/${catalogType}/${id}`,
    buildCoaUrl: (id: string) => `/admin/chart-of-accounts?panel=edit&id=${encodeURIComponent(id)}`,
    buildDocumentUrl: (documentType: string, id: string) => `/documents/${documentType}/${id}`,
  })

  configureNgbReporting({
    ...createDefaultNgbReportingConfig(),
    useLookupStore: () => args.lookupStore,
  })
}

async function flushUi() {
  await new Promise((resolve) => window.setTimeout(resolve, 50))
}

async function renderReportPage(initialUrl = '/reports/pm.occupancy.summary') {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/reports/:reportCode',
        component: NgbReportPage,
      },
      {
        path: '/catalogs/:catalogType/:id',
        component: {
          template: '<div data-testid="catalog-target-page">Catalog target</div>',
        },
      },
      {
        path: '/admin/chart-of-accounts',
        component: {
          template: '<div data-testid="coa-target-page">Chart of accounts target</div>',
        },
      },
      {
        path: '/documents/:documentType/:id',
        component: {
          template: '<div data-testid="document-target-page">Document target</div>',
        },
      },
    ],
  })

  await router.push(initialUrl)
  await router.isReady()

  const view = await render(AppRoot, {
    global: {
      plugins: [router],
    },
  })

  await flushUi()
  return { router, view }
}

function clickHeaderButtonByTitle(title: string) {
  const button = document.querySelector(`button[title="${title}"]`)
  if (!(button instanceof HTMLButtonElement)) throw new Error(`Button with title "${title}" not found.`)
  button.click()
}

function lookupInput(): HTMLInputElement {
  const input = document.querySelector('[data-testid="stub-lookup"] input')
  if (!(input instanceof HTMLInputElement)) throw new Error('Lookup input not found.')
  return input
}

function lookupAction(action: 'select-first' | 'open' | 'clear'): HTMLButtonElement {
  const button = document.querySelector(`[data-testid="stub-lookup"] button[data-action="${action}"]`)
  if (!(button instanceof HTMLButtonElement)) throw new Error(`Lookup action "${action}" not found.`)
  return button
}

function reportPageStateKey(options?: {
  variant?: string | null
  ctx?: string | null
  src?: string | null
}) {
  return stableStringify({
    reportCode: 'pm.occupancy.summary',
    variant: options?.variant ?? null,
    ctx: options?.ctx ?? null,
    src: options?.src ?? null,
  })
}

beforeEach(() => {
  sessionStorage.clear()

  reportPageMocks.state.definition = clone(baseDefinition)
  reportPageMocks.state.lookupQueries = []
  reportPageMocks.state.variants = clone(baseVariants)
  reportPageMocks.state.executeRequests = []
  reportPageMocks.state.exportRequests = []
  reportPageMocks.state.executeResponses = []
  reportPageMocks.state.deletedVariants = []
  reportPageMocks.state.commandPaletteResolver = null

  reportPageMocks.useCommandPalettePageContext.mockReset()
  reportPageMocks.getReportDefinition.mockReset()
  reportPageMocks.getReportVariants.mockReset()
  reportPageMocks.executeReport.mockReset()
  reportPageMocks.exportReportXlsx.mockReset()
  reportPageMocks.saveReportVariant.mockReset()
  reportPageMocks.deleteReportVariant.mockReset()
  reportPageMocks.resolveLookupTarget.mockReset()

  reportPageMocks.useCommandPalettePageContext.mockImplementation((resolve: () => CommandPaletteContext | null | undefined) => {
    reportPageMocks.state.commandPaletteResolver = resolve
  })

  reportPageMocks.getReportDefinition.mockImplementation(async () => clone(reportPageMocks.state.definition))
  reportPageMocks.getReportVariants.mockImplementation(async () => clone(reportPageMocks.state.variants))
  reportPageMocks.executeReport.mockImplementation(async (_reportCode: string, request: ReportExecutionRequestDto) => {
    reportPageMocks.state.executeRequests.push(clone(request as Record<string, unknown>))
    const next = reportPageMocks.state.executeResponses.shift() ?? buildResponse()
    return clone(next as ReportExecutionResponseDto)
  })
  reportPageMocks.exportReportXlsx.mockImplementation(async (_reportCode: string, request: ReportExportRequestDto) => {
    reportPageMocks.state.exportRequests.push(clone(request as Record<string, unknown>))
    return {
      blob: new Blob(['xlsx']),
      fileName: 'occupancy-summary.xlsx',
    }
  })
  reportPageMocks.saveReportVariant.mockImplementation(async (reportCode: string, variantCode: string, variant: ReportVariantDto) => {
    const saved = clone({
      ...variant,
      reportCode,
      variantCode,
    })

    reportPageMocks.state.variants = normalizeVariantDefaults([
      ...reportPageMocks.state.variants.filter((entry) => String(entry.variantCode) !== variantCode),
      saved,
    ])

    return clone(saved)
  })
  reportPageMocks.deleteReportVariant.mockImplementation(async (_reportCode: string, variantCode: string) => {
    reportPageMocks.state.deletedVariants.push(variantCode)
    reportPageMocks.state.variants = reportPageMocks.state.variants.filter((entry) => String(entry.variantCode) !== variantCode)
  })
  reportPageMocks.resolveLookupTarget.mockImplementation(async ({ value }: { value: { id?: string } | null }) => {
    const id = String(value?.id ?? '').trim()
    return id ? `/catalogs/pm.property/${id}` : null
  })

  configureNgbReporting({
    useLookupStore: createLookupStore,
    resolveLookupTarget: reportPageMocks.resolveLookupTarget,
  })
})

test('wires inline filters, runs the report, exports xlsx, and opens the selected lookup target', async () => {
  await page.viewport(1280, 900)
  reportPageMocks.state.executeResponses = [
    buildResponse({ rows: ['Riverfront Tower'], total: 1 }),
  ]

  const { router, view } = await renderReportPage()

  await expect.element(view.getByText('Occupancy Summary')).toBeVisible()
  await expect.element(view.getByText('Status: Open', { exact: true })).toBeVisible()
  await expect.element(view.getByText('lookup-value:none')).toBeVisible()

  const lookupInput = document.querySelector('[data-testid="stub-lookup"] input')
  if (!(lookupInput instanceof HTMLInputElement)) throw new Error('Lookup input not found.')
  lookupInput.value = 'tower'
  lookupInput.dispatchEvent(new Event('input', { bubbles: true }))
  await flushUi()

  expect(reportPageMocks.state.lookupQueries).toEqual([
    { catalogType: 'pm.property', query: 'tower' },
  ])

  const lookupButtons = document.querySelectorAll('[data-testid="stub-lookup"] button')
  ;(lookupButtons[0] as HTMLButtonElement).click()
  await expect.element(view.getByText('lookup-value:Riverfront Tower')).toBeVisible()

  const dateInput = document.querySelector('input[type="date"][data-testid^="stub-date-picker"]')
  if (!(dateInput instanceof HTMLInputElement)) throw new Error('Date input not found.')
  dateInput.value = '2026-04-30'
  dateInput.dispatchEvent(new Event('input', { bubbles: true }))
  dateInput.dispatchEvent(new Event('change', { bubbles: true }))
  await flushUi()

  clickHeaderButtonByTitle('Run')
  await flushUi()

  expect(reportPageMocks.executeReport).toHaveBeenCalledTimes(1)
  expect(reportPageMocks.state.executeRequests.at(-1)).toMatchObject({
    parameters: {
      as_of_utc: '2026-04-30',
    },
    filters: {
      property: {
        value: '11111111-1111-1111-1111-111111111111',
        includeDescendants: false,
      },
      status: {
        value: 'open',
        includeDescendants: false,
      },
    },
    limit: 2,
  })

  await expect.element(view.getByText('rows:1')).toBeVisible()
  await expect.element(view.getByText('loaded:1')).toBeVisible()

  clickHeaderButtonByTitle('Download')
  await flushUi()

  expect(reportPageMocks.exportReportXlsx).toHaveBeenCalledTimes(1)
  expect(reportPageMocks.state.exportRequests.at(-1)).toMatchObject({
    parameters: {
      as_of_utc: '2026-04-30',
    },
    filters: {
      property: {
        value: '11111111-1111-1111-1111-111111111111',
      },
      status: {
        value: 'open',
      },
    },
  })
  expect(reportPageMocks.state.exportRequests.at(-1)).not.toHaveProperty('offset')
  expect(reportPageMocks.state.exportRequests.at(-1)).not.toHaveProperty('limit')

  ;(document.querySelector('[data-action="open"]') as HTMLButtonElement).click()
  await flushUi()

  expect(reportPageMocks.resolveLookupTarget).toHaveBeenCalledWith(expect.objectContaining({
    value: {
      id: '11111111-1111-1111-1111-111111111111',
      label: 'Riverfront Tower',
    },
  }))
  expect(router.currentRoute.value.fullPath).toBe('/catalogs/pm.property/11111111-1111-1111-1111-111111111111')
  await expect.element(view.getByTestId('catalog-target-page')).toBeVisible()
})

test('reopens a shared report url and restores the route context without relying on session snapshots', async () => {
  await page.viewport(1280, 900)

  reportPageMocks.state.variants = []
  reportPageMocks.state.executeResponses = [
    buildResponse({ rows: ['North Square run'], total: 1 }),
    buildResponse({ rows: ['North Square reopen'], total: 1 }),
  ]

  const first = await renderReportPage(`/reports/pm.occupancy.summary?back=${encodeBackTarget('/workspace/home')}`)

  lookupInput().value = 'north'
  lookupInput().dispatchEvent(new Event('input', { bubbles: true }))
  await flushUi()

  lookupAction('select-first').click()
  await expect.element(first.view.getByText('lookup-value:North Square')).toBeVisible()

  const dateInput = document.querySelector('input[type="date"][data-testid^="stub-date-picker"]')
  if (!(dateInput instanceof HTMLInputElement)) throw new Error('Date input not found.')
  dateInput.value = '2026-04-30'
  dateInput.dispatchEvent(new Event('input', { bubbles: true }))
  dateInput.dispatchEvent(new Event('change', { bubbles: true }))
  await flushUi()

  clickHeaderButtonByTitle('Run')
  await flushUi()

  const sharedUrl = first.router.currentRoute.value.fullPath
  const sharedContext = decodeReportRouteContextParam(first.router.currentRoute.value.query.ctx)

  expect(sharedContext).toMatchObject({
    reportCode: 'pm.occupancy.summary',
    request: {
      parameters: {
        as_of_utc: '2026-04-30',
      },
      filters: {
        property: {
          value: '22222222-2222-2222-2222-222222222222',
        },
      },
      variantCode: null,
    },
  })

  first.view.unmount()
  sessionStorage.clear()
  reportPageMocks.executeReport.mockClear()
  reportPageMocks.state.executeRequests = []

  const reopened = await renderReportPage(sharedUrl)
  await flushUi()

  expect(reportPageMocks.executeReport).toHaveBeenCalledTimes(1)
  expect(reportPageMocks.state.executeRequests[0]).toMatchObject({
    parameters: {
      as_of_utc: '2026-04-30',
    },
    filters: {
      property: {
        value: '22222222-2222-2222-2222-222222222222',
        includeDescendants: false,
      },
    },
  })
  expect(reopened.router.currentRoute.value.fullPath).toBe(sharedUrl)
  await expect.element(reopened.view.getByText('lookup-value:North Square')).toBeVisible()
  await expect.element(reopened.view.getByText('rows:1')).toBeVisible()

  const backTargetText = Array.from(document.querySelectorAll('[data-testid="stub-report-sheet"] div'))
    .map((entry) => entry.textContent ?? '')
    .find((entry) => entry.startsWith('back-target:'))
  const nestedBackTarget = String(backTargetText ?? '').slice('back-target:'.length)
  const nestedBackParam = new URL(nestedBackTarget, 'https://example.test').searchParams.get('back')

  expect(decodeBackTarget(nestedBackParam)).toBe('/workspace/home')
})

test('ignores stale in-flight report executions after navigation switches to another report', async () => {
  await page.viewport(1280, 900)

  const staleRun = createDeferred<ReportExecutionResponseDto>()
  const definitions = new Map<string, ReportDefinitionDto>([
    ['pm.occupancy.summary', clone(baseDefinition)],
    ['pm.portfolio.home', {
      ...clone(baseDefinition),
      reportCode: 'pm.portfolio.home',
      name: 'Portfolio Home',
      description: 'Portfolio landing report.',
    }],
  ])

  reportPageMocks.getReportDefinition.mockImplementation(async (reportCode: string) => {
    const definition = definitions.get(reportCode)
    if (!definition) throw new Error(`Unknown report ${reportCode}`)
    return clone(definition)
  })
  reportPageMocks.getReportVariants.mockImplementation(async () => [])
  reportPageMocks.executeReport.mockImplementation(async (_reportCode: string, request: ReportExecutionRequestDto) => {
    reportPageMocks.state.executeRequests.push(clone(request as Record<string, unknown>))
    return await staleRun.promise
  })

  const { router, view } = await renderReportPage('/reports/pm.occupancy.summary')

  lookupInput().value = 'north'
  lookupInput().dispatchEvent(new Event('input', { bubbles: true }))
  await flushUi()
  lookupAction('select-first').click()
  await expect.element(view.getByText('lookup-value:North Square')).toBeVisible()

  const dateInput = document.querySelector('input[type="date"][data-testid^="stub-date-picker"]')
  if (!(dateInput instanceof HTMLInputElement)) throw new Error('Date input not found.')
  dateInput.value = '2026-04-30'
  dateInput.dispatchEvent(new Event('input', { bubbles: true }))
  dateInput.dispatchEvent(new Event('change', { bubbles: true }))
  await flushUi()

  clickHeaderButtonByTitle('Run')
  await flushUi()
  expect(reportPageMocks.executeReport).toHaveBeenCalledTimes(1)

  await router.push('/reports/pm.portfolio.home')
  await flushUi()

  await expect.element(view.getByText('Portfolio Home')).toBeVisible()
  await expect.element(view.getByText('rows:0')).toBeVisible()

  staleRun.resolve(buildResponse({ rows: ['Stale occupancy result'], total: 1 }))
  await flushUi()
  await flushUi()

  await expect.element(view.getByText('Portfolio Home')).toBeVisible()
  await expect.element(view.getByText('rows:0')).toBeVisible()
  expect(document.body.textContent ?? '').not.toContain('Stale occupancy result')
  expect(router.currentRoute.value.params.reportCode).toBe('pm.portfolio.home')
})

test('drops stale appended pages when a fresh rerun replaces the report while load-more is pending', async () => {
  await page.viewport(1280, 900)

  const staleAppend = createDeferred<ReportExecutionResponseDto>()
  reportPageMocks.state.variants = []
  reportPageMocks.executeReport.mockImplementation(async (_reportCode: string, request: ReportExecutionRequestDto) => {
    reportPageMocks.state.executeRequests.push(clone(request as Record<string, unknown>))

    if (request.cursor === 'cursor-2') return await staleAppend.promise

    if (request.parameters?.as_of_utc === '2026-04-30') {
      return buildResponse({ rows: ['North Square'], total: 2, hasMore: true, nextCursor: 'cursor-2' })
    }

    if (request.parameters?.as_of_utc === '2026-05-31') {
      return buildResponse({ rows: ['May rerun'], total: 1, hasMore: false, nextCursor: null })
    }

    return buildResponse()
  })

  const { view } = await renderReportPage()

  lookupInput().value = 'north'
  lookupInput().dispatchEvent(new Event('input', { bubbles: true }))
  await flushUi()
  lookupAction('select-first').click()
  await expect.element(view.getByText('lookup-value:North Square')).toBeVisible()

  const dateInput = document.querySelector('input[type="date"][data-testid^="stub-date-picker"]')
  if (!(dateInput instanceof HTMLInputElement)) throw new Error('Date input not found.')
  dateInput.value = '2026-04-30'
  dateInput.dispatchEvent(new Event('input', { bubbles: true }))
  dateInput.dispatchEvent(new Event('change', { bubbles: true }))
  await flushUi()

  clickHeaderButtonByTitle('Run')
  await flushUi()

  await expect.element(view.getByText('rows:1')).toBeVisible()
  await expect.element(view.getByText('total:2')).toBeVisible()
  await view.getByRole('button', { name: 'Load more' }).click()
  await flushUi()

  dateInput.value = '2026-05-31'
  dateInput.dispatchEvent(new Event('input', { bubbles: true }))
  dateInput.dispatchEvent(new Event('change', { bubbles: true }))
  await flushUi()

  clickHeaderButtonByTitle('Run')
  await flushUi()

  await expect.element(view.getByText('total:1')).toBeVisible()
  expect(reportPageMocks.state.executeRequests.at(-1)).toMatchObject({
    parameters: {
      as_of_utc: '2026-05-31',
    },
  })

  staleAppend.resolve(buildResponse({ rows: ['Stale Harbor Point'], total: 2, hasMore: false }))
  await flushUi()
  await flushUi()

  await expect.element(view.getByText('rows:1')).toBeVisible()
  await expect.element(view.getByText('total:1')).toBeVisible()
  expect(document.body.textContent ?? '').not.toContain('Stale Harbor Point')
  expect(reportPageMocks.state.executeRequests.map((request) => request.cursor ?? null)).toEqual([null, 'cursor-2', null])
})

test('opens catalog lookup targets through the default reporting config and preserves the current report as back target', async () => {
  await page.viewport(1280, 900)

  reportPageMocks.state.definition = buildLookupOnlyDefinition({
    fieldCode: 'property',
    label: 'Property',
    dataType: 'Guid',
    isRequired: true,
    lookup: {
      kind: 'catalog',
      catalogType: 'pm.property',
    },
  })
  reportPageMocks.state.variants = []

  const lookupStore = createScenarioLookupStore({
    kind: 'catalog',
    items: [{ id: 'property-1', label: 'Riverfront Tower' }],
  })

  configureDefaultLookupScenario({ lookupStore })

  const { router, view } = await renderReportPage('/reports/pm.occupancy.summary?src=portfolio')
  const reportRoute = router.currentRoute.value.fullPath

  lookupInput().value = 'river'
  lookupInput().dispatchEvent(new Event('input', { bubbles: true }))
  await flushUi()

  expect(lookupStore.searchCatalog).toHaveBeenCalledWith('pm.property', 'river', undefined)

  lookupAction('select-first').click()
  await flushUi()
  await expect.element(view.getByText('lookup-value:Riverfront Tower')).toBeVisible()

  lookupAction('open').click()
  await flushUi()

  expect(router.currentRoute.value.fullPath).toBe(
    withBackTarget('/catalogs/pm.property/property-1', reportRoute),
  )
})

test('opens coa lookup targets through the default reporting config and preserves the current report as back target', async () => {
  await page.viewport(1280, 900)

  reportPageMocks.state.definition = buildLookupOnlyDefinition({
    fieldCode: 'account',
    label: 'Account',
    dataType: 'Guid',
    isRequired: true,
    lookup: {
      kind: 'coa',
    },
  })
  reportPageMocks.state.variants = []

  const lookupStore = createScenarioLookupStore({
    kind: 'coa',
    items: [{ id: 'cash-id', label: '1100 Cash' }],
  })

  configureDefaultLookupScenario({ lookupStore })

  const { router, view } = await renderReportPage('/reports/pm.occupancy.summary')
  const reportRoute = router.currentRoute.value.fullPath

  lookupInput().value = 'cash'
  lookupInput().dispatchEvent(new Event('input', { bubbles: true }))
  await flushUi()

  expect(lookupStore.searchCoa).toHaveBeenCalledWith('cash')

  lookupAction('select-first').click()
  await flushUi()
  await expect.element(view.getByText('lookup-value:1100 Cash')).toBeVisible()

  lookupAction('open').click()
  await flushUi()

  expect(router.currentRoute.value.fullPath).toBe(
    withBackTarget('/admin/chart-of-accounts?panel=edit&id=cash-id', reportRoute),
  )
})

test('opens document lookup targets through the default reporting config after resolving the matching document type', async () => {
  await page.viewport(1280, 900)

  reportPageMocks.state.definition = buildLookupOnlyDefinition({
    fieldCode: 'document',
    label: 'Document',
    dataType: 'Guid',
    isRequired: true,
    lookup: {
      kind: 'document',
      documentTypes: ['pm.invoice', 'pm.credit_note'],
    },
  })
  reportPageMocks.state.variants = []

  const lookupStore = createScenarioLookupStore({
    kind: 'document',
    items: [{ id: 'doc-1', label: 'Credit Memo CM-001' }],
  })

  const loadDocumentItemsByIds = vi.fn(async (documentTypes: string[], ids: string[]) => {
    expect(documentTypes).toEqual(['pm.invoice', 'pm.credit_note'])
    expect(ids).toEqual(['doc-1'])

    return [{ id: 'doc-1', label: 'Credit Memo CM-001', documentType: 'pm.credit_note' }]
  })

  configureDefaultLookupScenario({
    lookupStore,
    loadDocumentItemsByIds,
  })

  const { router, view } = await renderReportPage('/reports/pm.occupancy.summary?ctx=journal')
  const reportRoute = router.currentRoute.value.fullPath

  lookupInput().value = 'credit'
  lookupInput().dispatchEvent(new Event('input', { bubbles: true }))
  await flushUi()

  expect(lookupStore.searchDocuments).toHaveBeenCalledWith(['pm.invoice', 'pm.credit_note'], 'credit')

  lookupAction('select-first').click()
  await flushUi()
  await expect.element(view.getByText('lookup-value:Credit Memo CM-001')).toBeVisible()

  lookupAction('open').click()
  await flushUi()

  expect(loadDocumentItemsByIds).toHaveBeenCalledWith(
    ['pm.invoice', 'pm.credit_note'],
    ['doc-1'],
  )
  expect(router.currentRoute.value.fullPath).toBe(
    withBackTarget('/documents/pm.credit_note/doc-1', reportRoute),
  )
})

test('creates a downloadable blob url, clicks the transient anchor, and restores button state after export completes', async () => {
  await page.viewport(1280, 900)

  let resolveExport!: (value: { blob: Blob; fileName: string }) => void
  reportPageMocks.exportReportXlsx.mockImplementationOnce(async () => {
    return await new Promise((resolve) => {
      resolveExport = resolve
    })
  })

  const createElement = document.createElement.bind(document)
  const anchor = createElement('a')
  const clickSpy = vi.spyOn(anchor, 'click').mockImplementation(() => {})
  const removeSpy = vi.spyOn(anchor, 'remove')
  const createElementSpy = vi.spyOn(document, 'createElement').mockImplementation(((tagName: string) => {
    if (tagName.toLowerCase() === 'a') return anchor
    return createElement(tagName)
  }) as typeof document.createElement)
  const createObjectUrlSpy = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:occupancy-export')
  const revokeObjectUrlSpy = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {})

  try {
    await renderReportPage()

    const downloadButton = document.querySelector('button[title="Download"]') as HTMLButtonElement | null
    if (!(downloadButton instanceof HTMLButtonElement)) throw new Error('Download button not found.')
    expect(downloadButton.disabled).toBe(false)

    downloadButton.click()
    await vi.waitFor(() => {
      expect(downloadButton.disabled).toBe(true)
    })

    resolveExport({
      blob: new Blob(['xlsx-bytes']),
      fileName: 'occupancy-summary.xlsx',
    })
    await flushUi()

    expect(createObjectUrlSpy).toHaveBeenCalledTimes(1)
    expect(anchor.href).toBe('blob:occupancy-export')
    expect(anchor.download).toBe('occupancy-summary.xlsx')
    expect(clickSpy).toHaveBeenCalledTimes(1)
    expect(removeSpy).toHaveBeenCalledTimes(1)
    expect(revokeObjectUrlSpy).toHaveBeenCalledWith('blob:occupancy-export')
    await vi.waitFor(() => {
      expect(downloadButton.disabled).toBe(false)
    })
  } finally {
    createElementSpy.mockRestore()
    createObjectUrlSpy.mockRestore()
    revokeObjectUrlSpy.mockRestore()
  }
})

test('auto-runs a default variant, appends paged responses, and keeps variant context in the sheet', async () => {
  await page.viewport(1280, 900)
  reportPageMocks.state.variants = [
    {
      ...clone(baseVariants[1]),
      isDefault: true,
    },
  ]
  reportPageMocks.state.executeResponses = [
    buildResponse({ rows: ['North Square'], total: 2, hasMore: true, nextCursor: 'cursor-2' }),
    buildResponse({ rows: ['Harbor Point'], total: 2, hasMore: false, nextCursor: null }),
  ]

  const { view } = await renderReportPage()

  expect(reportPageMocks.executeReport).toHaveBeenCalledTimes(1)
  expect(reportPageMocks.state.executeRequests[0]).toMatchObject({
    parameters: {
      as_of_utc: '2026-01-31',
    },
    filters: {
      property: {
        value: '22222222-2222-2222-2222-222222222222',
      },
      status: {
        value: 'posted',
      },
    },
    limit: 2,
  })

  await expect.element(view.getByText('lookup-value:North Square')).toBeVisible()
  await expect.element(view.getByText('rows:1')).toBeVisible()
  await expect.element(view.getByText('loaded:1')).toBeVisible()
  await expect.element(view.getByText('total:2')).toBeVisible()
  await expect.element(view.getByText('variant:audit-view')).toBeVisible()

  await view.getByRole('button', { name: 'Load more' }).click()
  await flushUi()

  expect(reportPageMocks.executeReport).toHaveBeenCalledTimes(2)
  expect(reportPageMocks.state.executeRequests[1]).toMatchObject({
    cursor: 'cursor-2',
    limit: 2,
  })
  await expect.element(view.getByText('rows:2')).toBeVisible()
  await expect.element(view.getByText('loaded:2')).toBeVisible()
  await expect.element(view.getByText('show-end:true')).toBeVisible()
})

test('creates, loads, and deletes variants through the composer and dialogs', async () => {
  await page.viewport(1280, 900)
  reportPageMocks.state.executeResponses = [
    buildResponse({ rows: ['North Square'], total: 1 }),
  ]

  const { router, view } = await renderReportPage()

  clickHeaderButtonByTitle('Composer')
  await expect.element(view.getByTestId('report-composer-panel')).toBeVisible()

  const variantSelect = view.getByTestId('composer-variant-select').element() as HTMLSelectElement
  variantSelect.value = 'audit-view'
  variantSelect.dispatchEvent(new Event('input', { bubbles: true }))
  variantSelect.dispatchEvent(new Event('change', { bubbles: true }))
  await flushUi()

  await view.getByRole('button', { name: 'Composer load variant' }).click()
  await flushUi()

  expect(reportPageMocks.executeReport).toHaveBeenCalledTimes(1)
  expect(router.currentRoute.value.query.variant).toBe('audit-view')
  await expect.element(view.getByText('lookup-value:North Square')).toBeVisible()

  await view.getByRole('button', { name: 'Composer create variant' }).click()
  await expect.element(view.getByTestId('stub-dialog')).toBeVisible()

  const nameInput = document.querySelector('[data-testid="stub-dialog"] input[placeholder="Month-end ledger"]')
  if (!(nameInput instanceof HTMLInputElement)) throw new Error('Variant name input not found.')
  nameInput.value = 'Desk View'
  nameInput.dispatchEvent(new Event('input', { bubbles: true }))
  nameInput.dispatchEvent(new Event('change', { bubbles: true }))
  await flushUi()

  await view.getByTestId('stub-switch').click()
  await view.getByRole('button', { name: 'Dialog confirm:Create' }).click()
  await flushUi()

  expect(reportPageMocks.saveReportVariant).toHaveBeenCalledWith(
    'pm.occupancy.summary',
    'desk-view',
    expect.objectContaining({
      name: 'Desk View',
      isDefault: true,
    }),
  )
  expect(router.currentRoute.value.query.variant).toBe('desk-view')

  const updatedSelect = view.getByTestId('composer-variant-select').element() as HTMLSelectElement
  updatedSelect.value = 'audit-view'
  updatedSelect.dispatchEvent(new Event('input', { bubbles: true }))
  updatedSelect.dispatchEvent(new Event('change', { bubbles: true }))
  await flushUi()

  await view.getByRole('button', { name: 'Composer delete variant' }).click()
  await expect.element(view.getByTestId('stub-dialog')).toBeVisible()
  await view.getByRole('button', { name: 'Dialog confirm:Delete' }).click()
  await flushUi()

  expect(reportPageMocks.deleteReportVariant).toHaveBeenCalledWith('pm.occupancy.summary', 'audit-view')
  expect(reportPageMocks.state.deletedVariants).toEqual(['audit-view'])
  expect(reportPageMocks.state.variants.map((variant) => String(variant.variantCode))).toEqual(['portfolio-view', 'desk-view'])
})

test('ignores malformed saved session state and falls back to a fresh execution', async () => {
  await page.viewport(1280, 900)

  const routeStateKey = reportPageStateKey({ variant: 'audit-view' })
  sessionStorage.setItem(`ngb.report.page.execution:${routeStateKey}`, '{broken-json')
  sessionStorage.setItem(`ngb.report.page.scroll:${routeStateKey}`, 'not-a-number')
  reportPageMocks.state.executeResponses = [
    buildResponse({ rows: ['Recovered fresh run'], total: 1 }),
  ]

  const { view } = await renderReportPage('/reports/pm.occupancy.summary?variant=audit-view')
  await flushUi()

  expect(reportPageMocks.executeReport).toHaveBeenCalledTimes(1)
  await expect.element(view.getByText('rows:1')).toBeVisible()
  await expect.element(view.getByText('restored-scroll-top:0')).toBeVisible()
  await expect.element(view.getByText('variant:audit-view')).toBeVisible()
  await expect.element(view.getByText('lookup-value:North Square')).toBeVisible()
})

test('restores a saved execution snapshot and scroll position without re-running the report', async () => {
  await page.viewport(1280, 900)

  const routeStateKey = reportPageStateKey({ variant: 'audit-view' })
  saveReportPageExecutionSnapshot(routeStateKey, buildResponse({
    rows: ['Restored North Square', 'Restored Harbor Point'],
    total: 2,
    hasMore: false,
  }), ['cursor-1'])
  saveReportPageScrollTop(routeStateKey, 240)

  const { view } = await renderReportPage('/reports/pm.occupancy.summary?variant=audit-view')
  await flushUi()
  await flushUi()

  expect(reportPageMocks.executeReport).not.toHaveBeenCalled()
  await expect.element(view.getByText('lookup-value:North Square')).toBeVisible()
  await expect.element(view.getByText('rows:2')).toBeVisible()
  await expect.element(view.getByText('total:2')).toBeVisible()
  await expect.element(view.getByText('variant:audit-view')).toBeVisible()
  await expect.element(view.getByText('restored-scroll-top:240')).toBeVisible()
})

test('navigates back to the prior report in the source trail when no outer back target is present', async () => {
  await page.viewport(1280, 900)

  const initialUrl = `/reports/pm.occupancy.summary?variant=audit-view&ctx=${encodeReportRouteContextParam({
    reportCode: 'pm.occupancy.summary',
    reportName: 'Occupancy Summary',
    request: {
      parameters: {
        as_of_utc: '2026-01-31',
      },
      filters: {
        property: {
          value: '22222222-2222-2222-2222-222222222222',
        },
        status: {
          value: 'posted',
        },
      },
      layout: null,
      offset: 0,
      limit: 2,
      cursor: null,
      variantCode: 'audit-view',
    },
  })}&src=${encodeReportSourceTrailParam({
    items: [
      {
        reportCode: 'pm.portfolio.home',
        reportName: 'Portfolio Home',
        request: {
          parameters: null,
          filters: null,
          layout: null,
          offset: 0,
          limit: 500,
          cursor: null,
          variantCode: null,
        },
      },
    ],
  })}`

  const { router, view } = await renderReportPage(initialUrl)
  const originalHistoryLength = window.history.length

  Object.defineProperty(window.history, 'length', {
    configurable: true,
    value: 1,
  })

  try {
    await expect.element(view.getByText('variant:audit-view')).toBeVisible()
    await view.getByRole('button', { name: 'Back' }).click()

    await expect.poll(() => router.currentRoute.value.params.reportCode).toBe('pm.portfolio.home')
  } finally {
    Object.defineProperty(window.history, 'length', {
      configurable: true,
      value: originalHistoryLength,
    })
  }

  expect(decodeReportRouteContextParam(router.currentRoute.value.query.ctx)).toEqual({
    reportCode: 'pm.portfolio.home',
    reportName: 'Portfolio Home',
    request: {
      parameters: null,
      filters: null,
      layout: null,
      offset: 0,
      limit: 500,
      cursor: null,
      variantCode: null,
    },
  })
  expect(router.currentRoute.value.query.src).toBeUndefined()
})

test('passes a nested report back target into the sheet when the route has an outer back target', async () => {
  await page.viewport(1280, 900)

  const initialUrl = `/reports/pm.occupancy.summary?variant=audit-view&ctx=${encodeReportRouteContextParam({
    reportCode: 'pm.occupancy.summary',
    reportName: 'Occupancy Summary',
    request: {
      parameters: {
        as_of_utc: '2026-01-31',
      },
      filters: {
        property: {
          value: '22222222-2222-2222-2222-222222222222',
        },
        status: {
          value: 'posted',
        },
      },
      layout: null,
      offset: 0,
      limit: 2,
      cursor: null,
      variantCode: 'audit-view',
    },
  })}&src=${encodeReportSourceTrailParam({
    items: [
      {
        reportCode: 'pm.portfolio.home',
        reportName: 'Portfolio Home',
        request: {
          parameters: null,
          filters: null,
          layout: null,
          offset: 0,
          limit: 500,
          cursor: null,
          variantCode: null,
        },
      },
    ],
  })}&back=${encodeBackTarget('/workspace/home')}`

  const { view } = await renderReportPage(initialUrl)

  await expect.element(view.getByText('source-trail-count:1')).toBeVisible()

  const backTargetText = Array.from(document.querySelectorAll('[data-testid="stub-report-sheet"] div'))
    .map((entry) => entry.textContent ?? '')
    .find((entry) => entry.startsWith('back-target:'))
  const nestedBackTarget = String(backTargetText ?? '').slice('back-target:'.length)
  const nestedBackParam = new URL(nestedBackTarget, 'https://example.test').searchParams.get('back')

  expect(nestedBackTarget.startsWith('/reports/pm.occupancy.summary')).toBe(true)
  expect(decodeBackTarget(nestedBackParam)).toBe('/workspace/home')
})

test('shows run and export errors, then recovers on a successful rerun', async () => {
  await page.viewport(1280, 900)

  reportPageMocks.executeReport.mockImplementationOnce(async () => {
    throw new Error('Run failed hard')
  })
  reportPageMocks.exportReportXlsx.mockImplementationOnce(async () => {
    throw new Error('Export failed hard')
  })
  reportPageMocks.state.executeResponses = [
    buildResponse({ rows: ['Riverfront Tower'], total: 1 }),
  ]

  const { view } = await renderReportPage()

  const lookupInput = document.querySelector('[data-testid="stub-lookup"] input')
  if (!(lookupInput instanceof HTMLInputElement)) throw new Error('Lookup input not found.')
  lookupInput.value = 'tower'
  lookupInput.dispatchEvent(new Event('input', { bubbles: true }))
  await flushUi()

  const lookupButtons = document.querySelectorAll('[data-testid="stub-lookup"] button')
  ;(lookupButtons[0] as HTMLButtonElement).click()
  await expect.element(view.getByText('lookup-value:Riverfront Tower')).toBeVisible()

  const dateInput = document.querySelector('input[type="date"][data-testid^="stub-date-picker"]')
  if (!(dateInput instanceof HTMLInputElement)) throw new Error('Date input not found.')
  dateInput.value = '2026-04-30'
  dateInput.dispatchEvent(new Event('input', { bubbles: true }))
  dateInput.dispatchEvent(new Event('change', { bubbles: true }))
  await flushUi()

  clickHeaderButtonByTitle('Run')
  await flushUi()

  await expect.element(view.getByText('Run failed hard')).toBeVisible()
  expect(reportPageMocks.executeReport).toHaveBeenCalledTimes(1)

  clickHeaderButtonByTitle('Run')
  await flushUi()

  expect(reportPageMocks.executeReport).toHaveBeenCalledTimes(2)
  await expect.element(view.getByText('rows:1')).toBeVisible()
  expect(document.body.textContent ?? '').not.toContain('Run failed hard')

  clickHeaderButtonByTitle('Download')
  await flushUi()

  expect(reportPageMocks.exportReportXlsx).toHaveBeenCalledTimes(1)
  await expect.element(view.getByText('Export failed hard')).toBeVisible()
})

test('surfaces edit-variant dialog errors and persists the rename on retry', async () => {
  await page.viewport(1280, 900)
  reportPageMocks.state.executeResponses = [
    buildResponse({ rows: ['North Square'], total: 1 }),
  ]

  const { router, view } = await renderReportPage('/reports/pm.occupancy.summary?variant=audit-view')

  clickHeaderButtonByTitle('Composer')
  await expect.element(view.getByTestId('report-composer-panel')).toBeVisible()

  reportPageMocks.saveReportVariant.mockImplementationOnce(async () => {
    throw new Error('Variant rename failed')
  })

  await view.getByRole('button', { name: 'Composer edit variant' }).click()
  await expect.element(view.getByTestId('stub-dialog')).toBeVisible()

  const nameInput = document.querySelector('[data-testid="stub-dialog"] input[placeholder="Month-end ledger"]')
  if (!(nameInput instanceof HTMLInputElement)) throw new Error('Variant name input not found.')
  nameInput.value = 'Audit View Revised'
  nameInput.dispatchEvent(new Event('input', { bubbles: true }))
  nameInput.dispatchEvent(new Event('change', { bubbles: true }))
  await flushUi()

  await view.getByRole('button', { name: 'Dialog confirm:Save' }).click()
  await flushUi()

  expect(reportPageMocks.saveReportVariant).toHaveBeenCalledTimes(1)
  await expect.element(view.getByText('Variant rename failed')).toBeVisible()
  expect(router.currentRoute.value.query.variant).toBe('audit-view')

  await view.getByRole('button', { name: 'Dialog confirm:Save' }).click()
  await flushUi()

  expect(reportPageMocks.saveReportVariant).toHaveBeenCalledTimes(2)
  expect(document.querySelector('[data-testid="stub-dialog"]')).toBeNull()
  expect(reportPageMocks.state.variants.find((variant) => String(variant.variantCode) === 'audit-view')).toMatchObject({
    name: 'Audit View Revised',
  })
  expect(router.currentRoute.value.query.variant).toBe('audit-view')
})

test('surfaces save-current-variant errors and recovers on retry', async () => {
  await page.viewport(1280, 900)
  reportPageMocks.state.executeResponses = [
    buildResponse({ rows: ['North Square'], total: 1 }),
  ]

  const { router, view } = await renderReportPage('/reports/pm.occupancy.summary?variant=audit-view')

  clickHeaderButtonByTitle('Composer')
  await expect.element(view.getByTestId('report-composer-panel')).toBeVisible()

  reportPageMocks.saveReportVariant.mockImplementationOnce(async () => {
    throw new Error('Save current variant failed')
  })

  await view.getByRole('button', { name: 'Composer save variant' }).click()
  await flushUi()

  expect(reportPageMocks.saveReportVariant).toHaveBeenCalledTimes(1)
  await expect.element(view.getByText('Save current variant failed')).toBeVisible()
  expect(router.currentRoute.value.query.variant).toBe('audit-view')

  await view.getByRole('button', { name: 'Composer save variant' }).click()
  await flushUi()

  expect(reportPageMocks.saveReportVariant).toHaveBeenCalledTimes(2)
  expect(router.currentRoute.value.query.variant).toBe('audit-view')
  expect(document.body.textContent ?? '').not.toContain('Save current variant failed')
})

test('keeps the delete dialog open on failure, then deletes the active variant and resets to default', async () => {
  await page.viewport(1280, 900)
  reportPageMocks.state.executeResponses = [
    buildResponse({ rows: ['North Square'], total: 1 }),
  ]

  const { router, view } = await renderReportPage('/reports/pm.occupancy.summary?variant=audit-view')

  clickHeaderButtonByTitle('Composer')
  await expect.element(view.getByTestId('report-composer-panel')).toBeVisible()

  reportPageMocks.deleteReportVariant.mockImplementationOnce(async () => {
    throw new Error('Delete variant failed')
  })

  await view.getByRole('button', { name: 'Composer delete variant' }).click()
  await expect.element(view.getByTestId('stub-dialog')).toBeVisible()
  await view.getByRole('button', { name: 'Dialog confirm:Delete' }).click()
  await flushUi()

  expect(reportPageMocks.deleteReportVariant).toHaveBeenCalledTimes(1)
  expect(reportPageMocks.state.deletedVariants).toEqual([])
  await expect.element(view.getByText('Delete variant failed')).toBeVisible()
  await expect.element(view.getByTestId('stub-dialog')).toBeVisible()

  await view.getByRole('button', { name: 'Dialog confirm:Delete' }).click()
  await flushUi()

  expect(reportPageMocks.deleteReportVariant).toHaveBeenCalledTimes(2)
  expect(reportPageMocks.state.deletedVariants).toEqual(['audit-view'])
  expect(router.currentRoute.value.query.variant).toBe('portfolio-view')
  await expect.element(view.getByText('rows:0')).toBeVisible()
  await expect.element(view.getByText('variant:portfolio-view')).toBeVisible()
})

test('resets to the definition draft when no default variant exists', async () => {
  await page.viewport(1280, 900)
  reportPageMocks.state.variants = clone(baseVariants).map((variant) => ({
    ...variant,
    isDefault: false,
  }))
  reportPageMocks.state.executeResponses = [
    buildResponse({ rows: ['North Square'], total: 1 }),
  ]

  const { router, view } = await renderReportPage()

  clickHeaderButtonByTitle('Composer')
  await expect.element(view.getByTestId('report-composer-panel')).toBeVisible()

  const variantSelect = view.getByTestId('composer-variant-select').element() as HTMLSelectElement
  variantSelect.value = 'audit-view'
  variantSelect.dispatchEvent(new Event('input', { bubbles: true }))
  variantSelect.dispatchEvent(new Event('change', { bubbles: true }))
  await flushUi()

  await view.getByRole('button', { name: 'Composer load variant' }).click()
  await flushUi()

  expect(router.currentRoute.value.query.variant).toBe('audit-view')
  await expect.element(view.getByText('rows:1')).toBeVisible()
  await expect.element(view.getByText('variant:audit-view')).toBeVisible()

  await view.getByRole('button', { name: 'Composer reset variant' }).click()
  await flushUi()

  expect(router.currentRoute.value.query.variant).toBeUndefined()
  await expect.element(view.getByText('rows:0')).toBeVisible()
  await expect.element(view.getByText('variant:none')).toBeVisible()
})

test('publishes save-current command palette action for the active variant and executes it', async () => {
  await page.viewport(1280, 900)
  reportPageMocks.state.executeResponses = [
    buildResponse({ rows: ['North Square'], total: 1 }),
  ]

  const { router } = await renderReportPage('/reports/pm.occupancy.summary?variant=audit-view')
  const resolver = reportPageMocks.state.commandPaletteResolver
  if (!resolver) throw new Error('Command palette resolver was not registered.')

  const context = resolver()
  expect(context?.entityType).toBe('report')
  expect(context?.title).toBe('Occupancy Summary')
  expect(context?.actions.map((action) => action.key)).toContain('current:save-variant')
  expect(context?.actions.map((action) => action.key)).not.toContain('current:load-variant')

  const saveAction = context?.actions.find((action) => action.key === 'current:save-variant')
  if (!saveAction?.perform) throw new Error('Save action not found.')

  await saveAction.perform()
  await flushUi()

  expect(reportPageMocks.saveReportVariant).toHaveBeenCalledTimes(1)
  expect(reportPageMocks.saveReportVariant).toHaveBeenCalledWith(
    'pm.occupancy.summary',
    'audit-view',
    expect.objectContaining({
      name: 'Audit View',
    }),
  )
  expect(router.currentRoute.value.query.variant).toBe('audit-view')
})

test('publishes load-selected command palette action when selected variant differs and executes it', async () => {
  await page.viewport(1280, 900)
  reportPageMocks.state.executeResponses = [
    buildResponse({ rows: ['North Square'], total: 1 }),
  ]

  const { router, view } = await renderReportPage()
  clickHeaderButtonByTitle('Composer')
  await expect.element(view.getByTestId('report-composer-panel')).toBeVisible()

  const variantSelect = view.getByTestId('composer-variant-select').element() as HTMLSelectElement
  variantSelect.value = 'audit-view'
  variantSelect.dispatchEvent(new Event('input', { bubbles: true }))
  variantSelect.dispatchEvent(new Event('change', { bubbles: true }))
  await flushUi()

  const resolver = reportPageMocks.state.commandPaletteResolver
  if (!resolver) throw new Error('Command palette resolver was not registered.')

  const context = resolver()
  expect(context?.actions.map((action) => action.key)).toEqual(
    expect.arrayContaining(['current:save-variant', 'current:load-variant']),
  )

  const loadAction = context?.actions.find((action) => action.key === 'current:load-variant')
  if (!loadAction?.perform) throw new Error('Load action not found.')

  await loadAction.perform()
  await flushUi()

  expect(reportPageMocks.executeReport).toHaveBeenCalledTimes(1)
  expect(router.currentRoute.value.query.variant).toBe('audit-view')
  await expect.element(view.getByText('lookup-value:North Square')).toBeVisible()
  await expect.element(view.getByText('variant:audit-view')).toBeVisible()
})
