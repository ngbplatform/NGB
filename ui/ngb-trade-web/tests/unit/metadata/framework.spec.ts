import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  buildLookupFieldTargetUrl: vi.fn(async ({ value }: { value: { id?: string } | string }) => `/jump/${typeof value === 'string' ? value : value.id}`),
  getCatalogTypeMetadata: vi.fn(),
  getDocumentTypeMetadata: vi.fn(async (documentType: string) => ({ documentType })),
  searchResolvedLookupItems: vi.fn(async () => [{ id: 'coa-1', label: 'Cash account' }]),
  lookupStore: { id: 'lookup-store' },
}))

vi.mock('ngb-ui-framework', () => ({
  buildLookupFieldTargetUrl: mocks.buildLookupFieldTargetUrl,
  getCatalogTypeMetadata: mocks.getCatalogTypeMetadata,
  getDocumentTypeMetadata: mocks.getDocumentTypeMetadata,
  lookupHintFromSource: (lookup?: { kind?: string; catalogType?: string; documentTypes?: string[] } | null) => lookup ?? null,
  searchResolvedLookupItems: mocks.searchResolvedLookupItems,
  useLookupStore: () => mocks.lookupStore,
}))

import { getTradeLookupHint } from '../../../src/lookup/hints'
import { findDisplayField, isFieldHidden, isFieldReadonly } from '../../../src/metadata/formBehavior'
import { createTradeMetadataConfig, tradeMetadataFormBehavior } from '../../../src/metadata/framework'

describe('trade metadata framework', () => {
  beforeEach(() => {
    mocks.buildLookupFieldTargetUrl.mockClear()
    mocks.getCatalogTypeMetadata.mockReset()
    mocks.getDocumentTypeMetadata.mockClear()
    mocks.searchResolvedLookupItems.mockClear()
  })

  it('marks computed and structural fields as readonly or hidden', () => {
    expect(isFieldReadonly({
      entityTypeCode: 'trd.sales_invoice',
      field: { key: 'amount', label: 'Amount', dataType: 'Money', uiControl: 4, isRequired: false, isReadOnly: false },
    })).toBe(true)

    expect(isFieldHidden({
      entityTypeCode: 'trd.item',
      field: { key: 'name', label: 'Name', dataType: 'String', uiControl: 1, isRequired: false, isReadOnly: false },
      isDocumentEntity: false,
    })).toBe(true)

    expect(isFieldHidden({
      entityTypeCode: 'trd.sales_invoice',
      field: { key: 'display', label: 'Display', dataType: 'String', uiControl: 1, isRequired: false, isReadOnly: false },
      isDocumentEntity: true,
    })).toBe(true)
  })

  it('finds the display field anywhere in the form tree', () => {
    expect(findDisplayField({
      sections: [
        {
          title: 'Main',
          rows: [
            { fields: [{ key: 'number', label: 'Number', dataType: 'String', uiControl: 1, isRequired: false, isReadOnly: false }] },
            { fields: [{ key: 'display', label: 'Display', dataType: 'String', uiControl: 1, isRequired: false, isReadOnly: false }] },
          ],
        },
      ],
    })?.label).toBe('Display')
  })

  it('normalizes trade catalog list columns and supplies party fallbacks', async () => {
    mocks.getCatalogTypeMetadata.mockResolvedValue({
      catalogType: 'trd.party',
      displayName: 'Party',
      kind: 1,
      list: {
        columns: [
          { key: 'party_number', label: 'Party Number', dataType: 'String', isSortable: true, align: 1 },
          { key: 'display', label: 'Display', dataType: 'String', isSortable: true, align: 1 },
        ],
      },
    })

    const config = createTradeMetadataConfig()
    const metadata = await config.loadCatalogTypeMetadata('trd.party')

    expect(metadata.list?.columns.map((column) => column.key)).toEqual([
      'display',
      'party_number',
      'is_customer',
      'is_vendor',
      'is_active',
    ])
  })

  it('resolves explicit and inferred lookup hints', async () => {
    expect(getTradeLookupHint('trd.accounting_policy', 'cash_account_id')).toEqual({ kind: 'coa' })
    expect(tradeMetadataFormBehavior.resolveLookupHint?.({
      entityTypeCode: 'trd.item',
      model: {},
      field: {
        key: 'unit_of_measure_id',
        label: 'Unit of Measure',
        dataType: 'Guid',
        uiControl: 1,
        isRequired: true,
        isReadOnly: false,
        lookup: { kind: 'catalog', catalogType: 'trd.unit_of_measure' },
      },
    })).toEqual({ kind: 'catalog', catalogType: 'trd.unit_of_measure' })

    const url = await tradeMetadataFormBehavior.buildLookupTargetUrl?.({
      hint: { kind: 'coa' },
      value: { id: 'coa-1', display: 'Cash account' },
      routeFullPath: '/catalogs/trd.accounting_policy',
    })

    expect(url).toBe('/jump/coa-1')
    expect(mocks.buildLookupFieldTargetUrl).toHaveBeenCalled()
  })
})
