import { afterEach, describe, expect, it, vi } from 'vitest'
import type { LookupStoreApi, PartMetadata, RecordPartRow } from '@ngbplatform/ui'

vi.mock('@ngbplatform/ui', () => ({
  buildFieldsPayload: (
    form: { sections?: Array<{ rows?: Array<{ fields?: Array<{ key: string }> }> }> },
    model: Record<string, unknown>,
  ) => {
    const payload: Record<string, unknown> = {}
    for (const section of form.sections ?? []) {
      for (const row of section.rows ?? []) {
        for (const field of row.fields ?? []) {
          payload[field.key] = model[field.key] ?? null
        }
      }
    }
    return payload
  },
  dataTypeKind: (dataType: unknown) => String(dataType),
  isNonEmptyGuid: (value: unknown) => /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(String(value).trim()),
  isReferenceValue: (value: unknown) => !!value && typeof value === 'object' && 'id' in (value as Record<string, unknown>),
  resolveLookupHint: ({
    entityTypeCode,
    model,
    field,
    behavior,
  }: {
    entityTypeCode: string
    model: Record<string, unknown>
    field: { key: string; lookup?: unknown | null }
    behavior?: { resolveLookupHint?: (args: { entityTypeCode: string; model: Record<string, unknown>; field: { key: string; lookup?: unknown | null } }) => unknown }
  }) => behavior?.resolveLookupHint?.({ entityTypeCode, model, field }) ?? field.lookup ?? null,
}))

import {
  buildAgencyBillingDocumentPartsPayload,
  calculateAgencyBillingDocumentPartAmount,
  ensureAgencyBillingDocumentPartRowKey,
  hydrateAgencyBillingDocumentPartLookupRows,
  listAgencyBillingDocumentPartFields,
  normalizeAgencyBillingDocumentPartRows,
  recomputeAgencyBillingDocumentPartRow,
  resolveAgencyBillingDocumentAmountSourceField,
  resolveAgencyBillingDocumentCostSourceField,
  syncAgencyBillingDocumentComputedFields,
} from '../../../src/editor/documentParts'

const serviceItemId = '11111111-1111-4111-8111-111111111111'
const sourceTimesheetId = '22222222-2222-4222-8222-222222222222'
const cashAccountId = '33333333-3333-4333-8333-333333333333'

const timesheetLinesPart = {
  partCode: 'lines',
  title: 'Lines',
  list: {
    columns: [
      { key: 'ordinal', label: '#', dataType: 'Int32', isSortable: true, align: 1 },
      { key: 'service_item_id', label: 'Service Item', dataType: 'Guid', isSortable: true, align: 1, lookup: { kind: 'catalog', catalogType: 'ab.service_item' } },
      { key: 'hours', label: 'Hours', dataType: 'Decimal', isSortable: true, align: 2 },
      { key: 'billable', label: 'Billable', dataType: 'Boolean', isSortable: true, align: 1 },
      { key: 'billing_rate', label: 'Billing Rate', dataType: 'Money', isSortable: true, align: 2 },
      { key: 'cost_rate', label: 'Cost Rate', dataType: 'Money', isSortable: true, align: 2 },
      { key: 'line_amount', label: 'Line Amount', dataType: 'Money', isSortable: true, align: 2 },
      { key: 'line_cost_amount', label: 'Line Cost Amount', dataType: 'Money', isSortable: true, align: 2 },
    ],
  },
} satisfies PartMetadata

const hydrationPart = {
  partCode: 'lines',
  title: 'Lines',
  list: {
    columns: [
      { key: 'ordinal', label: '#', dataType: 'Int32', isSortable: true, align: 1 },
      { key: 'service_item_id', label: 'Service Item', dataType: 'Guid', isSortable: true, align: 1, lookup: { kind: 'catalog', catalogType: 'ab.service_item' } },
      { key: 'source_timesheet_id', label: 'Source Timesheet', dataType: 'Guid', isSortable: false, align: 1, lookup: { kind: 'document', documentTypes: ['ab.timesheet'] } },
      { key: 'cash_account_id', label: 'Cash Account', dataType: 'Guid', isSortable: false, align: 1, lookup: { kind: 'coa' } },
      { key: 'line_amount', label: 'Line Amount', dataType: 'Money', isSortable: true, align: 2 },
    ],
  },
} satisfies PartMetadata

