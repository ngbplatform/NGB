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

import { createAgencyBillingMetadataConfig, agencyBillingMetadataFormBehavior } from '../../../src/metadata/framework'

describe('agency billing metadata framework', () => {
  beforeEach(() => {
    mocks.buildLookupFieldTargetUrl.mockClear()
    mocks.getCatalogTypeMetadata.mockReset()
    mocks.getDocumentTypeMetadata.mockClear()
    mocks.searchResolvedLookupItems.mockClear()
  })

  it('normalizes client list columns into the opinionated agency order', async () => {
    mocks.getCatalogTypeMetadata.mockResolvedValue({
      catalogType: 'ab.client',
      displayName: 'Client',
      kind: 1,
      list: {
        columns: [
          { key: 'billing_contact', label: 'Billing Contact', dataType: 'String', isSortable: true, align: 1 },
          { key: 'is_active', label: 'Active', dataType: 'Boolean', isSortable: true, align: 1 },
          { key: 'status', label: 'Status', dataType: 'Int32', isSortable: true, align: 1 },
          { key: 'display', label: 'Display', dataType: 'String', isSortable: true, align: 1 },
          { key: 'client_code', label: 'Client Code', dataType: 'String', isSortable: true, align: 1 },
          { key: 'payment_terms_id', label: 'Payment Terms', dataType: 'Guid', isSortable: true, align: 1 },
          { key: 'notes', label: 'Notes', dataType: 'String', isSortable: false, align: 1 },
        ],
      },
    })

    const config = createAgencyBillingMetadataConfig()
    const metadata = await config.loadCatalogTypeMetadata('ab.client')

    expect(metadata.list?.columns.map((column) => column.key)).toEqual([
      'display',
      'client_code',
      'status',
      'billing_contact',
      'payment_terms_id',
      'is_active',
    ])
  })

  it('keeps unrelated catalog metadata untouched', async () => {
    mocks.getCatalogTypeMetadata.mockResolvedValue({
      catalogType: 'ab.accounting_policy',
      displayName: 'Accounting Policy',
      kind: 1,
      list: {
        columns: [
          { key: 'display', label: 'Display', dataType: 'String', isSortable: true, align: 1 },
          { key: 'default_currency', label: 'Default Currency', dataType: 'String', isSortable: true, align: 1 },
        ],
      },
    })

    const config = createAgencyBillingMetadataConfig()
    const metadata = await config.loadCatalogTypeMetadata('ab.accounting_policy')

    expect(metadata.list?.columns.map((column) => column.key)).toEqual(['display', 'default_currency'])
  })

  it('passes document metadata loading through to the framework registry', async () => {
    const config = createAgencyBillingMetadataConfig()

    const metadata = await config.loadDocumentTypeMetadata('ab.sales_invoice')

    expect(metadata).toEqual({ documentType: 'ab.sales_invoice' })
    expect(mocks.getDocumentTypeMetadata).toHaveBeenCalledWith('ab.sales_invoice')
  })

  it('resolves lookups, searches them, and builds target urls through the framework hooks', async () => {
    expect(agencyBillingMetadataFormBehavior.resolveLookupHint?.({
      entityTypeCode: 'ab.accounting_policy',
      model: {},
      field: {
        key: 'cash_account_id',
        label: 'Cash Account',
        dataType: 'Guid',
        uiControl: 1,
        isRequired: false,
        isReadOnly: false,
      },
    })).toEqual({ kind: 'coa' })

    const searchItems = await agencyBillingMetadataFormBehavior.searchLookup?.({
      hint: { kind: 'coa' },
      query: 'cash',
    } as never)
    const url = await agencyBillingMetadataFormBehavior.buildLookupTargetUrl?.({
      hint: { kind: 'coa' },
      value: { id: 'coa-1', display: 'Cash account' },
      routeFullPath: '/catalogs/ab.accounting_policy',
    })

    expect(searchItems).toEqual([{ id: 'coa-1', label: 'Cash account' }])
    expect(mocks.searchResolvedLookupItems).toHaveBeenCalledWith(mocks.lookupStore, { kind: 'coa' }, 'cash')
    expect(url).toBe('/jump/coa-1')
    expect(mocks.buildLookupFieldTargetUrl).toHaveBeenCalled()
  })
})
