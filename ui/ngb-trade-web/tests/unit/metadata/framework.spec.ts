import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  buildLookupFieldTargetUrl: vi.fn(async ({ value }: { value: { id?: string } | string }) => `/jump/${typeof value === 'string' ? value : value.id}`),
  getCatalogTypeMetadata: vi.fn(),
  getDocumentTypeMetadata: vi.fn(async (documentType: string) => ({ documentType })),
  searchResolvedLookupItems: vi.fn(async () => [{ id: 'coa-1', label: 'Cash account' }]),
  lookupStore: { id: 'lookup-store' },
}))

vi.mock('@ngbplatform/ui', () => ({
  buildLookupFieldTargetUrl: mocks.buildLookupFieldTargetUrl,
  getCatalogTypeMetadata: mocks.getCatalogTypeMetadata,
  getDocumentTypeMetadata: mocks.getDocumentTypeMetadata,
  lookupHintFromSource: (lookup?: { kind?: string; catalogType?: string; documentTypes?: string[] } | null) => lookup ?? null,
  normalizeLookupValue: (value: unknown) => value,
  searchResolvedLookupItems: mocks.searchResolvedLookupItems,
  useLookupStore: () => mocks.lookupStore,
}))

import { getTradeLookupHint } from '../../../src/lookup/hints'
import { findDisplayField, isFieldHidden, isFieldReadonly } from '../../../src/metadata/formBehavior'
import { createTradeMetadataConfig, tradeMetadataFormBehavior } from '../../../src/metadata/framework'

const column = (key: string) => ({
  key,
  label: key,
  dataType: 'String',
  isSortable: true,
  align: 1,
})

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

  it.each([
    ['trd.item', ['display', 'sku', 'unit_of_measure_id', 'item_type']],
    ['trd.unit_of_measure', ['display', 'is_active', 'code', 'symbol']],
    ['trd.warehouse', ['display', 'name', 'warehouse_code', 'address', 'is_active']],
  ])('orders and filters %s list columns', async (catalogType, expectedKeys) => {
    mocks.getCatalogTypeMetadata.mockResolvedValue({
      catalogType,
      displayName: catalogType,
      kind: 1,
      list: { columns: [...expectedKeys].reverse().map(column).concat(column('internal')) },
    })

    const metadata = await createTradeMetadataConfig().loadCatalogTypeMetadata(catalogType)

    expect(metadata.list?.columns.map((item) => item.key)).toEqual(expectedKeys)
  })

  it('keeps missing, empty, and unknown list metadata unchanged', async () => {
    const withoutList = { catalogType: 'trd.item', displayName: 'Item', kind: 1 }
    const emptyList = { catalogType: 'trd.item', displayName: 'Item', kind: 1, list: { columns: [] } }
    const unknown = { catalogType: 'trd.custom', displayName: 'Custom', kind: 1, list: { columns: [column('display')] } }
    mocks.getCatalogTypeMetadata
      .mockResolvedValueOnce(withoutList)
      .mockResolvedValueOnce(emptyList)
      .mockResolvedValueOnce(unknown)

    await expect(createTradeMetadataConfig().loadCatalogTypeMetadata('trd.item')).resolves.toBe(withoutList)
    await expect(createTradeMetadataConfig().loadCatalogTypeMetadata('trd.item')).resolves.toBe(emptyList)
    await expect(createTradeMetadataConfig().loadCatalogTypeMetadata('trd.custom')).resolves.toBe(unknown)
  })

  it('omits item columns unavailable in metadata when no fallback exists', async () => {
    mocks.getCatalogTypeMetadata.mockResolvedValue({
      catalogType: 'trd.item',
      displayName: 'Item',
      kind: 1,
      list: { columns: [column('display')] },
    })

    const metadata = await createTradeMetadataConfig().loadCatalogTypeMetadata('trd.item')

    expect(metadata.list?.columns.map((item) => item.key)).toEqual(['display'])
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

  it('searches resolved lookup items through the shared store', async () => {
    await expect(tradeMetadataFormBehavior.searchLookup?.({ hint: { kind: 'coa' }, query: 'cash' } as never))
      .resolves.toEqual([{ id: 'coa-1', label: 'Cash account' }])
    expect(mocks.searchResolvedLookupItems).toHaveBeenCalledWith(mocks.lookupStore, { kind: 'coa' }, 'cash')
    const signal = new AbortController().signal
    await tradeMetadataFormBehavior.searchLookup?.({ hint: { kind: 'coa' }, query: 'cash', signal } as never)
    expect(mocks.searchResolvedLookupItems).toHaveBeenCalledWith(mocks.lookupStore, { kind: 'coa' }, 'cash', { signal })
  })
})
