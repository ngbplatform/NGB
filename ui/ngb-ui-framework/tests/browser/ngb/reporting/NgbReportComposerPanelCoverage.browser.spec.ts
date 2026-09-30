import { mount } from '@vue/test-utils'
import { defineComponent, h, nextTick, ref, type PropType } from 'vue'
import { beforeEach, expect, test, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  routerPush: vi.fn(),
  resolveLookupTarget: vi.fn(),
  controls: [] as Array<{ kind: string; props: any; attrs: any }>,
  collections: {} as Record<string, { props: any; attrs: any }>,
  filters: {} as Record<string, { props: any; attrs: any }>,
  tabs: null as any,
  draft: null as any,
  definition: null as any,
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ fullPath: '/reports/full?source=test' }),
  useRouter: () => ({ push: mocks.routerPush }),
}))

vi.mock('../../../../src/ngb/reporting/config', () => ({
  resolveReportLookupTarget: mocks.resolveLookupTarget,
}))

vi.mock('../../../../src/ngb/primitives/NgbSelect.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({
    inheritAttrs: false,
    props: { modelValue: { default: null }, options: { type: Array, default: () => [] }, disabled: { type: Boolean, default: false } },
    setup(props, { attrs, slots }) {
      mocks.controls.push({ kind: 'select', props, attrs })
      return () => h('div', { 'data-control': 'select' }, slots.default?.())
    },
  }) }
})

vi.mock('../../../../src/ngb/primitives/NgbInput.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({
    inheritAttrs: false,
    props: { modelValue: { default: null }, type: { type: String, default: 'text' }, placeholder: { type: String, default: '' } },
    setup(props, { attrs, slots }) {
      mocks.controls.push({ kind: 'input', props, attrs })
      return () => h('div', { 'data-control': 'input' }, slots.default?.())
    },
  }) }
})

vi.mock('../../../../src/ngb/primitives/NgbDatePicker.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({
    inheritAttrs: false,
    props: { modelValue: { default: null } },
    setup(props, { attrs, slots }) {
      mocks.controls.push({ kind: 'date', props, attrs })
      return () => h('div', { 'data-control': 'date' }, slots.default?.())
    },
  }) }
})

vi.mock('../../../../src/ngb/primitives/NgbSwitch.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({
    inheritAttrs: false,
    props: { modelValue: { type: Boolean, default: false }, disabled: { type: Boolean, default: false } },
    setup(props, { attrs, slots }) {
      mocks.controls.push({ kind: 'switch', props, attrs })
      return () => h('div', { 'data-control': 'switch' }, slots.default?.())
    },
  }) }
})

vi.mock('../../../../src/ngb/primitives/NgbButton.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({
    inheritAttrs: false,
    props: { disabled: { type: Boolean, default: false } },
    setup(props, { attrs, slots }) {
      mocks.controls.push({ kind: 'button', props, attrs })
      return () => h('div', { 'data-control': 'button' }, slots.default?.())
    },
  }) }
})

vi.mock('../../../../src/ngb/primitives/NgbIcon.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({ setup: () => () => h('span') }) }
})

vi.mock('../../../../src/ngb/primitives/NgbTabs.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return {
    default: defineComponent({
      inheritAttrs: false,
      props: {
        modelValue: { type: String, required: true },
        tabs: { type: Array as PropType<Array<{ key: string; label: string }>>, default: () => [] },
      },
      setup(props, { attrs, slots }) {
        mocks.tabs = { props, attrs }
        return () => h('div', props.tabs.map((tab) => h('section', { key: tab.key, 'data-tab': tab.key }, slots.default?.({ active: tab.key }))))
      },
    }),
  }
})

vi.mock('../../../../src/ngb/components/forms/NgbFormLayout.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({ setup: (_props, { slots }) => () => h('div', slots.default?.()) }) }
})

vi.mock('../../../../src/ngb/components/forms/NgbFormRow.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({ setup: (_props, { slots }) => () => h('div', slots.default?.()) }) }
})

vi.mock('../../../../src/ngb/metadata/NgbFilterFieldControl.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return {
    default: defineComponent({
      inheritAttrs: false,
      props: {
        field: { type: Object, required: true },
        state: { type: Object, required: true },
        lookupItems: { type: Array, default: () => [] },
        showOpen: { type: Boolean, default: false },
        showClear: { type: Boolean, default: false },
      },
      setup(props, { attrs }) {
        return () => {
          mocks.filters[(props.field as any).fieldCode] = { props, attrs }
          return h('div')
        }
      },
    }),
  }
})

