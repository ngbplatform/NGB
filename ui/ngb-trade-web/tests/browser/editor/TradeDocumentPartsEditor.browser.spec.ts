import { mount } from '@vue/test-utils'
import { defineComponent, h, nextTick, toRaw } from 'vue'
import { beforeEach, describe, expect, test, vi } from 'vitest'
import type { FieldMetadata, MetadataFormBehavior, PartMetadata, RecordParts } from '@ngbplatform/ui'

const ITEM_1 = '11111111-1111-4111-8111-111111111111'
const ITEM_2 = '22222222-2222-4222-8222-222222222222'
const PRICE_TYPE = '33333333-3333-4333-8333-333333333333'

const mocks = vi.hoisted(() => ({
  push: vi.fn().mockResolvedValue(undefined),
  resolveDefaults: vi.fn(async () => ({ rows: [] })),
}))

vi.mock('vue-router', async (importOriginal) => ({
  ...(await importOriginal<typeof import('vue-router')>()),
  useRoute: () => ({ fullPath: '/documents/current' }),
  useRouter: () => ({ push: mocks.push }),
}))

vi.mock('../../../src/editor/tradeDocumentLineDefaultsApi', () => ({
  resolveTradeDocumentLineDefaults: mocks.resolveDefaults,
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
          h('button', { 'data-action': 'lookup-select', onClick: () => emit('update:modelValue', { id: ITEM_2, label: 'Item Two' }) }, 'select'),
          h('button', { 'data-action': 'lookup-clear', onClick: () => emit('update:modelValue', null) }, 'clear'),
          h('button', { 'data-action': 'lookup-open', onClick: () => emit('open') }, 'open'),
        ])
      },
    }),
    NgbSelect: defineComponent({
      name: 'NgbSelect',
      emits: ['update:modelValue'],
      setup(_, { emit }) {
        return () => h('button', { 'data-action': 'select', onClick: () => emit('update:modelValue', 'USD') }, 'select')
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

import TradeDocumentPartsEditor from '../../../src/editor/TradeDocumentPartsEditor.vue'

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
    { key: 'currency', label: 'Currency', dataType: 'String', widthPx: 120 },
    { key: 'item_id', label: 'Item', dataType: 'Guid', lookup: { kind: 'catalog', catalogType: 'trd.item' } },
    { key: 'price_type_id', label: 'Price Type', dataType: 'Guid', lookup: { kind: 'catalog', catalogType: 'trd.price_type' } },
    { key: 'service_on', label: 'Service On', dataType: 'Date' },
    { key: 'created_at', label: 'Created At', dataType: 'DateTime' },
    { key: 'quantity', label: 'Quantity', dataType: 'Decimal' },
    { key: 'quantity_delta', label: 'Quantity Delta', dataType: 'Decimal' },
    { key: 'unit_price', label: 'Unit Price', dataType: 'Money' },
    { key: 'unit_cost', label: 'Unit Cost', dataType: 'Money' },
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
          __row_key: 'row-1', item_id: { id: ITEM_1, display: 'Item One' }, price_type_id: null,
          enabled: true, currency: null, service_on: '2026-07-31', created_at: '2026-07-31T10:00',
          quantity: 2, quantity_delta: -2, unit_price: null, unit_cost: null, memo: 'first', line_amount: null,
        },
        {
          __row_key: 'row-2', item_id: ITEM_2, price_type_id: PRICE_TYPE,
          enabled: false, currency: 'EUR', service_on: null, created_at: null,
          quantity: 1, quantity_delta: 1, unit_price: 7, unit_cost: 4, memo: null, line_amount: 7,
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
    resolveFieldOptions: ({ field }) => field.key === 'currency' ? [{ value: 'USD', label: 'USD' }] : null,
    searchLookup: vi.fn(async () => [{ id: ITEM_2, label: 'Item Two' }]),
    buildLookupTargetUrl: vi.fn(async () => '/catalogs/items/1'),
  }
}

function setupState(wrapper: ReturnType<typeof mount>): Record<string, any> {
  return (wrapper.vm as any).$?.setupState
}

async function flush(): Promise<void> {
  await Promise.resolve()
  await nextTick()
  await Promise.resolve()
}

describe('TradeDocumentPartsEditor coverage', () => {
  beforeEach(() => {
    mocks.push.mockClear()
    mocks.resolveDefaults.mockReset()
    mocks.resolveDefaults.mockResolvedValue({ rows: [] })
  })

  test('exercises the complete grid, lookup, derived amount, and row-management contract', async () => {
    const parts = [
      part(),
      part('empty', false, false),
      part('no_amount', true, false),
      { partCode: 'no_list', title: 'No list', allowAddRemoveRows: false, list: null } as PartMetadata,
    ]
    const documentModel = { amount: 0, document_date_utc: '2026-07-31', warehouse_id: ITEM_1 }
    const formBehavior = behavior()
    const partsModel = model()
    const wrapper = mount(TradeDocumentPartsEditor, {
      attachTo: document.body,
      props: {
        entityTypeCode: 'trd.sales_invoice', parts, modelValue: partsModel, documentModel, behavior: formBehavior,
        errors: { lines: { 0: { memo: 'Required', currency: '' }, 1: { memo: null as never, currency: ' ' } } },
      },
    })
    await flush()
    const state = setupState(wrapper)

    expect(wrapper.text()).toContain('No rows yet.')
    expect(wrapper.text()).toContain('Required')
    for (const control of wrapper.findAll('[data-action]')) await control.trigger('click')
    for (const checkbox of wrapper.findAll('input[type="checkbox"]')) {
      ;(checkbox.element as HTMLInputElement).checked = false
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
    state.emitRows('lines', state.partRows('lines'))
    const structurallyShared = wrapper.emitted('update:modelValue')?.at(-1)?.[0] as RecordParts
    expect(toRaw(structurallyShared.empty)).toBe(partsModel.empty)
    expect(structurallyShared.lines).not.toBe(partsModel.lines)
    expect(state.createEmptyRow('lines')).toMatchObject({ enabled: false, ordinal: 3 })
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

    expect(state.lookupCellKey('lines', 0, 'item_id')).toBe('lines:0:item_id')
    expect(state.fieldError('lines', 0, 'memo')).toBe('Required')
    expect(state.fieldError('lines', 1, 'missing')).toBeNull()
    expect(state.rowHasErrors('lines', 0)).toBe(true)
    expect(state.rowHasErrors('lines', 1)).toBe(false)
    expect(state.rowHasErrors('empty', 0)).toBe(false)

    const row = state.partRows('lines')[0]
    expect(state.resolveFieldState(field('currency', 'String'), row).mode).toBe('select')
    expect(state.resolveFieldState(field('item_id', 'Guid', { lookup: { kind: 'catalog', catalogType: 'trd.item' } }), row).mode).toBe('lookup')
    expect(state.resolveFieldState(field('enabled', 'Boolean'), row).mode).toBe('checkbox')
    expect(state.resolveFieldState(field('service_on', 'Date'), row).mode).toBe('date')
    expect(state.resolveFieldState(field('created_at', 'DateTime'), row).inputType).toBe('datetime-local')
    expect(state.resolveFieldState(field('count', 'Int32'), row).inputType).toBe('number')
    expect(state.resolveFieldState(field('quantity', 'Decimal'), row).inputType).toBe('number')
    expect(state.resolveFieldState(field('price', 'Money'), row).inputType).toBe('number')
    expect(state.resolveFieldState(field('memo', 'String'), row).inputType).toBe('text')

    expect(state.lookupValue({ item_id: null }, 'item_id')).toBeNull()
    expect(state.lookupValue({ item_id: { id: ITEM_1, display: 'One' } }, 'item_id')).toEqual({ id: ITEM_1, label: 'One' })
    expect(state.lookupValue({ item_id: ITEM_2 }, 'item_id')).toEqual({ id: ITEM_2, label: ITEM_2 })
    expect(state.lookupValue({ item_id: {} }, 'item_id')).toBeNull()
    expect(state.parseDecimal(null)).toBeNull()
    expect(state.parseDecimal(Number.NaN)).toBeNull()
    expect(state.parseDecimal(' ')).toBeNull()
    expect(state.parseDecimal('bad')).toBeNull()
    expect(state.parseDecimal('1,234.5')).toBe(1234.5)
    expect(state.roundTo4(1.23456)).toBe(1.2346)

    const lookupField = field('item_id', 'Guid', { lookup: { kind: 'catalog', catalogType: 'trd.item' } })
    await state.onLookupQuery('lines', 0, lookupField, row, null as never)
    await state.onLookupQuery('lines', 0, field('memo', 'String'), row, 'find')
    await state.onLookupQuery('lines', 0, lookupField, row, ' find ')
    state.onLookupSelect('lines', 0, 'item_id', { id: ITEM_2, label: 'Two' })
    state.onLookupSelect('lines', 99, 'item_id', { id: ITEM_2, label: 'Two' })
    state.onLookupSelect('lines', 0, 'memo', null)
    await state.openLookup(field('memo', 'String'), row)
    await state.openLookup(lookupField, row)
    expect(mocks.push).toHaveBeenCalledWith('/catalogs/items/1')
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

    expect(state.fieldColStyle('lines', field('currency', 'String'))).toBe('width:120px')
    expect(state.fieldColStyle('lines', field('item_id', 'Lookup', { lookup: {} }))).toBe('width:260px')
    expect(state.fieldColStyle('lines', field('enabled', 'Boolean'))).toBe('width:96px')
    expect(state.fieldColStyle('lines', field('service_on', 'Date'))).toBe('width:170px')
    expect(state.fieldColStyle('lines', field('created_at', 'DateTime'))).toBe('width:170px')
    expect(state.fieldColStyle('lines', field('quantity', 'Decimal'))).toBe('width:160px')
    expect(state.fieldColStyle('lines', field('memo', 'String'))).toBeUndefined()
    expect(state.partAmount(parts[0])).toBe(7)
    expect(state.partAmount(parts[2])).toBeNull()
    expect(state.formatAmount(null)).toMatch(/0/)
    expect(state.formatAmount(12.34567)).toMatch(/12/)

    wrapper.unmount()

    const readonly = mount(TradeDocumentPartsEditor, {
      props: { entityTypeCode: 'trd.sales_invoice', parts: [part()], modelValue: model(), readonly: true },
    })
    await flush()
    const readonlyState = setupState(readonly)
    expect(readonlyState.canManageRows('lines')).toBe(false)
    readonlyState.onDragStart('lines', 0, {} as never)
    readonlyState.onDrop('lines', 0, { preventDefault } as never)
    expect(readonlyState.buildLineDefaultsRequest()).toBeNull()
    await readonlyState.refreshLineDefaults()
    readonly.unmount()

    const blank = mount(TradeDocumentPartsEditor, { props: { entityTypeCode: 'trd.inventory_transfer', parts: [part()] } })
    expect(setupState(blank).partRows('lines')).toEqual([])
    expect(setupState(blank).defaultsRefreshSignature()).toBe('')
    expect(setupState(blank).buildLineDefaultsRequest()).toBeNull()
    blank.unmount()

    const unchangedAmount = mount(TradeDocumentPartsEditor, {
      props: {
        entityTypeCode: 'trd.sales_invoice', parts: [part()], modelValue: model(), documentModel: { amount: 7 },
      },
    })
    await flush()
    expect((unchangedAmount.props('documentModel') as { amount: number }).amount).toBe(7)
    unchangedAmount.unmount()
  })

  test('covers every line-defaults policy, document type, result, race, and error path', async () => {
    const wrapper = mount(TradeDocumentPartsEditor, {
      props: {
        entityTypeCode: 'trd.item_price_update',
        parts: [part()],
        modelValue: model(),
        documentModel: { effective_date: ' 2026-07-30 ', price_type_id: PRICE_TYPE },
      },
    })
    await flush()
    const state = setupState(wrapper)

    expect(state.isBlankValue(null)).toBe(true)
    expect(state.isBlankValue(' ')).toBe(true)
    expect(state.isBlankValue('x')).toBe(false)
    expect(state.normalizeManagedValue({ id: ITEM_1, display: 'One' })).toBe(`ref:${ITEM_1}`)
    expect(state.normalizeManagedValue('1.25')).toBe('num:1.2500')
    expect(state.normalizeManagedValue(null)).toBe('')
    state.markForcedRowKey('row-1')
    expect(state.pendingForcedRowKeys.has('row-1')).toBe(true)
    expect(state.shouldOverwriteManagedField('row-1', 'unit_price', 10, true)).toBe(true)
    expect(state.shouldOverwriteManagedField('row-1', 'unit_price', null, false)).toBe(true)
    expect(state.shouldOverwriteManagedField('row-1', 'unit_price', 10, false)).toBe(false)
    state.recordManagedFieldValue('row-1', 'unit_price', 10)
    expect(state.shouldOverwriteManagedField('row-1', 'unit_price', 10, false)).toBe(true)
    expect(state.shouldOverwriteManagedField('row-1', 'unit_price', 11, false)).toBe(false)

    expect(state.resolveLineAmountField('trd.purchase_receipt')).toEqual({ quantityField: 'quantity', amountField: 'unit_cost' })
    expect(state.resolveLineAmountField('trd.vendor_return')).toEqual({ quantityField: 'quantity', amountField: 'unit_cost' })
    expect(state.resolveLineAmountField('trd.sales_invoice')).toEqual({ quantityField: 'quantity', amountField: 'unit_price' })
    expect(state.resolveLineAmountField('trd.customer_return')).toEqual({ quantityField: 'quantity', amountField: 'unit_price' })
    expect(state.resolveLineAmountField('trd.inventory_adjustment')).toEqual({ quantityField: 'quantity_delta', amountField: 'unit_cost' })
    expect(state.resolveLineAmountField('trd.item_price_update')).toBeNull()
    const unchanged = { quantity: 1 }
    expect(state.recomputeDerivedFields('trd.item_price_update', unchanged)).toBe(unchanged)
    expect(state.recomputeDerivedFields('trd.sales_invoice', { quantity: null, unit_price: 2 }).line_amount).toBeNull()
    expect(state.recomputeDerivedFields('trd.sales_invoice', { quantity: 2, unit_price: 3 }).line_amount).toBe(6)
    expect(state.recomputeDerivedFields('trd.inventory_adjustment', { quantity_delta: -2, unit_cost: 3 }).line_amount).toBe(6)

    expect(state.resolveHeaderReferenceId('price_type_id')).toBe(PRICE_TYPE)
    expect(state.resolveHeaderReferenceId('missing')).toBeNull()
    expect(state.resolveAsOfDateInput()).toBe('2026-07-30')
    const request = state.buildLineDefaultsRequest()
    expect(request.rows).toHaveLength(2)
    expect(request.rows[0]).toMatchObject({ rowKey: 'row-1', itemId: ITEM_1, priceTypeId: null })
    expect(request.rows[1]).toMatchObject({ rowKey: 'row-2', itemId: ITEM_2, priceTypeId: PRICE_TYPE })
    expect(state.defaultsRefreshSignature()).toContain('trd.item_price_update')

    state.applyResolvedDefaults([], new Set())
    state.applyResolvedDefaults([{ rowKey: 'missing', unitPrice: 1 }], new Set())
    state.applyResolvedDefaults([
      { rowKey: 'row-1', priceType: { id: PRICE_TYPE, display: 'Retail' }, unitPrice: 10, currency: 'USD', unitCost: 4 },
      { rowKey: 'row-2', priceType: null, unitPrice: null, currency: null, unitCost: null },
    ], new Set(['row-1', 'row-2']))
    expect(wrapper.emitted('update:modelValue')).toBeTruthy()

    state.autoManagedValuesByRow = {}
    state.applyResolvedDefaults([
      { rowKey: 'row-2', priceType: { id: PRICE_TYPE, display: 'Retail' }, unitPrice: 99, currency: 'USD', unitCost: 99 },
    ], new Set())

    mocks.resolveDefaults.mockResolvedValueOnce({})
    await state.refreshLineDefaults()
    mocks.resolveDefaults.mockResolvedValueOnce({ rows: [{ rowKey: 'row-1', unitPrice: 12 }] })
    await state.refreshLineDefaults()

    let resolveSlow!: (value: { rows: [] }) => void
    mocks.resolveDefaults.mockImplementationOnce(() => new Promise((resolve) => { resolveSlow = resolve }))
    const stale = state.refreshLineDefaults()
    state.defaultsRequestVersion += 1
    state.defaultsAbortController = new AbortController()
    resolveSlow({ rows: [] })
    await stale

    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    mocks.resolveDefaults.mockRejectedValueOnce(new Error('network'))
    await state.refreshLineDefaults()
    expect(consoleError).toHaveBeenCalled()

    let rejectAborted!: (error: Error) => void
    mocks.resolveDefaults.mockImplementationOnce((_request, signal: AbortSignal) => new Promise((_resolve, reject) => {
      rejectAborted = reject
      queueMicrotask(() => {
        state.defaultsAbortController.abort()
        reject(new Error(signal.aborted ? 'aborted' : 'unexpected'))
      })
    }))
    await state.refreshLineDefaults()
    expect(rejectAborted).toBeTypeOf('function')
    consoleError.mockRestore()

    wrapper.unmount()

    for (const entityTypeCode of ['trd.purchase_receipt', 'trd.sales_invoice', 'trd.inventory_adjustment', 'trd.customer_return', 'trd.vendor_return']) {
      const current = mount(TradeDocumentPartsEditor, {
        props: {
          entityTypeCode, parts: [part()], modelValue: model(),
          documentModel: { document_date_utc: null, warehouse_id: null, price_type_id: null, sales_invoice_id: null, purchase_receipt_id: null },
        },
      })
      await flush()
      const currentState = setupState(current)
      expect(currentState.resolveAsOfDateInput()).toBeNull()
      expect(currentState.buildLineDefaultsRequest()?.documentType).toBe(entityTypeCode)
      expect(currentState.defaultsRefreshSignature()).toContain(entityTypeCode)
      current.unmount()
    }

    const noItemsModel = { lines: { rows: [{ __row_key: 'blank', item_id: null }] } }
    const noItems = mount(TradeDocumentPartsEditor, {
      props: { entityTypeCode: 'trd.sales_invoice', parts: [part()], modelValue: noItemsModel },
    })
    await flush()
    expect(setupState(noItems).buildLineDefaultsRequest()).toBeNull()
    noItems.unmount()
  })
})