function createLookupStore(): LookupStoreApi {
  return {
    searchCatalog: vi.fn(async () => []),
    searchCoa: vi.fn(async () => []),
    searchDocuments: vi.fn(async () => []),
    ensureCatalogLabels: vi.fn(async () => undefined),
    ensureCoaLabels: vi.fn(async () => undefined),
    ensureAnyDocumentLabels: vi.fn(async () => undefined),
    labelForCatalog: vi.fn((catalogType: string, id: unknown) => `${catalogType}:${String(id)}`),
    labelForCoa: vi.fn((id: unknown) => `coa:${String(id)}`),
    labelForAnyDocument: vi.fn((_documentTypes: string[], id: unknown) => `document:${String(id)}`),
  }
}

describe('agency billing document parts', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('maps every metadata data type and handles missing lists', () => {
    const part = {
      partCode: 'all',
      title: 'All',
      list: {
        columns: [
          { key: 'ordinal', label: '#', dataType: 'Int32' },
          { key: 'flag', label: 'Flag', dataType: 'Boolean' },
          { key: 'count', label: 'Count', dataType: 'Int32' },
          { key: 'quantity', label: 'Quantity', dataType: 'Decimal' },
          { key: 'price', label: 'Price', dataType: 'Money' },
          { key: 'on', label: 'On', dataType: 'Date' },
          { key: 'at', label: 'At', dataType: 'DateTime' },
          { key: 'memo', label: 'Memo', dataType: 'String' },
        ],
      },
    } as PartMetadata
    expect(listAgencyBillingDocumentPartFields(part).map((field) => field.uiControl)).toEqual([5, 3, 3, 4, 6, 7, 1])
    expect(listAgencyBillingDocumentPartFields({ ...part, list: null as never })).toEqual([])
  })

  it('uses fallback row keys and accepts absent row collections', () => {
    vi.stubGlobal('crypto', {})
    expect(ensureAgencyBillingDocumentPartRowKey({})).toMatch(/^row_/)
    expect(normalizeAgencyBillingDocumentPartRows(null)).toEqual([])
    expect(normalizeAgencyBillingDocumentPartRows(undefined)).toEqual([])
    expect(ensureAgencyBillingDocumentPartRowKey({ __row_key: 42 })).toMatch(/^row_/)
  })

  it('resolves all amount and cost source priorities and missing sources', () => {
    const withColumns = (...keys: string[]) => ({
      partCode: 'p', title: 'P', list: { columns: keys.map((key) => ({ key, label: key, dataType: 'Decimal' })) },
    }) as PartMetadata
    expect(resolveAgencyBillingDocumentAmountSourceField(withColumns('amount', 'applied_amount', 'line_amount'))).toBe('line_amount')
    expect(resolveAgencyBillingDocumentAmountSourceField(withColumns('amount', 'applied_amount'))).toBe('applied_amount')
    expect(resolveAgencyBillingDocumentAmountSourceField(withColumns('amount'))).toBe('amount')
    expect(resolveAgencyBillingDocumentAmountSourceField(withColumns('memo'))).toBeNull()
    expect(resolveAgencyBillingDocumentCostSourceField(withColumns('cost_amount', 'line_cost_amount'))).toBe('line_cost_amount')
    expect(resolveAgencyBillingDocumentCostSourceField(withColumns('cost_amount'))).toBe('cost_amount')
    expect(resolveAgencyBillingDocumentCostSourceField(withColumns('memo'))).toBeNull()
    expect(resolveAgencyBillingDocumentAmountSourceField({ ...withColumns('amount'), list: null as never })).toBeNull()
    expect(calculateAgencyBillingDocumentPartAmount(withColumns('memo'), [])).toBeNull()
  })

  it('covers decimal parsing and every derived-row outcome', () => {
    expect(calculateAgencyBillingDocumentPartAmount(timesheetLinesPart, [
      { line_amount: null },
      { line_amount: Number.NaN },
      { line_amount: ' ' },
      { line_amount: Number.POSITIVE_INFINITY },
      { line_amount: 2 },
    ])).toBe(2)
    expect(recomputeAgencyBillingDocumentPartRow('ab.timesheet', { billable: false, hours: null, billing_rate: null, cost_rate: null }))
      .toMatchObject({ line_amount: 0, line_cost_amount: null })
    expect(recomputeAgencyBillingDocumentPartRow('ab.timesheet', { billable: true, hours: 'x', billing_rate: 2, cost_rate: 3 }))
      .toMatchObject({ line_amount: null, line_cost_amount: null })
    expect(recomputeAgencyBillingDocumentPartRow('ab.timesheet', { billable: true, hours: 2, billing_rate: 'x', cost_rate: 'x' }))
      .toMatchObject({ line_amount: null, line_cost_amount: null })
    expect(recomputeAgencyBillingDocumentPartRow('ab.sales_invoice', { quantity_hours: null, rate: 2 }).line_amount).toBeNull()
    expect(recomputeAgencyBillingDocumentPartRow('ab.sales_invoice', { quantity_hours: 2, rate: null }).line_amount).toBeNull()
    const unchanged = { memo: 'same' }
    expect(recomputeAgencyBillingDocumentPartRow('ab.customer_payment', unchanged)).toBe(unchanged)
  })

  it('skips computed synchronization when inputs or destination fields are absent', () => {
    syncAgencyBillingDocumentComputedFields({ documentType: 'ab.timesheet', partsMeta: [], partsModel: null, model: null })
    const untouched = { memo: 'x' }
    syncAgencyBillingDocumentComputedFields({ documentType: 'ab.sales_invoice', partsMeta: [], partsModel: null, model: untouched })
    expect(untouched).toEqual({ memo: 'x' })
    const noTotals = { amount: 7, total_hours: 8, cost_amount: 9 }
    syncAgencyBillingDocumentComputedFields({
      documentType: 'ab.timesheet',
      partsMeta: [{ partCode: 'memo', title: 'Memo', list: { columns: [] } } as never],
      partsModel: { memo: { rows: [{ hours: 'x' }] } },
      model: noTotals,
    })
    expect(noTotals).toEqual({ amount: 7, total_hours: 8, cost_amount: 9 })
    const undefinedMetadata = { cost_amount: 9 }
    syncAgencyBillingDocumentComputedFields({
      documentType: 'ab.timesheet', partsMeta: undefined, partsModel: {}, model: undefinedMetadata,
    })
    expect(undefinedMetadata.cost_amount).toBe(9)
    const nonTimesheet = { amount: 0, total_hours: 10, cost_amount: 20 }
    syncAgencyBillingDocumentComputedFields({
      documentType: 'ab.sales_invoice', partsMeta: [timesheetLinesPart], partsModel: { lines: { rows: [] } }, model: nonTimesheet,
    })
    expect(nonTimesheet).toEqual({ amount: 0, total_hours: 10, cost_amount: 20 })

    const absentModels = { amount: 4, total_hours: 5, cost_amount: 6 }
    syncAgencyBillingDocumentComputedFields({
      documentType: 'ab.timesheet', partsMeta: undefined, partsModel: undefined, model: absentModels,
    })
    expect(absentModels).toEqual({ amount: 4, total_hours: 5, cost_amount: 6 })

    const mixedCosts = { cost_amount: 0 }
    syncAgencyBillingDocumentComputedFields({
      documentType: 'ab.timesheet',
      partsMeta: [timesheetLinesPart],
      partsModel: { lines: { rows: [{ line_cost_amount: null }, { line_cost_amount: 'bad' }, { line_cost_amount: 2 }] } },
      model: mixedCosts,
    })
    expect(mixedCosts.cost_amount).toBe(2)
  })

  it('passes through payloads without metadata and normalizes missing rows', () => {
    const existing = { lines: { rows: [{ amount: 1 }] } }
    expect(buildAgencyBillingDocumentPartsPayload('ab.sales_invoice', null, existing)).toBe(existing)
    expect(buildAgencyBillingDocumentPartsPayload('ab.sales_invoice', [], null)).toBeNull()
    expect(buildAgencyBillingDocumentPartsPayload('ab.sales_invoice', [timesheetLinesPart], null))
      .toEqual({ lines: { rows: [] } })
  })

  it('skips lookup hydration without usable pending references and falls back to ids for blank labels', async () => {
    const lookupStore = createLookupStore()
    await hydrateAgencyBillingDocumentPartLookupRows({ entityTypeCode: 'ab.sales_invoice', partsMeta: null, partsModel: null, lookupStore })
    await hydrateAgencyBillingDocumentPartLookupRows({ entityTypeCode: 'ab.sales_invoice', partsMeta: null, partsModel: {}, lookupStore })
    await hydrateAgencyBillingDocumentPartLookupRows({
      entityTypeCode: 'ab.sales_invoice', partsMeta: [hydrationPart], partsModel: null, lookupStore,
    })
    await hydrateAgencyBillingDocumentPartLookupRows({
      entityTypeCode: 'ab.sales_invoice',
      partsMeta: [hydrationPart],
      partsModel: { lines: { rows: [{ service_item_id: serviceItemId, source_timesheet_id: 'bad', cash_account_id: null }] } },
      lookupStore,
      behavior: { resolveLookupHint: () => false as never },
    })
    expect(lookupStore.ensureCatalogLabels).not.toHaveBeenCalled()

    await hydrateAgencyBillingDocumentPartLookupRows({
      entityTypeCode: 'ab.sales_invoice', partsMeta: [hydrationPart], partsModel: {}, lookupStore,
    })

    ;(lookupStore.labelForCatalog as ReturnType<typeof vi.fn>).mockReturnValue(' ')
    const rows = [{ service_item_id: serviceItemId }]
    await hydrateAgencyBillingDocumentPartLookupRows({
      entityTypeCode: 'ab.sales_invoice', partsMeta: [hydrationPart], partsModel: { lines: { rows } }, lookupStore,
    })
    expect(rows[0].service_item_id).toEqual({ id: serviceItemId, display: serviceItemId })
  })
  it('keeps stable row keys and normalizes ordinals', () => {
    const firstRow: RecordPartRow = { service_item_id: serviceItemId }
    const secondRow: RecordPartRow = { __row_key: 'persisted-key', service_item_id: serviceItemId }

    const generatedKey = ensureAgencyBillingDocumentPartRowKey(firstRow)

    expect(ensureAgencyBillingDocumentPartRowKey(firstRow)).toBe(generatedKey)
    expect(ensureAgencyBillingDocumentPartRowKey(secondRow)).toBe('persisted-key')

    const normalized = normalizeAgencyBillingDocumentPartRows([firstRow, secondRow])
    expect(normalized[0]).toMatchObject({ __row_key: generatedKey, ordinal: 1, service_item_id: serviceItemId })
    expect(normalized[1]).toMatchObject({ __row_key: 'persisted-key', ordinal: 2, service_item_id: serviceItemId })
  })

  it('calculates part amounts from mixed numeric inputs', () => {
    expect(calculateAgencyBillingDocumentPartAmount(timesheetLinesPart, [
      { line_amount: '1,200.1055' },
      { line_amount: 9.00456 },
      { line_amount: 'oops' },
    ])).toBe(1209.1101)
  })

  it('recomputes timesheet and invoice line amounts from the business fields', () => {
    expect(recomputeAgencyBillingDocumentPartRow('ab.timesheet', {
      hours: '2.5',
      billable: true,
      billing_rate: '160',
      cost_rate: '60',
    })).toMatchObject({
      line_amount: 400,
      line_cost_amount: 150,
    })

    expect(recomputeAgencyBillingDocumentPartRow('ab.sales_invoice', {
      quantity_hours: '3',
      rate: '175',
    })).toMatchObject({
      line_amount: 525,
    })
  })

  it('syncs amount, total hours, and cost amount onto timesheet models', () => {
    const model = {
      amount: 0,
      total_hours: 0,
      cost_amount: 0,
    }

    syncAgencyBillingDocumentComputedFields({
      documentType: 'ab.timesheet',
      partsMeta: [timesheetLinesPart],
      partsModel: {
        lines: {
          rows: [
            { hours: '2.5', billable: true, billing_rate: '160', cost_rate: '60', line_amount: 400, line_cost_amount: 150 },
            { hours: '1.25', billable: false, billing_rate: '160', cost_rate: '60', line_amount: 0, line_cost_amount: 75 },
          ],
        },
      },
      model,
    })

    expect(model).toEqual({
      amount: 400,
      total_hours: 3.75,
      cost_amount: 225,
    })
  })

  it('builds payloads without synthetic row keys and with recomputed amounts', () => {
    const payload = buildAgencyBillingDocumentPartsPayload(
      'ab.timesheet',
      [timesheetLinesPart],
      {
        lines: {
          rows: [
            {
              __row_key: 'local-1',
              ordinal: 99,
              service_item_id: serviceItemId,
              hours: '2',
              billable: true,
              billing_rate: '150',
              cost_rate: '50',
            },
          ],
        },
      },
    )

    expect(payload).toEqual({
      lines: {
        rows: [
          {
            ordinal: 1,
            service_item_id: serviceItemId,
            hours: '2',
            billable: true,
            billing_rate: '150',
            cost_rate: '50',
            line_amount: 300,
            line_cost_amount: 100,
          },
        ],
      },
    })
  })

  it('hydrates catalog, document, and coa references in one deduplicated pass', async () => {
    const lookupStore = createLookupStore()
    const rows: RecordPartRow[] = [
      {
        service_item_id: serviceItemId,
        source_timesheet_id: sourceTimesheetId,
        cash_account_id: cashAccountId,
        line_amount: 12,
      },
      {
        service_item_id: serviceItemId,
        source_timesheet_id: sourceTimesheetId,
        cash_account_id: cashAccountId,
        line_amount: 18,
      },
    ]

    await hydrateAgencyBillingDocumentPartLookupRows({
      entityTypeCode: 'ab.sales_invoice',
      partsMeta: [hydrationPart],
      partsModel: {
        lines: { rows },
      },
      lookupStore,
      behavior: {
        resolveLookupHint: ({ field }) => field.lookup ?? null,
      },
    })

    expect(lookupStore.ensureCatalogLabels).toHaveBeenCalledWith('ab.service_item', [serviceItemId])
    expect(lookupStore.ensureAnyDocumentLabels).toHaveBeenCalledWith(['ab.timesheet'], [sourceTimesheetId])
    expect(lookupStore.ensureCoaLabels).toHaveBeenCalledWith([cashAccountId])

    expect(rows[0]).toMatchObject({
      service_item_id: { id: serviceItemId, display: `ab.service_item:${serviceItemId}` },
      source_timesheet_id: { id: sourceTimesheetId, display: `document:${sourceTimesheetId}` },
      cash_account_id: { id: cashAccountId, display: `coa:${cashAccountId}` },
    })
    expect(rows[1]).toMatchObject({
      service_item_id: { id: serviceItemId, display: `ab.service_item:${serviceItemId}` },
      source_timesheet_id: { id: sourceTimesheetId, display: `document:${sourceTimesheetId}` },
      cash_account_id: { id: cashAccountId, display: `coa:${cashAccountId}` },
      line_amount: 18,
    })
  })
})