vi.mock('../../../../src/ngb/reporting/NgbReportComposerCollectionSection.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return {
    default: defineComponent({
      inheritAttrs: false,
      props: {
        title: { type: String, required: true },
        addLabel: { type: String, required: true },
        items: { type: Array, default: () => [] },
        columns: { type: Array, default: () => [] },
        section: { type: String, required: true },
        rowKey: { type: Function, required: true },
      },
      setup(props, { attrs, slots }) {
        return () => {
          mocks.collections[props.section] = { props, attrs }
          const cells = props.items.flatMap((item: any, index: number) => [
            h('span', { key: `key:${(props.rowKey as any)(item, index)}` }),
            slots.cells?.({ item, index }),
          ])
          if (props.items.length > 0) cells.push(slots.cells?.({ item: props.items[0], index: 999 }))
          return h('div', { 'data-section': props.section }, cells)
        }
      },
    }),
  }
})

import NgbReportComposerPanel from '../../../../src/ngb/reporting/NgbReportComposerPanel.vue'
import {
  ReportAggregationKind,
  ReportFieldKind,
  ReportSortDirection,
  ReportTimeGrain,
  type ReportComposerDraft,
  type ReportDefinitionDto,
} from '../../../../src/ngb/reporting/types'

function definition(): ReportDefinitionDto {
  return {
    reportCode: 'coverage.report',
    name: 'Coverage report',
    description: 'All composer branches',
    capabilities: {
      allowsFilters: true,
      allowsRowGroups: true,
      allowsColumnGroups: true,
      allowsMeasures: true,
      allowsDetailFields: true,
      allowsSorting: true,
      allowsShowDetails: true,
      allowsSubtotals: true,
      allowsSeparateRowSubtotals: true,
      allowsGrandTotals: true,
      allowsVariants: true,
    },
    dataset: {
      datasetCode: 'coverage',
      fields: [
        { code: 'property', label: 'Property', dataType: 'String', kind: ReportFieldKind.Dimension, isGroupable: true, isSelectable: true, isSortable: true },
        { code: 'manager', label: 'Manager', dataType: 'String', kind: ReportFieldKind.Dimension, isGroupable: true, isSelectable: true, isSortable: true, description: 'Manager description' },
        { code: 'period', label: 'Period', dataType: 'Date', kind: ReportFieldKind.Time, isGroupable: true, isSelectable: true, isSortable: true, supportedTimeGrains: [ReportTimeGrain.Year, ReportTimeGrain.Quarter, ReportTimeGrain.Month, ReportTimeGrain.Week, ReportTimeGrain.Day] },
        { code: 'unsortable', label: 'Unsortable', dataType: 'String', kind: ReportFieldKind.Dimension, isGroupable: true, isSelectable: true, isSortable: false },
        { code: 'column_only', label: 'Column only', dataType: 'String', kind: ReportFieldKind.Dimension, isGroupable: true, isSelectable: false, isSortable: true },
        { code: 'detail_only', label: 'Detail only', dataType: 'String', kind: ReportFieldKind.Detail, isGroupable: false, isSelectable: true, isSortable: true },
      ],
      measures: [
        { code: 'amount', label: 'Amount', dataType: 'Decimal', supportedAggregations: [ReportAggregationKind.Sum, ReportAggregationKind.Average] },
        { code: 'count', label: 'Count', dataType: 'Int32', supportedAggregations: [ReportAggregationKind.Count] },
      ],
    },
    parameters: [
      { code: 'as_of', label: 'As of', dataType: 'Date_Time_UTC', isRequired: false },
      { code: 'limit', label: null, dataType: 'Integer', isRequired: false, description: null },
      { code: 'ratio', label: 'Ratio', dataType: 'Decimal', isRequired: false },
      { code: 'note', label: 'Note', dataType: 'String', isRequired: false },
    ],
    filters: [
      { fieldCode: 'property', label: 'Property', dataType: 'String', lookup: { kind: 'catalog', catalogType: 'pm.property' }, supportsIncludeDescendants: true },
      { fieldCode: 'status', label: 'Status', dataType: 'String', options: [{ value: 'open', label: 'Open' }] },
    ],
  }
}

