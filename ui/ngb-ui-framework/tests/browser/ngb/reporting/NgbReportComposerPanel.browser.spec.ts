import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'

vi.mock('../../../../src/ngb/metadata/NgbFilterFieldControl.vue', async () => {
  const { defineComponent, h } = await vi.importActual<typeof import('vue')>('vue')

  return {
    default: defineComponent({
      props: {
        field: {
          type: Object,
          required: true,
        },
        state: {
          type: Object,
          required: true,
        },
        lookupItems: {
          type: Array,
          default: () => [],
        },
        showOpen: {
          type: Boolean,
          default: false,
        },
        showClear: {
          type: Boolean,
          default: false,
        },
        allowIncludeDescendants: {
          type: Boolean,
          default: false,
        },
      },
      emits: ['lookup-query', 'update:items', 'update:raw', 'update:includeDescendants', 'open'],
      setup(props, { emit }) {
        return () => h('div', { 'data-testid': `report-filter-control-${props.field.fieldCode}` }, [
          h('div', `raw:${props.state.raw ?? ''}`),
          h('div', `items:${(props.state.items ?? []).map((item: { label?: string }) => String(item.label ?? '')).join('|') || 'none'}`),
          h('input', {
            'aria-label': `filter-query-${props.field.fieldCode}`,
            onInput: (event: Event) => emit('lookup-query', (event.target as HTMLInputElement).value),
          }),
          h('button', {
            type: 'button',
            onClick: () => emit('update:raw', `raw:${props.field.fieldCode}`),
          }, `Set raw ${props.field.fieldCode}`),
          (props.lookupItems?.length ?? 0) > 0
            ? h('button', {
              type: 'button',
              onClick: () => emit('update:items', props.field.isMulti ? props.lookupItems.slice(0, 2) : [props.lookupItems[0]]),
            }, `Select ${props.field.fieldCode}`)
            : null,
          props.showOpen
            ? h('button', {
              type: 'button',
              onClick: () => emit('open'),
            }, `Open ${props.field.fieldCode}`)
            : null,
          props.showClear
            ? h('button', {
              type: 'button',
              onClick: () => emit('update:items', []),
            }, `Clear ${props.field.fieldCode}`)
            : null,
          props.allowIncludeDescendants
            && !!props.field.supportsIncludeDescendants
            && (props.state.items?.length ?? 0) > 0
            ? h('button', {
              type: 'button',
              onClick: () => emit('update:includeDescendants', !props.state.includeDescendants),
            }, `Include descendants:${String(!!props.state.includeDescendants)}`)
            : null,
        ])
      },
    }),
  }
})

import NgbReportComposerPanel from '../../../../src/ngb/reporting/NgbReportComposerPanel.vue'
import { createComposerDraft } from '../../../../src/ngb/reporting/composer'
import { configureNgbReporting } from '../../../../src/ngb/reporting/config'
import { withBackTarget } from '../../../../src/ngb/router/backNavigation'
import {
  ReportAggregationKind,
  ReportFieldKind,
  ReportSortDirection,
  ReportTimeGrain,
  type ReportComposerDraft,
  type ReportDefinitionDto,
} from '../../../../src/ngb/reporting/types'

