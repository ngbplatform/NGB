import { mount } from '@vue/test-utils'
import { defineComponent, h, nextTick } from 'vue'
import { beforeEach, describe, expect, test, vi } from 'vitest'
import type { FieldMetadata, MetadataFormBehavior, PartMetadata, RecordParts } from '@ngbplatform/ui'

const GUID = '11111111-1111-4111-8111-111111111111'
const GUID_2 = '22222222-2222-4222-8222-222222222222'

const mocks = vi.hoisted(() => ({
  push: vi.fn().mockResolvedValue(undefined),
}))

vi.mock('vue-router', async (importOriginal) => ({
  ...(await importOriginal<typeof import('vue-router')>()),
  useRoute: () => ({ fullPath: '/documents/current' }),
  useRouter: () => ({ push: mocks.push }),
}))

vi.mock('@ngbplatform/ui', async (importOriginal) => {
  const platform = await importOriginal<typeof import('@ngbplatform/ui')>()
  const stub = (name: string) => defineComponent({
    name,
    inheritAttrs: false,
    setup(_, { attrs, slots }) {
      return () => h('div', { 'data-stub': name, ...attrs }, slots.default?.())
    },
  })
  const lookup = defineComponent({
    name: 'NgbLookup',
    emits: ['query', 'update:modelValue', 'open'],
    setup(_, { emit }) {
      return () => h('div', { 'data-stub': 'NgbLookup' }, [
        h('button', { 'data-action': 'lookup-empty', onClick: () => emit('query', ' ') }, 'empty query'),
        h('button', { 'data-action': 'lookup-query', onClick: () => emit('query', 'find') }, 'query'),
        h('button', { 'data-action': 'lookup-select', onClick: () => emit('update:modelValue', { id: '22222222-2222-4222-8222-222222222222', label: 'Two' }) }, 'select'),
        h('button', { 'data-action': 'lookup-clear', onClick: () => emit('update:modelValue', null) }, 'clear'),
        h('button', { 'data-action': 'lookup-open', onClick: () => emit('open') }, 'open'),
      ])
    },
  })
  const select = defineComponent({
    name: 'NgbSelect',
    emits: ['update:modelValue'],
    setup(_, { emit }) {
      return () => h('button', { 'data-action': 'select', onClick: () => emit('update:modelValue', 'open') }, 'select option')
    },
  })
  const datePicker = defineComponent({
    name: 'NgbDatePicker',
    emits: ['update:modelValue'],
    setup(_, { emit }) {
      return () => h('button', { 'data-action': 'date', onClick: () => emit('update:modelValue', '2026-08-01') }, 'date')
    },
  })
  const input = defineComponent({
    name: 'NgbInput',
    emits: ['update:modelValue'],
    setup(_, { emit }) {
      return () => h('button', { 'data-action': 'input', onClick: () => emit('update:modelValue', '5') }, 'input')
    },
  })
  return {
    ...platform,
    NgbDatePicker: datePicker,
    NgbIcon: stub('NgbIcon'),
    NgbInput: input,
    NgbLookup: lookup,
    NgbSelect: select,
  }
})

import AgencyBillingDocumentPartsEditor from '../../../src/editor/AgencyBillingDocumentPartsEditor.vue'

function makePart(partCode = 'lines', allowAddRemoveRows = true): PartMetadata {
  return {
    partCode,
    title: partCode === 'lines' ? 'Lines' : 'Empty',
    allowAddRemoveRows,
    list: {
      columns: [
        { key: 'ordinal', label: '#', dataType: 'Int32' },
        { key: 'enabled', label: 'Enabled', dataType: 'Boolean' },
        { key: 'status', label: 'Status', dataType: 'String', widthPx: 120 },
        { key: 'product_id', label: 'Product', dataType: 'Guid', lookup: { kind: 'catalog', catalogType: 'crm.product' } },
        { key: 'service_on', label: 'Service On', dataType: 'Date' },
        { key: 'created_at', label: 'Created At', dataType: 'DateTime' },
        { key: 'quantity', label: 'Quantity', dataType: 'Decimal' },
        { key: 'unit_price', label: 'Unit Price', dataType: 'Money' },
        { key: 'memo', label: 'Memo', dataType: 'String', widthPx: 0 },
        { key: 'line_amount', label: 'Line Amount', dataType: 'Decimal' },
      ],
    },
  } as PartMetadata
}