function draft(): ReportComposerDraft {
  return {
    parameters: { as_of: '2026-01-02T03:04:05Z', limit: '10', ratio: '1.5', note: 'hello' },
    filters: {
      property: { raw: '', items: [{ id: 'property-1', label: 'Property One' }], includeDescendants: true },
      status: { raw: 'open', items: [], includeDescendants: false },
    },
    rowGroups: [
      { groupKey: null, fieldCode: 'period', timeGrain: ReportTimeGrain.Year },
      { groupKey: null, fieldCode: 'period', timeGrain: ReportTimeGrain.Year },
      { groupKey: null, fieldCode: 'property', timeGrain: null },
      { groupKey: null, fieldCode: 'unsortable', timeGrain: null },
    ],
    columnGroups: [
      { groupKey: null, fieldCode: 'period', timeGrain: ReportTimeGrain.Quarter },
      { groupKey: null, fieldCode: 'period', timeGrain: ReportTimeGrain.Quarter },
      { groupKey: null, fieldCode: 'manager', timeGrain: null },
      { groupKey: null, fieldCode: 'unsortable', timeGrain: null },
      { groupKey: null, fieldCode: 'column_only', timeGrain: null },
    ],
    measures: [
      { measureCode: 'amount', aggregation: null, labelOverride: null },
      { measureCode: 'count', aggregation: ReportAggregationKind.Count, labelOverride: 'Custom count' },
    ],
    detailFields: ['manager', 'property', 'unsortable', 'detail_only', null as unknown as string],
    sorts: [
      { fieldCode: 'period', direction: ReportSortDirection.Asc, timeGrain: ReportTimeGrain.Year, appliesToColumnAxis: false, groupKey: null },
      { fieldCode: 'period', direction: ReportSortDirection.Desc, timeGrain: ReportTimeGrain.Quarter, appliesToColumnAxis: true, groupKey: null },
      { fieldCode: 'manager', direction: ReportSortDirection.Asc, timeGrain: null, appliesToColumnAxis: false, groupKey: null },
      { fieldCode: 'property', direction: ReportSortDirection.Asc, timeGrain: null, appliesToColumnAxis: true, groupKey: null },
      { fieldCode: 'column_only', direction: ReportSortDirection.Asc, timeGrain: null, appliesToColumnAxis: false, groupKey: null },
      { fieldCode: 'detail_only', direction: ReportSortDirection.Asc, timeGrain: null, appliesToColumnAxis: false, groupKey: null },
      { fieldCode: 'missing', direction: ReportSortDirection.Asc, timeGrain: null, appliesToColumnAxis: false, groupKey: null },
    ],
    showDetails: true,
    showSubtotals: true,
    showSubtotalsOnSeparateRows: false,
    showGrandTotals: true,
  }
}

const Harness = defineComponent({
  setup() {
    const currentDefinition = ref(definition())
    const currentDraft = ref(draft())
    mocks.definition = currentDefinition
    mocks.draft = currentDraft
    return () => h(NgbReportComposerPanel, {
      definition: currentDefinition.value,
      modelValue: currentDraft.value,
      lookupItemsByFilterCode: { property: [{ id: 'property-1', label: 'Property One' }] },
      variantOptions: undefined,
      selectedVariantCode: undefined,
      variantSummary: undefined,
      'onUpdate:modelValue': (value: ReportComposerDraft) => { currentDraft.value = value },
    })
  },
})

function call(target: { attrs: any }, name: string, ...args: any[]) {
  const handler = target.attrs[name]
  if (typeof handler !== 'function') throw new Error(`${name} handler not found: ${Object.keys(target.attrs).join(',')}`)
  return handler(...args)
}

async function settle() {
  await nextTick()
  await nextTick()
}

beforeEach(() => {
  vi.clearAllMocks()
  mocks.controls = []
  mocks.collections = {}
  mocks.filters = {}
  mocks.tabs = null
  mocks.resolveLookupTarget.mockResolvedValue('/lookup-target')
  mocks.routerPush.mockResolvedValue(undefined)
})