const composerDefinition: ReportDefinitionDto = {
  reportCode: 'pm.occupancy.summary',
  name: 'Occupancy Summary',
  description: 'Portfolio occupancy by property and manager.',
  mode: 2,
  capabilities: {
    allowsFilters: true,
    allowsRowGroups: true,
    allowsColumnGroups: true,
    allowsMeasures: true,
    allowsDetailFields: true,
    allowsSorting: true,
    allowsShowDetails: true,
    allowsSubtotals: true,
    allowsGrandTotals: true,
    allowsVariants: true,
    allowsXlsxExport: true,
  },
  dataset: {
    datasetCode: 'pm.occupancy.summary',
    fields: [
      {
        code: 'property',
        label: 'Property',
        dataType: 'String',
        kind: 1,
        isGroupable: true,
        isSelectable: true,
        isSortable: true,
      },
      {
        code: 'manager',
        label: 'Manager',
        dataType: 'String',
        kind: ReportFieldKind.Dimension,
        isGroupable: true,
        isSelectable: true,
        isSortable: true,
      },
      {
        code: 'period',
        label: 'Period',
        dataType: 'Date',
        kind: ReportFieldKind.Time,
        isGroupable: true,
        isSelectable: true,
        isSortable: true,
        supportedTimeGrains: [ReportTimeGrain.Month, ReportTimeGrain.Quarter],
      },
    ],
    measures: [
      {
        code: 'total_units',
        label: 'Units',
        dataType: 'Decimal',
        supportedAggregations: [1],
      },
      {
        code: 'occupied_units',
        label: 'Occupied',
        dataType: 'Decimal',
        supportedAggregations: [1],
      },
    ],
  },
  defaultLayout: {
    detailFields: [],
    measures: [
      { measureCode: 'total_units', aggregation: 1 },
    ],
    showDetails: true,
    showSubtotals: true,
    showGrandTotals: true,
  },
  filters: [
    {
      fieldCode: 'property',
      label: 'Property',
      dataType: 'String',
      lookup: {
        kind: 'catalog',
        catalogType: 'pm.property',
      },
      supportsIncludeDescendants: true,
      defaultIncludeDescendants: true,
    },
    {
      fieldCode: 'status',
      label: 'Status',
      dataType: 'String',
      options: [
        { value: 'open', label: 'Open' },
        { value: 'closed', label: 'Closed' },
      ],
    },
  ],
  parameters: [],
  presentation: {
    initialPageSize: 100,
    rowNoun: 'property',
    emptyStateMessage: 'Run the report to review current portfolio occupancy.',
  },
}

function createHarnessDraft(withCollections: boolean): ReportComposerDraft {
  const draft = createComposerDraft(composerDefinition)

  if (!withCollections) return draft

  draft.rowGroups = [
    { groupKey: null, fieldCode: 'property', timeGrain: null },
    { groupKey: null, fieldCode: 'manager', timeGrain: null },
  ]
  draft.measures = [
    { measureCode: 'total_units', aggregation: ReportAggregationKind.Sum },
    { measureCode: 'occupied_units', aggregation: ReportAggregationKind.Sum },
  ]
  draft.sorts = [
    {
      fieldCode: 'property',
      direction: ReportSortDirection.Asc,
      timeGrain: null,
      appliesToColumnAxis: false,
      groupKey: null,
    },
    {
      fieldCode: 'manager',
      direction: ReportSortDirection.Desc,
      timeGrain: null,
      appliesToColumnAxis: false,
      groupKey: null,
    },
  ]

  return draft
}

function buildCompactComposerDefinition(): ReportDefinitionDto {
  return {
    ...composerDefinition,
    filters: [],
    parameters: [],
    capabilities: {
      allowsFilters: false,
      allowsRowGroups: false,
      allowsColumnGroups: false,
      allowsMeasures: true,
      allowsDetailFields: false,
      allowsSorting: false,
      allowsShowDetails: false,
      allowsSubtotals: false,
      allowsSeparateRowSubtotals: false,
      allowsGrandTotals: false,
      allowsVariants: false,
      allowsXlsxExport: false,
    },
  }
}

beforeEach(() => {
  configureNgbReporting({
    useLookupStore: () => ({
      searchCatalog: async () => [],
      searchCoa: async () => [],
      searchDocuments: async () => [],
      ensureCatalogLabels: async () => undefined,
      ensureCoaLabels: async () => undefined,
      ensureAnyDocumentLabels: async () => undefined,
      labelForCatalog: (_catalogType, id) => String(id),
      labelForCoa: (id) => String(id),
      labelForAnyDocument: (_documentTypes, id) => String(id),
    }),
    resolveLookupTarget: async ({ hint, value, routeFullPath }) => {
      const id = String((value as { id?: unknown } | null)?.id ?? '').trim()
      if (hint?.kind !== 'catalog' || !hint.catalogType || id.length === 0) return null
      return withBackTarget(`/lookups/catalog/${hint.catalogType}/${id}`, routeFullPath)
    },
  })
})

