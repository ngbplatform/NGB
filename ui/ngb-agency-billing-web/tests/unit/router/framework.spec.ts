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

vi.mock('../../../src/editor/AgencyBillingEntityEditor.vue', async () => {
  const { defineComponent } = await import('vue')
  return {
    default: defineComponent({
      template: '<div data-testid="agency-billing-entity-editor-stub"></div>',
    }),
  }
})

import { createAgencyBillingRouteFrameworkConfig } from '../../../src/router/framework'

describe('agency billing route framework', () => {
  beforeEach(() => {
    mocks.getCatalogPage.mockClear()
    mocks.getDocumentPage.mockClear()
  })

  it('loads catalog pages with the expected trash filter and naming helpers', async () => {
    const config = createAgencyBillingRouteFrameworkConfig()
    const route = config.catalogRoutes[0]
    const props = route.props as Record<string, (...args: never[]) => unknown>

    await props.loadPage({
      catalogType: 'ab.client',
      offset: 20,
      limit: 10,
      search: 'northwind',
      trashMode: 'deleted',
    })

    expect(mocks.getCatalogPage).toHaveBeenCalledWith('ab.client', {
      offset: 20,
      limit: 10,
      search: 'northwind',
      filters: { deleted: 'deleted' },
    })
    expect(props.resolveTitle('ab.client', 'Client')).toBe('Clients')
    expect(props.resolveStorageKey('ab.client')).toBe('ngb:agency-billing:catalog:ab.client')
  })

  it('loads document pages with period and ad-hoc filters merged together', async () => {
    const config = createAgencyBillingRouteFrameworkConfig()
    const route = config.documentRoutes[0]
    const props = route.props as Record<string, (...args: never[]) => unknown>

    await props.loadPage({
      documentType: 'ab.sales_invoice',
      offset: 0,
      limit: 50,
      search: 'SI-20',
      trashMode: 'active',
      periodFrom: '2026-04-01',
      periodTo: '2026-04-30',
      listFilters: {
        client_id: '11111111-1111-4111-8111-111111111111',
      },
    })

    expect(mocks.getDocumentPage).toHaveBeenCalledWith('ab.sales_invoice', {
      offset: 0,
      limit: 50,
      search: 'SI-20',
      filters: {
        deleted: 'active',
        periodFrom: '2026-04-01',
        periodTo: '2026-04-30',
        client_id: '11111111-1111-4111-8111-111111111111',
      },
    })
    expect(props.resolveLookupHint({
      entityTypeCode: 'ab.sales_invoice',
      fieldKey: 'source_timesheet_id',
      lookup: null,
    })).toEqual({ kind: 'document', documentTypes: ['ab.timesheet'] })
    expect(props.resolveTitle('ab.sales_invoice', 'Sales Invoice')).toBe('Sales Invoices')
    expect(props.resolveStorageKey('ab.sales_invoice')).toBe('ngb:agency-billing:document:ab.sales_invoice')
  })

  it('exposes the metadata-driven create and edit routes with stable paths', () => {
    const config = createAgencyBillingRouteFrameworkConfig()

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