test('executes every collection, control, filter, sorting, and formatting port', async () => {
  const wrapper = mount(Harness)
  await settle()
  expect(Object.keys(mocks.collections).sort()).toEqual(['columnGroups', 'detailFields', 'measures', 'rowGroups', 'sorts'])

  for (const collection of Object.values(mocks.collections)) {
    call(collection, 'onAdd')
    call(collection, 'onRemove', 999)
  }
  await settle()

  for (const control of [...mocks.controls]) {
    if (control.kind === 'select') {
      for (const value of [null, '', 'property', 'manager', 'period', 'column', 'detail', 'row', ReportTimeGrain.Month, ReportSortDirection.Desc, 'invalid']) {
        call(control, 'onUpdate:modelValue', value)
      }
    } else if (control.kind === 'input') {
      call(control, 'onUpdate:modelValue', null)
      call(control, 'onUpdate:modelValue', 'Custom label')
    } else if (control.kind === 'date') {
      call(control, 'onUpdate:modelValue', null)
      call(control, 'onUpdate:modelValue', '2026-12-31')
    } else if (control.kind === 'switch') {
      call(control, 'onUpdate:modelValue', !control.props.modelValue)
    }
  }
  await settle()

  call(mocks.filters.property, 'onLookupQuery', 'tower')
  call(mocks.filters.property, 'onUpdate:items', [])
  call(mocks.filters.property, 'onUpdate:items', [{ id: 'property-2', label: 'Property Two' }])
  call(mocks.filters.property, 'onUpdate:raw', 'manual')
  call(mocks.filters.property, 'onUpdate:includeDescendants', false)
  call(mocks.filters.status, 'onUpdate:raw', 'closed')
  await call(mocks.filters.property, 'onOpen')
  expect(mocks.routerPush).toHaveBeenCalledWith('/lookup-target')
  mocks.resolveLookupTarget.mockResolvedValueOnce(null)
  await call(mocks.filters.property, 'onOpen')
  await settle()

  const preventDefault = vi.fn()
  const throwingDrag = {
    preventDefault,
    dataTransfer: {
      setData: () => { throw new Error('blocked') },
      setDragImage: vi.fn(),
      getData: () => '0',
    },
  } as any
  call(mocks.collections.rowGroups, 'onDragstart', { section: 'rowGroups', index: 0, event: throwingDrag })
  call(mocks.collections.rowGroups, 'onDragover', throwingDrag)
  call(mocks.collections.rowGroups, 'onDrop', { section: 'rowGroups', index: 1, event: throwingDrag })

  for (const section of ['columnGroups', 'measures', 'detailFields', 'sorts']) {
    const collection = mocks.collections[section]!
    const event = { preventDefault, dataTransfer: { getData: () => '0' } } as any
    call(collection, 'onDrop', { section, index: 1, event })
  }
  const invalidEvent = { preventDefault, dataTransfer: { getData: () => 'not-a-number' } } as any
  call(mocks.collections.sorts, 'onDrop', { section: 'sorts', index: 0, event: invalidEvent })
  const negativeEvent = { preventDefault, dataTransfer: { getData: () => '-1' } } as any
  call(mocks.collections.sorts, 'onDrop', { section: 'sorts', index: 1, event: negativeEvent })
  const largeEvent = { preventDefault, dataTransfer: { getData: () => '999' } } as any
  call(mocks.collections.sorts, 'onDrop', { section: 'sorts', index: 1, event: largeEvent })
  await settle()

  expect(mocks.draft.value.parameters.as_of).toBeDefined()
  wrapper.unmount()
})

test('covers empty-definition guards and stale captured collection handlers', async () => {
  const wrapper = mount(Harness)
  await settle()
  const captured = { ...mocks.collections }

  mocks.definition.value = {
    reportCode: 'empty',
    name: 'Empty',
    description: null,
    capabilities: {
      allowsRowGroups: true,
      allowsColumnGroups: true,
      allowsMeasures: true,
      allowsDetailFields: true,
      allowsSorting: true,
      allowsShowDetails: false,
      allowsSubtotals: false,
      allowsSeparateRowSubtotals: false,
      allowsGrandTotals: false,
      allowsVariants: false,
    },
    dataset: { datasetCode: 'empty', fields: [], measures: [] },
    parameters: [],
    filters: [],
  }
  mocks.draft.value = {
    parameters: {}, filters: {}, rowGroups: [], columnGroups: [], measures: [], detailFields: [], sorts: [],
    showDetails: false, showSubtotals: false, showSubtotalsOnSeparateRows: false, showGrandTotals: false,
  }
  await settle()

  for (const collection of Object.values(captured)) call(collection, 'onAdd')
  expect(mocks.draft.value.rowGroups).toEqual([])
  expect(mocks.draft.value.measures).toEqual([])
  expect(mocks.draft.value.sorts).toEqual([])
  expect(mocks.draft.value.detailFields).toEqual([])
  wrapper.unmount()
})