const ReportComposerHarness = defineComponent({
  props: {
    withCollections: {
      type: Boolean,
      default: false,
    },
    running: {
      type: Boolean,
      default: false,
    },
    variantDisabled: {
      type: Boolean,
      default: false,
    },
    editVariantDisabled: {
      type: Boolean,
      default: false,
    },
    saveVariantDisabled: {
      type: Boolean,
      default: false,
    },
    deleteVariantDisabled: {
      type: Boolean,
      default: false,
    },
    resetVariantDisabled: {
      type: Boolean,
      default: false,
    },
    loadVariantDisabled: {
      type: Boolean,
      default: false,
    },
  },
  setup(props) {
    const draft = ref(createHarnessDraft(props.withCollections))
    const runCount = ref(0)
    const closeCount = ref(0)
    const createVariantCount = ref(0)
    const editVariantCount = ref(0)
    const saveVariantCount = ref(0)
    const deleteVariantCount = ref(0)
    const resetVariantCount = ref(0)
    const loadVariantCount = ref(0)
    const selectedVariantCode = ref('')

    return () => h('div', {
        style: 'width: 440px; max-width: 440px; height: 780px; display: flex; flex-direction: column; overflow: hidden;',
      }, [
      h(NgbReportComposerPanel, {
        definition: composerDefinition,
        modelValue: draft.value,
        lookupItemsByFilterCode: {},
        running: props.running,
        variantDisabled: props.variantDisabled,
        editVariantDisabled: props.editVariantDisabled,
        saveVariantDisabled: props.saveVariantDisabled,
        deleteVariantDisabled: props.deleteVariantDisabled,
        resetVariantDisabled: props.resetVariantDisabled,
        loadVariantDisabled: props.loadVariantDisabled,
        variantOptions: [
          { value: '', label: 'Definition default' },
          { value: 'month_end', label: 'Month-end snapshot' },
        ],
        selectedVariantCode: selectedVariantCode.value,
        variantSummary: 'Using the report definition default layout and filters.',
        onRun: () => {
          runCount.value += 1
        },
        onClose: () => {
          closeCount.value += 1
        },
        'onUpdate:modelValue': (value: unknown) => {
          draft.value = value as ReportComposerDraft
        },
        'onUpdate:selectedVariantCode': (value: string) => {
          selectedVariantCode.value = value
        },
        onCreateVariant: () => {
          createVariantCount.value += 1
        },
        onEditVariant: () => {
          editVariantCount.value += 1
        },
        onSaveVariant: () => {
          saveVariantCount.value += 1
        },
        onDeleteVariant: () => {
          deleteVariantCount.value += 1
        },
        onResetVariant: () => {
          resetVariantCount.value += 1
        },
        onLoadVariant: () => {
          loadVariantCount.value += 1
        },
      }),
      h('pre', { 'data-testid': 'composer-draft-state' }, JSON.stringify(draft.value)),
      h('div', { 'data-testid': 'composer-run-count' }, String(runCount.value)),
      h('div', { 'data-testid': 'composer-close-count' }, String(closeCount.value)),
      h('div', { 'data-testid': 'composer-create-variant-count' }, String(createVariantCount.value)),
      h('div', { 'data-testid': 'composer-edit-variant-count' }, String(editVariantCount.value)),
      h('div', { 'data-testid': 'composer-save-variant-count' }, String(saveVariantCount.value)),
      h('div', { 'data-testid': 'composer-delete-variant-count' }, String(deleteVariantCount.value)),
      h('div', { 'data-testid': 'composer-reset-variant-count' }, String(resetVariantCount.value)),
      h('div', { 'data-testid': 'composer-load-variant-count' }, String(loadVariantCount.value)),
      h('div', { 'data-testid': 'composer-selected-variant' }, selectedVariantCode.value),
    ])
  },
})

const ReportComposerFilterHarness = defineComponent({
  setup() {
    const draft = ref(createComposerDraft(composerDefinition))
    const filterQueries = ref<string[]>([])

    return () => h('div', {
      style: 'width: 440px; max-width: 440px; height: 780px; display: flex; flex-direction: column; overflow: hidden;',
    }, [
      h(NgbReportComposerPanel, {
        definition: composerDefinition,
        modelValue: draft.value,
        lookupItemsByFilterCode: {
          property: [
            { id: 'property-1', label: 'Riverfront Tower' },
            { id: 'property-2', label: 'Harbor Point' },
          ],
        },
        'onUpdate:modelValue': (value: unknown) => {
          draft.value = value as ReportComposerDraft
        },
        onFilterQuery: ({ fieldCode, query }: { fieldCode: string; query: string }) => {
          filterQueries.value = [...filterQueries.value, `${fieldCode}:${query}`]
        },
      }),
      h('pre', { 'data-testid': 'composer-filter-draft-state' }, JSON.stringify(draft.value)),
      h('div', { 'data-testid': 'composer-filter-queries' }, filterQueries.value.join('|') || 'none'),
    ])
  },
})