function makePartWithoutAmount(partCode: string, allowAddRemoveRows = true): PartMetadata {
  return {
    partCode,
    title: 'Without amount',
    allowAddRemoveRows,
    list: {
      columns: [
        { key: 'memo', label: 'Memo', dataType: 'String' },
      ],
    },
  } as PartMetadata
}

function makeModel(): RecordParts {
  return {
    lines: {
      rows: [
        {
          __row_key: 'row-1',
          enabled: true,
          status: 'open',
          product_id: { id: GUID, display: 'Product One' },
          service_on: '2026-07-31',
          created_at: '2026-07-31T10:00',
          quantity: 2,
          unit_price: 10,
          memo: 'first',
          line_amount: 20,
        },
        {
          __row_key: 'row-2',
          enabled: false,
          status: null,
          product_id: GUID_2,
          service_on: null,
          created_at: null,
          quantity: null,
          unit_price: null,
          memo: null,
          line_amount: 'bad',
        },
      ],
    },
    empty: { rows: [] },
    no_amount: { rows: [] },
    no_list: { rows: [] },
  }
}

function makeBehavior() {
  const searchLookup = vi.fn(async () => [{ id: GUID_2, label: 'Product Two' }])
  const buildLookupTargetUrl = vi.fn(async () => '/catalogs/products/1')
  const behavior: MetadataFormBehavior = {
    resolveFieldOptions: ({ field }) => field.key === 'status'
      ? [{ value: 'open', label: 'Open' }]
      : null,
    searchLookup,
    buildLookupTargetUrl,
  }
  return { behavior, searchLookup, buildLookupTargetUrl }
}

function stateOf(wrapper: ReturnType<typeof mount>): Record<string, any> {
  return (wrapper.vm as any).$?.setupState ?? (wrapper.vm as any).$?.setupState
}

function field(key: string, dataType: string, extra: Record<string, unknown> = {}): FieldMetadata {
  return {
    key,
    label: key,
    dataType,
    uiControl: 1,
    isRequired: false,
    isReadOnly: false,
    lookup: null,
    validation: null,
    helpText: null,
    ...extra,
  } as FieldMetadata
}

