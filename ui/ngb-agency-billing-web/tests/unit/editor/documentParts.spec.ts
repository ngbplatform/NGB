import { describe, expect, it, vi } from 'vitest'
import type { LookupStoreApi, PartMetadata, RecordPartRow } from 'ngb-ui-framework'

vi.mock('ngb-ui-framework', () => ({
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
  normalizeAgencyBillingDocumentPartRows,
  recomputeAgencyBillingDocumentPartRow,
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