const ReportComposerDynamicTabsHarness = defineComponent({
  setup() {
    const definition = ref<ReportDefinitionDto>(composerDefinition)
    const draft = ref(createComposerDraft(composerDefinition))

    return () => h('div', {
      style: 'width: 440px; max-width: 440px; height: 780px; display: flex; flex-direction: column; overflow: hidden;',
    }, [
      h('button', {
        type: 'button',
        onClick: () => {
          definition.value = buildCompactComposerDefinition()
          draft.value = createComposerDraft(definition.value)
        },
      }, 'Compact mode'),
      h(NgbReportComposerPanel, {
        definition: definition.value,
        modelValue: draft.value,
        lookupItemsByFilterCode: {},
        variantOptions: [
          { value: '', label: 'Definition default' },
          { value: 'month_end', label: 'Month-end snapshot' },
        ],
        selectedVariantCode: '',
        'onUpdate:modelValue': (value: unknown) => {
          draft.value = value as ReportComposerDraft
        },
      }),
    ])
  },
})

const ReportComposerSortNormalizationHarness = defineComponent({
  setup() {
    const draft = ref<ReportComposerDraft>({
      ...createComposerDraft(composerDefinition),
      rowGroups: [
        { groupKey: null, fieldCode: 'period', timeGrain: ReportTimeGrain.Month },
      ],
      columnGroups: [
        { groupKey: null, fieldCode: 'period', timeGrain: ReportTimeGrain.Quarter },
      ],
      sorts: [
        {
          fieldCode: 'period',
          direction: ReportSortDirection.Asc,
          timeGrain: ReportTimeGrain.Month,
          appliesToColumnAxis: false,
          groupKey: null,
        },
      ],
    })

    return () => h('div', {
      style: 'width: 440px; max-width: 440px; height: 780px; display: flex; flex-direction: column; overflow: hidden;',
    }, [
      h(NgbReportComposerPanel, {
        definition: composerDefinition,
        modelValue: draft.value,
        lookupItemsByFilterCode: {},
        'onUpdate:modelValue': (value: unknown) => {
          draft.value = value as ReportComposerDraft
        },
      }),
      h('pre', { 'data-testid': 'composer-normalized-draft-state' }, JSON.stringify(draft.value)),
    ])
  },
})

async function renderHarnessWithRouter(component: object, props: Record<string, unknown> = {}) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/',
        component: {
          template: '<div />',
        },
      },
      {
        path: '/lookups/catalog/:catalogType/:id',
        component: {
          template: '<div data-testid="lookup-target-page">Lookup target</div>',
        },
      },
    ],
  })

  await router.push('/')
  await router.isReady()

  const view = await render(component, {
    props,
    global: {
      plugins: [router],
    },
  })

  return { router, view }
}

async function renderWithRouter(props: Record<string, unknown> = {}) {
  const { view } = await renderHarnessWithRouter(ReportComposerHarness, props)
  return view
}

function collectionRows(): HTMLTableRowElement[] {
  return Array.from(document.querySelectorAll('tbody tr')) as HTMLTableRowElement[]
}

function readDraftState(view: Awaited<ReturnType<typeof renderWithRouter>>): ReportComposerDraft {
  return JSON.parse(view.getByTestId('composer-draft-state').element().textContent ?? '{}') as ReportComposerDraft
}

test('emits run and close actions from the composer header', async () => {
  await page.viewport(1024, 900)

  const view = await renderWithRouter()

  await expect.element(view.getByTestId('report-composer-panel')).toBeVisible()

  await view.getByTitle('Run').click()
  await view.getByTitle('Close').click()

  expect(view.getByTestId('composer-run-count').element().textContent).toBe('1')
  expect(view.getByTestId('composer-close-count').element().textContent).toBe('1')
})

test('adds row groups and measures through composer tabs', async () => {
  await page.viewport(1024, 900)

  const view = await renderWithRouter()

  await view.getByRole('tab', { name: 'Grouping' }).click()
  await view.getByRole('button', { name: 'Add row' }).click()

  let stateText = view.getByTestId('composer-draft-state').element().textContent ?? ''
  expect(stateText).toContain('"rowGroups":[{"groupKey":null,"fieldCode":"property","timeGrain":null}]')

  await view.getByRole('tab', { name: 'Fields' }).click()
  await view.getByRole('button', { name: 'Add measure' }).click()

  stateText = view.getByTestId('composer-draft-state').element().textContent ?? ''
  expect(stateText).toContain('"measureCode":"occupied_units"')
})