async function exerciseEditor(): Promise<void> {
  const component = AgencyBillingDocumentPartsEditor
  const entityTypeCode = 'ab.sales_invoice'
  const parts = [
    makePart(),
    makePartWithoutAmount('empty', false),
    makePartWithoutAmount('no_amount'),
    { partCode: 'no_list', title: 'No list', allowAddRemoveRows: false, list: null } as PartMetadata,
  ]
  const modelValue = makeModel()
  const documentModel = { amount: 0 }
  const { behavior, searchLookup, buildLookupTargetUrl } = makeBehavior()
  const wrapper = mount(component, {
    attachTo: document.body,
    props: {
      entityTypeCode,
      parts,
      modelValue,
      documentModel,
      behavior,
      errors: {
        lines: {
          0: { memo: 'Required', status: '' },
          1: { memo: null as never, status: ' ' },
        },
      },
    },
  })
  await nextTick()
  const state = stateOf(wrapper)

  expect(wrapper.text()).toContain('Lines')
  expect(wrapper.text()).toContain('No rows yet.')
  expect(wrapper.text()).toContain('Required')
  for (const control of wrapper.findAll('[data-action]')) await control.trigger('click')
  for (const checkbox of wrapper.findAll('input[type="checkbox"]')) {
    ;(checkbox.element as HTMLInputElement).checked = !(checkbox.element as HTMLInputElement).checked
    await checkbox.trigger('change')
  }
  for (const button of wrapper.findAll('button[title="Delete"]')) await button.trigger('click')
  for (const button of wrapper.findAll('button')) {
    if (button.text().includes('Add row')) await button.trigger('click')
  }
  const renderedRows = wrapper.findAll('tbody tr')
  if (renderedRows.length > 1) {
    const dataTransfer = new DataTransfer()
    await renderedRows[0].trigger('dragstart', { dataTransfer })
    await renderedRows[1].trigger('dragover', { dataTransfer })
    await renderedRows[1].trigger('drop', { dataTransfer })
  }
  expect(state.partRows('lines')).toHaveLength(2)
  expect(state.partRows('missing')).toEqual([])
  expect(state.cloneParts()).toMatchObject(modelValue)
  expect(state.cloneNormalizedParts().lines.rows).toHaveLength(2)

  state.emitParts(modelValue)
  state.emitRows('lines', state.partRows('lines'))
  const emptyRow = state.createEmptyRow('lines')
  expect(emptyRow.enabled).toBe(false)
  expect(emptyRow.ordinal).toBe(3)
  expect(state.createEmptyRow('missing')).toMatchObject({ ordinal: 1 })
  expect(state.canManageRows('lines')).toBe(true)
  expect(state.canManageRows('empty')).toBe(false)
  expect(state.canManageRows('missing')).toBe(true)
  state.addRow('lines')
  state.addRow('empty')
  state.removeRow('lines', 0)
  state.removeRow('empty', 0)
  state.updateCell('lines', 0, 'quantity', 3)
  state.updateCell('lines', 99, 'quantity', 3)
  expect(state.lookupCellKey('lines', 0, 'product_id')).toBe('lines:0:product_id')
  expect(state.fieldError('lines', 0, 'memo')).toBe('Required')
  expect(state.fieldError('lines', 1, 'missing')).toBeNull()
  expect(state.rowHasErrors('lines', 0)).toBe(true)
  expect(state.rowHasErrors('lines', 1)).toBe(false)
  expect(state.rowHasErrors('empty', 0)).toBe(false)

  const row = state.partRows('lines')[0]
  expect(state.resolveFieldState(field('status', 'String'), row).mode).toBe('select')
  expect(state.resolveFieldState(field('product_id', 'Guid', { lookup: { kind: 'catalog', catalogType: 'crm.product' } }), row).mode).toBe('lookup')
  expect(state.resolveFieldState(field('enabled', 'Boolean'), row).mode).toBe('checkbox')
  expect(state.resolveFieldState(field('service_on', 'Date'), row).mode).toBe('date')
  expect(state.resolveFieldState(field('created_at', 'DateTime'), row).inputType).toBe('datetime-local')
  expect(state.resolveFieldState(field('count', 'Int32'), row).inputType).toBe('number')
  expect(state.resolveFieldState(field('quantity', 'Decimal'), row).inputType).toBe('number')
  expect(state.resolveFieldState(field('price', 'Money'), row).inputType).toBe('number')
  expect(state.resolveFieldState(field('memo', 'String'), row).inputType).toBe('text')

  expect(state.lookupValue({ product_id: null }, 'product_id')).toBeNull()
  expect(state.lookupValue({ product_id: { id: GUID, display: 'One' } }, 'product_id')).toEqual({ id: GUID, label: 'One' })
  expect(state.lookupValue({ product_id: GUID_2 }, 'product_id')).toEqual({ id: GUID_2, label: GUID_2 })
  expect(state.lookupValue({ product_id: '' }, 'product_id')).toBeNull()
  expect(state.lookupValue({ product_id: {} }, 'product_id')).toBeNull()

  const lookupField = field('product_id', 'Guid', { lookup: { kind: 'catalog', catalogType: 'crm.product' } })
  await state.onLookupQuery('lines', 0, lookupField, row, ' ')
  await state.onLookupQuery('lines', 0, lookupField, row, null as never)
  await state.onLookupQuery('lines', 0, field('memo', 'String'), row, 'find')
  await state.onLookupQuery('lines', 0, lookupField, row, ' find ')
  expect(searchLookup).toHaveBeenCalledWith(expect.objectContaining({ query: 'find' }))
  state.onLookupSelect('lines', 0, 'product_id', { id: GUID_2, label: 'Two' })
  state.onLookupSelect('lines', 0, 'product_id', null)

  await state.openLookup(field('memo', 'String'), row)
  await state.openLookup(lookupField, row)
  expect(buildLookupTargetUrl).toHaveBeenCalled()
  expect(mocks.push).toHaveBeenCalledWith('/catalogs/products/1')
  buildLookupTargetUrl.mockResolvedValueOnce(null as never)
  await state.openLookup(lookupField, row)

  expect(state.canReorder('lines')).toBe(true)
  expect(state.canReorder('empty')).toBe(false)
  const transfer = {
    setData: vi.fn(),
    setDragImage: vi.fn(),
    getData: vi.fn(() => 'lines:0'),
  }
  state.onDragStart('lines', 0, { dataTransfer: transfer } as never)
  expect(transfer.setData).toHaveBeenCalled()
  state.onDragStart('lines', 0, {
    dataTransfer: {
      setData: () => { throw new Error('unsupported') },
      setDragImage: vi.fn(),
    },
  } as never)
  const preventDefault = vi.fn()
  state.onDragOver('lines', { preventDefault } as never)
  state.onDragOver('empty', { preventDefault } as never)
  state.onDrop('lines', 1, { preventDefault } as never)
  state.onDrop('lines', 1, { preventDefault, dataTransfer: transfer } as never)
  state.onDrop('lines', 1, { preventDefault, dataTransfer: { getData: () => 'other:0' } } as never)
  state.onDrop('lines', 1, { preventDefault, dataTransfer: { getData: () => 'lines:x' } } as never)
  state.onDrop('lines', 1, { preventDefault, dataTransfer: { getData: () => 'lines:1' } } as never)
  state.onDrop('lines', 1, { preventDefault, dataTransfer: { getData: () => 'lines:99' } } as never)
  state.onDrop('empty', 0, { preventDefault } as never)

  expect(state.fieldColStyle('lines', field('status', 'String'))).toBe('width:120px')
  expect(state.fieldColStyle('lines', field('product_id', 'Lookup', { lookup: { kind: 'catalog', catalogType: 'crm.product' } }))).toBe('width:260px')
  expect(state.fieldColStyle('lines', field('enabled', 'Boolean'))).toBe('width:96px')
  expect(state.fieldColStyle('lines', field('service_on', 'Date'))).toBe('width:170px')
  expect(state.fieldColStyle('lines', field('created_at', 'DateTime'))).toBe('width:170px')
  expect(state.fieldColStyle('lines', field('quantity', 'Decimal'))).toBe('width:160px')
  expect(state.fieldColStyle('lines', field('memo', 'String'))).toBeUndefined()
  expect(state.partAmount(parts[0])).toBe(20)
  expect(state.partAmount(parts[2])).toBeNull()
  expect(state.partAmount({ partCode: 'none', title: 'None', list: { columns: [] } })).toBeNull()
  expect(state.formatAmount(null)).toMatch(/0/)
  expect(state.formatAmount(12.34567)).toMatch(/12/)

  wrapper.unmount()

  const readonlyWrapper = mount(component, {
    attachTo: document.body,
    props: { entityTypeCode, parts: [makePart()], modelValue, readonly: true },
  })
  await nextTick()
  const readonlyState = stateOf(readonlyWrapper)
  expect(readonlyState.canManageRows('lines')).toBe(false)
  expect(readonlyState.canReorder('lines')).toBe(false)
  readonlyState.onDragStart('lines', 0, {} as never)
  readonlyState.onDrop('lines', 0, { preventDefault: vi.fn() } as never)
  readonlyWrapper.unmount()

  const withoutModel = mount(component, {
    props: { entityTypeCode, parts: [makePartWithoutAmount('blank')] },
  })
  expect(stateOf(withoutModel).cloneParts()).toEqual({})
  withoutModel.unmount()
}

describe('document parts editors', () => {
  beforeEach(() => {
    mocks.push.mockClear()
  })

  test('covers the Agency Billing grid contract', async () => {
    await exerciseEditor()
  })
})
