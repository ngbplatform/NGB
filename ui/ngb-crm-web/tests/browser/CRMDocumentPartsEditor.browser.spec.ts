import { mount } from '@vue/test-utils'
import { defineComponent, h, nextTick } from 'vue'
import { beforeEach, describe, expect, test, vi } from 'vitest'
import type { FieldMetadata, MetadataFormBehavior, PartMetadata, RecordParts } from '@ngbplatform/ui'

const PRODUCT_1 = '11111111-1111-4111-8111-111111111111'
const PRODUCT_2 = '22222222-2222-4222-8222-222222222222'

const mocks = vi.hoisted(() => ({
  push: vi.fn().mockResolvedValue(undefined),
}))

vi.mock('vue-router', async (importOriginal) => ({
  ...(await importOriginal<typeof import('vue-router')>()),
  useRoute: () => ({ fullPath: '/documents/crm.quote/current' }),
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

  return {
    ...platform,
    NgbIcon: stub('NgbIcon'),
    NgbLookup: defineComponent({
      name: 'NgbLookup',
      emits: ['query', 'update:modelValue', 'open'],
      setup(_, { emit }) {
        return () => h('div', { 'data-stub': 'NgbLookup' }, [
          h('button', { 'data-action': 'lookup-empty', onClick: () => emit('query', ' ') }, 'empty'),
          h('button', { 'data-action': 'lookup-query', onClick: () => emit('query', 'find') }, 'query'),
          h('button', { 'data-action': 'lookup-select', onClick: () => emit('update:modelValue', { id: PRODUCT_2, label: 'Product Two' }) }, 'select'),
          h('button', { 'data-action': 'lookup-clear', onClick: () => emit('update:modelValue', null) }, 'clear'),
          h('button', { 'data-action': 'lookup-open', onClick: () => emit('open') }, 'open'),
        ])
      },
    }),
    NgbSelect: defineComponent({
      name: 'NgbSelect',
      emits: ['update:modelValue'],
      setup(_, { emit }) {
        return () => h('button', { 'data-action': 'select', onClick: () => emit('update:modelValue', 'open') }, 'select')
      },
    }),
    NgbDatePicker: defineComponent({
      name: 'NgbDatePicker',
      emits: ['update:modelValue'],
      setup(_, { emit }) {
        return () => h('button', { 'data-action': 'date', onClick: () => emit('update:modelValue', '2026-08-01') }, 'date')
      },
    }),
    NgbInput: defineComponent({
      name: 'NgbInput',
      emits: ['update:modelValue'],
      setup(_, { emit }) {
        return () => h('button', { 'data-action': 'input', onClick: () => emit('update:modelValue', '5') }, 'input')
      },
    }),
  }
})

import CRMDocumentPartsEditor from '../../src/editor/CRMDocumentPartsEditor.vue'

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

function part(partCode = 'lines', allowAddRemoveRows = true, withAmount = true): PartMetadata {
  const columns = [
    { key: 'ordinal', label: '#', dataType: 'Int32' },
    { key: 'enabled', label: 'Enabled', dataType: 'Boolean' },
    { key: 'status', label: 'Status', dataType: 'String', widthPx: 120 },
    { key: 'product_id', label: 'Product', dataType: 'Guid', lookup: { kind: 'catalog', catalogType: 'crm.product' } },
    { key: 'service_on', label: 'Service On', dataType: 'Date' },
    { key: 'created_at', label: 'Created At', dataType: 'DateTime' },
    { key: 'quantity', label: 'Quantity', dataType: 'Decimal' },
    { key: 'unit_price', label: 'Unit Price', dataType: 'Money' },
    { key: 'discount_percent', label: 'Discount', dataType: 'Decimal' },
    { key: 'memo', label: 'Memo', dataType: 'String', widthPx: 0 },
  ]
  if (withAmount) columns.push({ key: 'line_amount', label: 'Line Amount', dataType: 'Money' })
  return { partCode, title: partCode, allowAddRemoveRows, list: { columns } } as PartMetadata
}

function model(): RecordParts {
  return {
    lines: {
      rows: [
        {
          __row_key: 'row-1', enabled: true, status: 'open', product_id: { id: PRODUCT_1, display: 'Product One' },
          service_on: '2026-07-31', created_at: '2026-07-31T10:00', quantity: 2, unit_price: 10,
          discount_percent: 0, memo: 'first', line_amount: 20,
        },
        {
          __row_key: 'row-2', enabled: false, status: null, product_id: PRODUCT_2,
          service_on: null, created_at: null, quantity: 3, unit_price: 5,
          discount_percent: 0, memo: null, line_amount: 15,
        },
      ],
    },
    empty: { rows: [] },
    no_amount: { rows: [] },
    no_list: { rows: [] },
  }
}

function behavior(): MetadataFormBehavior {
  return {
    resolveFieldOptions: ({ field }) => field.key === 'status' ? [{ value: 'open', label: 'Open' }] : null,
    searchLookup: vi.fn(async () => [{ id: PRODUCT_2, label: 'Product Two' }]),
    buildLookupTargetUrl: vi.fn(async () => '/catalogs/products/1'),
  }
}

function setupState(wrapper: ReturnType<typeof mount>): Record<string, any> {
  return (wrapper.vm as any).$?.setupState
}

describe('CRMDocumentPartsEditor coverage', () => {
  beforeEach(() => mocks.push.mockClear())

  test('covers rendering, lookups, row management, derived totals, validation, and drag policies', async () => {
    const parts = [
      part(),
      part('empty', false, false),
      part('no_amount', true, false),
      { partCode: 'no_list', title: 'No list', allowAddRemoveRows: false, list: null } as PartMetadata,
    ]
    const documentModel = { amount: 0 }
    const formBehavior = behavior()
    const wrapper = mount(CRMDocumentPartsEditor, {
      attachTo: document.body,
      props: {
        entityTypeCode: 'crm.quote',
        parts,
        modelValue: model(),
        documentModel,
        behavior: formBehavior,
        errors: { lines: { 0: { memo: 'Required', status: '' }, 1: { memo: null as never, status: ' ' } } },
      },
    })
    await nextTick()
    const state = setupState(wrapper)

    expect(documentModel.amount).toBe(35)
    expect(wrapper.text()).toContain('No rows yet.')
    expect(wrapper.text()).toContain('Required')
    for (const control of wrapper.findAll('[data-action]')) await control.trigger('click')
    for (const checkbox of wrapper.findAll('input[type="checkbox"]')) {
      ;(checkbox.element as HTMLInputElement).checked = !(checkbox.element as HTMLInputElement).checked
      await checkbox.trigger('change')
    }
    const renderedRows = wrapper.findAll('tbody tr')
    if (renderedRows.length > 1) {
      const browserTransfer = new DataTransfer()
      await renderedRows[0].trigger('dragstart', { dataTransfer: browserTransfer })
      await renderedRows[1].trigger('dragover', { dataTransfer: browserTransfer })
      await renderedRows[1].trigger('drop', { dataTransfer: browserTransfer })
    }
    for (const button of wrapper.findAll('button[title="Delete"]')) await button.trigger('click')
    for (const button of wrapper.findAll('button')) if (button.text().includes('Add row')) await button.trigger('click')

    expect(state.partRows('lines')).toHaveLength(2)
    expect(state.partRows('missing')).toEqual([])
    expect(state.cloneParts()).toMatchObject(model())
    expect(state.cloneNormalizedParts().lines.rows).toHaveLength(2)
    state.emitParts(model())
    state.emitRows('lines', state.partRows('lines'))
    expect(state.createEmptyRow('lines')).toMatchObject({ enabled: false, ordinal: 3, line_amount: null })
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
    expect(state.lookupValue({ product_id: { id: PRODUCT_1, display: 'One' } }, 'product_id')).toEqual({ id: PRODUCT_1, label: 'One' })
    expect(state.lookupValue({ product_id: PRODUCT_2 }, 'product_id')).toEqual({ id: PRODUCT_2, label: PRODUCT_2 })
    expect(state.lookupValue({ product_id: {} }, 'product_id')).toBeNull()
    expect(state.parseDecimal(null)).toBeNull()
    expect(state.parseDecimal(Number.NaN)).toBeNull()
    expect(state.parseDecimal(' ')).toBeNull()
    expect(state.parseDecimal('bad')).toBeNull()
    expect(state.parseDecimal('1,234.5')).toBe(1234.5)
    expect(state.roundTo4(1.23456)).toBe(1.2346)
    expect(state.resolveLineAmountField('crm.quote')).toEqual({ quantityField: 'quantity', amountField: 'unit_price' })
    expect(state.resolveLineAmountField('crm.activity')).toBeNull()
    const unchanged = { quantity: 1 }
    expect(state.recomputeDerivedFields('crm.activity', unchanged)).toBe(unchanged)
    expect(state.recomputeDerivedFields('crm.quote', { quantity: null, unit_price: 2 }).line_amount).toBeNull()
    expect(state.recomputeDerivedFields('crm.quote', { quantity: 2, unit_price: null }).line_amount).toBeNull()
    expect(state.recomputeDerivedFields('crm.quote', { quantity: 2, unit_price: 10 }).line_amount).toBe(20)
    expect(state.recomputeDerivedFields('crm.quote', { quantity: 2, unit_price: 10, discount_percent: -5 }).line_amount).toBe(20)
    expect(state.recomputeDerivedFields('crm.quote', { quantity: 2, unit_price: 10, discount_percent: 150 }).line_amount).toBe(0)

    const lookupField = field('product_id', 'Guid', { lookup: { kind: 'catalog', catalogType: 'crm.product' } })
    await state.onLookupQuery('lines', 0, lookupField, row, null as never)
    await state.onLookupQuery('lines', 0, field('memo', 'String'), row, 'find')
    await state.onLookupQuery('lines', 0, lookupField, row, ' find ')
    expect(formBehavior.searchLookup).toHaveBeenCalledWith(expect.objectContaining({ query: 'find' }))
    state.onLookupSelect('lines', 0, 'product_id', { id: PRODUCT_2, label: 'Two' })
    state.onLookupSelect('lines', 0, 'product_id', null)
    await state.openLookup(field('memo', 'String'), row)
    await state.openLookup(lookupField, row)
    expect(mocks.push).toHaveBeenCalledWith('/catalogs/products/1')
    ;(formBehavior.buildLookupTargetUrl as ReturnType<typeof vi.fn>).mockResolvedValueOnce(null)
    await state.openLookup(lookupField, row)

    const transfer = { setData: vi.fn(), setDragImage: vi.fn(), getData: vi.fn(() => 'lines:0') }
    expect(state.canReorder('lines')).toBe(true)
    expect(state.canReorder('empty')).toBe(false)
    state.onDragStart('lines', 0, { dataTransfer: transfer } as never)
    state.onDragStart('lines', 0, { dataTransfer: { setData: () => { throw new Error('unsupported') } } } as never)
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
    expect(state.fieldColStyle('lines', field('product_id', 'Lookup', { lookup: {} }))).toBe('width:260px')
    expect(state.fieldColStyle('lines', field('enabled', 'Boolean'))).toBe('width:96px')
    expect(state.fieldColStyle('lines', field('service_on', 'Date'))).toBe('width:170px')
    expect(state.fieldColStyle('lines', field('created_at', 'DateTime'))).toBe('width:170px')
    expect(state.fieldColStyle('lines', field('quantity', 'Decimal'))).toBe('width:160px')
    expect(state.fieldColStyle('lines', field('memo', 'String'))).toBeUndefined()
    expect(state.partAmount(parts[0])).toBe(35)
    expect(state.partAmount(parts[2])).toBeNull()
    expect(state.formatAmount(null)).toMatch(/0/)
    expect(state.formatAmount(12.34567)).toMatch(/12/)

    wrapper.unmount()

    const readonly = mount(CRMDocumentPartsEditor, {
      props: { entityTypeCode: 'crm.quote', parts: [part()], modelValue: model(), readonly: true },
    })
    await nextTick()
    const readonlyState = setupState(readonly)
    expect(readonlyState.canManageRows('lines')).toBe(false)
    readonlyState.onDragStart('lines', 0, {} as never)
    readonlyState.onDrop('lines', 0, { preventDefault } as never)
    readonly.unmount()

    const blank = mount(CRMDocumentPartsEditor, { props: { entityTypeCode: 'crm.activity', parts: [part()] } })
    expect(setupState(blank).cloneParts()).toEqual({})
    blank.unmount()
  })

  test('preserves document amounts when no supported total should be synchronized', async () => {
    const quoteModel = model()
    const sameAmount = { amount: 35 }
    const same = mount(CRMDocumentPartsEditor, {
      props: { entityTypeCode: 'crm.quote', parts: [part()], modelValue: quoteModel, documentModel: sameAmount },
    })
    await nextTick()
    expect(sameAmount.amount).toBe(35)
    same.unmount()

    const noAmountField = { memo: 'keep' }
    const missing = mount(CRMDocumentPartsEditor, {
      props: { entityTypeCode: 'crm.quote', parts: [part()], modelValue: quoteModel, documentModel: noAmountField },
    })
    await nextTick()
    expect(noAmountField).toEqual({ memo: 'keep' })
    missing.unmount()

    const unsupportedAmount = { amount: 9 }
    const unsupported = mount(CRMDocumentPartsEditor, {
      props: { entityTypeCode: 'crm.activity', parts: [part('lines', true, false)], modelValue: quoteModel, documentModel: unsupportedAmount },
    })
    await nextTick()
    expect(unsupportedAmount.amount).toBe(9)
    unsupported.unmount()
  })
})