test('covers drill progression, fallback axes, missing filter states, and nullable metadata', async () => {
  const wrapper = mount(Harness)
  await settle()

  delete mocks.draft.value.parameters.as_of
  await settle()

  mocks.draft.value = {
    ...draft(),
    rowGroups: [{ groupKey: null, fieldCode: 'period', timeGrain: ReportTimeGrain.Year }],
    columnGroups: [{ groupKey: null, fieldCode: 'column_only', timeGrain: null }],
    detailFields: ['detail_only'],
    sorts: [
      { fieldCode: 'column_only', direction: ReportSortDirection.Asc, timeGrain: null, appliesToColumnAxis: false, groupKey: null },
      { fieldCode: 'detail_only', direction: ReportSortDirection.Asc, timeGrain: null, appliesToColumnAxis: false, groupKey: null },
      { fieldCode: 'missing', direction: ReportSortDirection.Asc, timeGrain: null, appliesToColumnAxis: false, groupKey: null },
    ],
  }
  await settle()
  call(mocks.collections.rowGroups, 'onAdd')
  await settle()
  expect(mocks.draft.value.rowGroups[1]?.timeGrain).toBe(ReportTimeGrain.Quarter)

  const sortFieldControl = mocks.controls.find((control) =>
    control.kind === 'select'
    && control.props.modelValue === 'column_only'
    && control.props.options.some((option: any) => option.value === 'detail_only'))
  expect(sortFieldControl).toBeDefined()
  call(sortFieldControl!, 'onUpdate:modelValue', 'column_only')
  call(sortFieldControl!, 'onUpdate:modelValue', 'detail_only')
  await settle()

  const sparseDefinition = definition()
  const sparsePeriod = sparseDefinition.dataset!.fields!.find((field) => field.code === 'period')!
  sparsePeriod.supportedTimeGrains = [ReportTimeGrain.Year, ReportTimeGrain.Day]
  mocks.definition.value = sparseDefinition
  mocks.draft.value = {
    ...draft(),
    rowGroups: [{ groupKey: null, fieldCode: 'period', timeGrain: ReportTimeGrain.Year }],
  }
  await settle()
  call(mocks.collections.rowGroups, 'onAdd')
  await settle()
  expect(mocks.draft.value.rowGroups[1]?.timeGrain).toBe(ReportTimeGrain.Day)

  mocks.definition.value = definition()
  mocks.draft.value = {
    ...draft(),
    rowGroups: [
      { groupKey: null, fieldCode: 'property', timeGrain: null },
      { groupKey: null, fieldCode: 'period', timeGrain: ReportTimeGrain.Quarter },
      { groupKey: null, fieldCode: 'period', timeGrain: ReportTimeGrain.Year },
    ],
  }
  await settle()
  call(mocks.collections.rowGroups, 'onAdd')
  await settle()

  mocks.draft.value = {
    ...draft(),
    rowGroups: [
      { groupKey: null, fieldCode: 'property', timeGrain: null },
      { groupKey: null, fieldCode: 'manager', timeGrain: null },
      { groupKey: null, fieldCode: 'period', timeGrain: ReportTimeGrain.Day },
      { groupKey: null, fieldCode: 'unsortable', timeGrain: null },
      { groupKey: null, fieldCode: 'column_only', timeGrain: null },
    ],
  }
  await settle()
  call(mocks.collections.rowGroups, 'onAdd')
  await settle()

  mocks.draft.value = {
    ...draft(),
    rowGroups: [{ groupKey: null, fieldCode: 'property', timeGrain: 999 as ReportTimeGrain }],
    columnGroups: [],
    detailFields: [],
    sorts: [{ fieldCode: 'property', direction: ReportSortDirection.Asc, timeGrain: 999 as ReportTimeGrain, appliesToColumnAxis: false, groupKey: null }],
  }
  await settle()

  mocks.draft.value = {
    ...draft(),
    rowGroups: [
      { groupKey: null, fieldCode: 'property', timeGrain: null },
      { groupKey: null, fieldCode: 'period', timeGrain: ReportTimeGrain.Year },
    ],
    columnGroups: [
      { groupKey: null, fieldCode: 'period', timeGrain: ReportTimeGrain.Quarter },
      { groupKey: null, fieldCode: 'column_only', timeGrain: null },
    ],
    detailFields: ['manager', 'detail_only'],
    sorts: [
      { fieldCode: 'property', direction: ReportSortDirection.Asc, timeGrain: null, appliesToColumnAxis: false, groupKey: null },
      { fieldCode: 'period', direction: ReportSortDirection.Asc, timeGrain: ReportTimeGrain.Year, appliesToColumnAxis: false, groupKey: null },
      { fieldCode: 'period', direction: ReportSortDirection.Asc, timeGrain: ReportTimeGrain.Quarter, appliesToColumnAxis: true, groupKey: null },
      { fieldCode: 'column_only', direction: ReportSortDirection.Asc, timeGrain: null, appliesToColumnAxis: true, groupKey: null },
      { fieldCode: 'manager', direction: ReportSortDirection.Asc, timeGrain: null, appliesToColumnAxis: false, groupKey: null },
      { fieldCode: 'detail_only', direction: ReportSortDirection.Asc, timeGrain: null, appliesToColumnAxis: false, groupKey: null },
    ],
  }
  await settle()
  call(mocks.collections.sorts, 'onAdd')
  await settle()

  mocks.draft.value = {
    ...draft(),
    detailFields: ['property', 'manager', 'period', 'unsortable', 'detail_only'],
  }
  await settle()
  call(mocks.collections.detailFields, 'onAdd')
  await settle()

  mocks.draft.value = { ...mocks.draft.value, rowGroups: [{ groupKey: null, fieldCode: 'period', timeGrain: ReportTimeGrain.Day }] }
  await settle()
  call(mocks.collections.rowGroups, 'onAdd')
  await settle()
  mocks.draft.value = { ...mocks.draft.value, rowGroups: [{ groupKey: null, fieldCode: 'period', timeGrain: 999 as ReportTimeGrain }] }
  await settle()
  call(mocks.collections.rowGroups, 'onAdd')
  await settle()
  mocks.draft.value = { ...mocks.draft.value, rowGroups: [{ groupKey: null, fieldCode: 'missing', timeGrain: ReportTimeGrain.Year }] }
  await settle()
  call(mocks.collections.rowGroups, 'onAdd')
  await settle()
  mocks.draft.value = { ...mocks.draft.value, rowGroups: [{ groupKey: null, fieldCode: 'property', timeGrain: null }] }
  await settle()
  call(mocks.collections.rowGroups, 'onAdd')
  await settle()

  const propertyFilter = mocks.filters.property
  const statusFilter = mocks.filters.status
  delete mocks.draft.value.filters.property
  delete mocks.draft.value.filters.status
  call(propertyFilter, 'onUpdate:raw', 'ignored')
  call(propertyFilter, 'onUpdate:items', [{ id: 'ignored', label: 'Ignored' }])
  call(propertyFilter, 'onUpdate:includeDescendants', true)
  await call(statusFilter, 'onOpen')
  await settle()

  mocks.draft.value.parameters.as_of = 'invalid-date'
  delete mocks.draft.value.parameters.limit
  await settle()
  mocks.draft.value.parameters.as_of = ''
  await settle()

  const nullableDefinition = definition()
  nullableDefinition.description = null
  nullableDefinition.parameters = null
  nullableDefinition.filters = null
  nullableDefinition.capabilities = {
    ...nullableDefinition.capabilities,
    allowsSorting: false,
    allowsRowGroups: false,
    allowsColumnGroups: false,
    allowsDetailFields: false,
    allowsMeasures: false,
    allowsVariants: false,
  }
  mocks.definition.value = nullableDefinition
  await settle()
  expect(mocks.tabs.props.tabs.some((tab: any) => tab.key === 'general')).toBe(true)
  wrapper.unmount()
})