test('publishes filter query, selection, include-descendants, and open flows through the filters tab', async () => {
  await page.viewport(1024, 900)

  const { router, view } = await renderHarnessWithRouter(ReportComposerFilterHarness)

  await view.getByRole('tab', { name: 'Filters' }).click()
  await expect.element(view.getByTestId('report-filter-control-property')).toBeVisible()

  const queryInput = view.getByLabelText('filter-query-property').element() as HTMLInputElement
  queryInput.value = 'river'
  queryInput.dispatchEvent(new Event('input', { bubbles: true }))

  await view.getByRole('button', { name: 'Select property' }).click()
  await expect.element(view.getByRole('button', { name: 'Include descendants:true' })).toBeVisible()
  await view.getByRole('button', { name: 'Include descendants:true' }).click()
  await view.getByRole('button', { name: 'Open property' }).click()
  await view.getByRole('button', { name: 'Set raw status' }).click()

  await expect.element(view.getByTestId('composer-filter-queries')).toHaveTextContent('property:river')
  expect(router.currentRoute.value.fullPath).toBe(withBackTarget('/lookups/catalog/pm.property/property-1', '/'))

  const state = JSON.parse(view.getByTestId('composer-filter-draft-state').element().textContent ?? '{}') as ReportComposerDraft
  expect(state.filters.property.items).toEqual([{ id: 'property-1', label: 'Riverfront Tower' }])
  expect(state.filters.property.includeDescendants).toBe(false)
  expect(state.filters.status.raw).toBe('raw:status')
})

test('recomputes sort axis and time grain when grouping changes invalidate the previous row-axis sort', async () => {
  await page.viewport(1024, 900)

  const { view } = await renderHarnessWithRouter(ReportComposerSortNormalizationHarness)

  await view.getByRole('tab', { name: 'Grouping' }).click()

  const deleteButtons = Array.from(document.querySelectorAll('button[title="Delete"]')) as HTMLButtonElement[]
  deleteButtons[0]?.click()

  await expect.poll(() => {
    const state = JSON.parse(view.getByTestId('composer-normalized-draft-state').element().textContent ?? '{}') as ReportComposerDraft
    return state.sorts[0] ?? null
  }).toEqual({
    fieldCode: 'period',
    direction: ReportSortDirection.Asc,
    timeGrain: ReportTimeGrain.Quarter,
    appliesToColumnAxis: true,
    groupKey: null,
  })
})

test('falls back to the first surviving tab when the definition drops optional composer capabilities', async () => {
  await page.viewport(1024, 900)

  const { view } = await renderHarnessWithRouter(ReportComposerDynamicTabsHarness)

  await view.getByRole('tab', { name: 'Variant' }).click()
  await expect.element(view.getByText('Using the report definition default layout and filters.', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: 'Compact mode' }).click()

  await expect.element(view.getByRole('tab', { name: 'Fields' })).toBeVisible()
  expect(document.body.textContent).not.toContain('Using the report definition default layout and filters.')
  expect(document.body.textContent).not.toContain('Grouping')
  expect(document.body.textContent).not.toContain('Filters')
  expect(document.body.textContent).not.toContain('Sorting')
  expect(document.body.textContent).not.toContain('Variant')
  await expect.element(view.getByRole('button', { name: 'Add measure' })).toBeVisible()
})

test('supports variant selection and create action', async () => {
  await page.viewport(1024, 900)

  const view = await renderWithRouter()

  await view.getByRole('tab', { name: 'Variant' }).click()
  await expect.element(view.getByText('Using the report definition default layout and filters.', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: 'Definition default' }).click()
  await view.getByText('Month-end snapshot', { exact: true }).click()
  await view.getByTitle('Create variant').click()

  expect(view.getByTestId('composer-selected-variant').element().textContent).toBe('month_end')
  expect(view.getByTestId('composer-create-variant-count').element().textContent).toBe('1')
})

