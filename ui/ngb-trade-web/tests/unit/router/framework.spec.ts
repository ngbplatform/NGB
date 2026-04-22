import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  getCatalogPage: vi.fn(async () => ({ items: [], total: 0 })),
  getDocumentPage: vi.fn(async () => ({ items: [], total: 0 })),
}))

vi.mock('ngb-ui-framework', () => ({
  NgbMetadataCatalogEditPage: { name: 'NgbMetadataCatalogEditPage' },
  NgbMetadataCatalogListPage: { name: 'NgbMetadataCatalogListPage' },
  NgbMetadataDocumentEditPage: { name: 'NgbMetadataDocumentEditPage' },
  NgbMetadataDocumentListPage: { name: 'NgbMetadataDocumentListPage' },
  getCatalogPage: mocks.getCatalogPage,
  getDocumentPage: mocks.getDocumentPage,
  lookupHintFromSource: (lookup?: { kind?: string; catalogType?: string; documentTypes?: string[] } | null) => lookup ?? null,
}))

import { createTradeRouteFrameworkConfig } from '../../../src/router/framework'

describe('trade route framework', () => {
  beforeEach(() => {
    mocks.getCatalogPage.mockClear()
    mocks.getDocumentPage.mockClear()
  })

  it('loads catalog pages with the expected trash filter and naming helpers', async () => {
    const config = createTradeRouteFrameworkConfig()
    const route = config.catalogRoutes[0]
    const props = route.props as Record<string, (...args: never[]) => unknown>

    await props.loadPage({
      catalogType: 'trd.party',
      offset: 20,
      limit: 10,
      search: 'bayview',
      trashMode: 'deleted',
    })

    expect(mocks.getCatalogPage).toHaveBeenCalledWith('trd.party', {
      offset: 20,
      limit: 10,
      search: 'bayview',
      filters: { deleted: 'deleted' },
    })
    expect(props.resolveTitle('trd.party', 'Party')).toBe('Parties')
    expect(props.resolveStorageKey('trd.party')).toBe('ngb:trade:catalog:trd.party')
  })

  it('loads document pages with period and ad-hoc filters merged together', async () => {
    const config = createTradeRouteFrameworkConfig()
    const route = config.documentRoutes[0]
    const props = route.props as Record<string, (...args: never[]) => unknown>

    await props.loadPage({
      documentType: 'trd.sales_invoice',
      offset: 0,
      limit: 50,
      search: 'SI-20',
      trashMode: 'active',
      periodFrom: '2026-04-01',
      periodTo: '2026-04-30',
      listFilters: {
        customer_id: '11111111-1111-4111-8111-111111111111',
      },
    })

    expect(mocks.getDocumentPage).toHaveBeenCalledWith('trd.sales_invoice', {
      offset: 0,
      limit: 50,
      search: 'SI-20',
      filters: {
        deleted: 'active',
        periodFrom: '2026-04-01',
        periodTo: '2026-04-30',
        customer_id: '11111111-1111-4111-8111-111111111111',
      },
    })
    expect(props.resolveLookupHint({
      entityTypeCode: 'trd.accounting_policy',
      fieldKey: 'cash_account_id',
      lookup: null,
    })).toEqual({ kind: 'coa' })
    expect(props.resolveTitle('trd.sales_invoice', 'Sales Invoice')).toBe('Sales Invoices')
    expect(props.resolveStorageKey('trd.sales_invoice')).toBe('ngb:trade:document:trd.sales_invoice')
  })

  it('exposes the trade create and edit routes with stable paths', () => {
    const config = createTradeRouteFrameworkConfig()

    expect(config.catalogRoutes.map((route) => route.path)).toEqual([
      '/catalogs/:catalogType',
      '/catalogs/:catalogType/new',
      '/catalogs/:catalogType/:id',
    ])
    expect(config.documentRoutes.map((route) => route.path)).toEqual([
      '/documents/:documentType',
      '/documents/:documentType/new',
      '/documents/:documentType/:id',
    ])
  })
})