test('reorders and removes collection items through grouping, fields, and sorting tabs', async () => {
  await page.viewport(1024, 900)

  const view = await renderWithRouter({
    withCollections: true,
  })

  await view.getByRole('tab', { name: 'Grouping' }).click()
  let rows = collectionRows()
  rows[0]?.dispatchEvent(new DragEvent('dragstart', { bubbles: true }))
  rows[1]?.dispatchEvent(new DragEvent('drop', { bubbles: true }))

  await expect.poll(() => readDraftState(view).rowGroups.map((group) => group.fieldCode).join('|'))
    .toBe('manager|property')

  await view.getByRole('tab', { name: 'Fields' }).click()
  rows = collectionRows()
  rows[0]?.dispatchEvent(new DragEvent('dragstart', { bubbles: true }))
  rows[1]?.dispatchEvent(new DragEvent('drop', { bubbles: true }))

  await expect.poll(() => readDraftState(view).measures.map((measure) => measure.measureCode).join('|'))
    .toBe('occupied_units|total_units')

  ;(document.querySelector('button[title="Delete"]') as HTMLButtonElement | null)?.click()

  await expect.poll(() => readDraftState(view).measures.map((measure) => measure.measureCode).join('|'))
    .toBe('total_units')

  await view.getByRole('tab', { name: 'Sorting' }).click()
  rows = collectionRows()
  rows[0]?.dispatchEvent(new DragEvent('dragstart', { bubbles: true }))
  rows[1]?.dispatchEvent(new DragEvent('drop', { bubbles: true }))

  await expect.poll(() => readDraftState(view).sorts.map((sort) => `${sort.fieldCode}:${sort.direction}`).join('|'))
    .toBe('manager:2|property:1')
})

test('ignores invalid cross-section drops so unrelated collections keep their order', async () => {
  await page.viewport(1024, 900)

  const view = await renderWithRouter({
    withCollections: true,
  })

  await view.getByRole('tab', { name: 'Grouping' }).click()
  collectionRows()[0]?.dispatchEvent(new DragEvent('dragstart', { bubbles: true }))

  await view.getByRole('tab', { name: 'Fields' }).click()
  collectionRows()[1]?.dispatchEvent(new DragEvent('drop', { bubbles: true }))

  await expect.poll(() => readDraftState(view).measures.map((measure) => measure.measureCode).join('|'))
    .toBe('total_units|occupied_units')
  expect(readDraftState(view).rowGroups.map((group) => group.fieldCode).join('|')).toBe('property|manager')
})

test('emits edit, save, delete, reset, and load actions from the variant toolbar', async () => {
  await page.viewport(1024, 900)

  const view = await renderWithRouter()

  await view.getByRole('tab', { name: 'Variant' }).click()
  await view.getByRole('button', { name: 'Definition default' }).click()
  await view.getByText('Month-end snapshot', { exact: true }).click()

  await view.getByTitle('Edit variant').click()
  await view.getByTitle('Save variant').click()
  await view.getByTitle('Delete variant').click()
  await view.getByTitle('Reset variant').click()
  await view.getByTitle('Load variant').click()

  expect(view.getByTestId('composer-selected-variant').element().textContent).toBe('month_end')
  expect(view.getByTestId('composer-edit-variant-count').element().textContent).toBe('1')
  expect(view.getByTestId('composer-save-variant-count').element().textContent).toBe('1')
  expect(view.getByTestId('composer-delete-variant-count').element().textContent).toBe('1')
  expect(view.getByTestId('composer-reset-variant-count').element().textContent).toBe('1')
  expect(view.getByTestId('composer-load-variant-count').element().textContent).toBe('1')
})

test('disables the variant toolbar while the composer is running', async () => {
  await page.viewport(1024, 900)

  const view = await renderWithRouter({
    running: true,
  })

  await view.getByRole('tab', { name: 'Variant' }).click()

  for (const title of [
    'Create variant',
    'Edit variant',
    'Save variant',
    'Delete variant',
    'Reset variant',
    'Load variant',
  ]) {
    expect((view.getByTitle(title).element() as HTMLButtonElement).disabled).toBe(true)
  }
})

test('respects per-action disabled flags in the variant toolbar', async () => {
  await page.viewport(1024, 900)

  const view = await renderWithRouter({
    editVariantDisabled: true,
    saveVariantDisabled: true,
    deleteVariantDisabled: true,
    resetVariantDisabled: true,
    loadVariantDisabled: true,
  })

  await view.getByRole('tab', { name: 'Variant' }).click()

  expect((view.getByTitle('Create variant').element() as HTMLButtonElement).disabled).toBe(false)
  expect((view.getByTitle('Edit variant').element() as HTMLButtonElement).disabled).toBe(true)
  expect((view.getByTitle('Save variant').element() as HTMLButtonElement).disabled).toBe(true)
  expect((view.getByTitle('Delete variant').element() as HTMLButtonElement).disabled).toBe(true)
  expect((view.getByTitle('Reset variant').element() as HTMLButtonElement).disabled).toBe(true)
  expect((view.getByTitle('Load variant').element() as HTMLButtonElement).disabled).toBe(true)
})
