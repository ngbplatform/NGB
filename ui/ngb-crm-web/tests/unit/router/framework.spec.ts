import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  getCatalogPage: vi.fn(async () => ({ items: [], total: 0 })),
  getDocumentPage: vi.fn(async () => ({ items: [], total: 0 })),
}))

vi.mock('@ngbplatform/ui', () => ({
  NgbMetadataCatalogEditPage: { name: 'NgbMetadataCatalogEditPage' },
  NgbMetadataCatalogListPage: { name: 'NgbMetadataCatalogListPage' },
  NgbMetadataDocumentEditPage: { name: 'NgbMetadataDocumentEditPage' },
  NgbMetadataDocumentListPage: { name: 'NgbMetadataDocumentListPage' },
  getCatalogPage: mocks.getCatalogPage,
  getDocumentPage: mocks.getDocumentPage,
  lookupHintFromSource: (lookup?: object | null) => lookup ?? null,
}))

vi.mock('vue', () => ({ defineAsyncComponent: (loader: unknown) => loader }))

vi.mock('../../../src/editor/CRMEntityEditor.vue', () => ({ default: { name: 'CRMEntityEditorStub' } }))

import { createCRMRouteFrameworkConfig } from '../../../src/router/framework'

describe('CRM route framework', () => {
  beforeEach(() => {
    mocks.getCatalogPage.mockClear()
    mocks.getDocumentPage.mockClear()
  })

  it('loads catalog pages with the expected trash filter and naming helpers', async () => {
    const props = createCRMRouteFrameworkConfig().catalogRoutes[0].props as Record<string, (...args: never[]) => unknown>

    await props.loadPage({
      catalogType: 'crm.account',
      offset: 20,
      limit: 10,
      search: 'northwind',
      trashMode: 'deleted',
    })
    const signal = new AbortController().signal
    await props.loadPage({ catalogType: 'crm.account', offset: 0, limit: 1, search: '', trashMode: 'active', signal })

    expect(mocks.getCatalogPage).toHaveBeenCalledWith('crm.account', {
      offset: 20,
      limit: 10,
      search: 'northwind',
      filters: { deleted: 'deleted' },
    })
    expect(props.resolveTitle('crm.account', 'Account')).toBe('Accounts')
    expect(props.resolveStorageKey('crm.account')).toBe('ngb:crm:catalog:crm.account')
    expect(mocks.getCatalogPage).toHaveBeenLastCalledWith('crm.account', expect.any(Object), { signal })
  })

  it('loads document pages with present and omitted period filters', async () => {
    const props = createCRMRouteFrameworkConfig().documentRoutes[0].props as Record<string, (...args: never[]) => unknown>

    await props.loadPage({
      documentType: 'crm.quote',
      offset: 0,
      limit: 50,
      search: 'Q-20',
      trashMode: 'active',
      periodFrom: '2026-04-01',
      periodTo: '2026-04-30',
      listFilters: { account_id: '11111111-1111-4111-8111-111111111111' },
    })
    await props.loadPage({
      documentType: 'crm.quote',
      offset: 50,
      limit: 50,
      search: '',
      trashMode: 'all',
      periodFrom: null,
      periodTo: null,
      listFilters: {},
    })
    const signal = new AbortController().signal
    await props.loadPage({ documentType: 'crm.quote', offset: 0, limit: 1, search: '', trashMode: 'active', periodFrom: null, periodTo: null, listFilters: {}, signal })

    expect(mocks.getDocumentPage).toHaveBeenNthCalledWith(1, 'crm.quote', {
      offset: 0,
      limit: 50,
      search: 'Q-20',
      filters: {
        deleted: 'active',
        periodFrom: '2026-04-01',
        periodTo: '2026-04-30',
        account_id: '11111111-1111-4111-8111-111111111111',
      },
    })
    expect(mocks.getDocumentPage).toHaveBeenNthCalledWith(2, 'crm.quote', {
      offset: 50,
      limit: 50,
      search: '',
      filters: { deleted: 'all' },
    })
    expect(mocks.getDocumentPage).toHaveBeenLastCalledWith('crm.quote', expect.any(Object), { signal })
    expect(props.resolveLookupHint({
      entityTypeCode: 'crm.quote',
      fieldKey: 'account_id',
      lookup: { kind: 'catalog', catalogType: 'crm.account' },
    })).toEqual({ kind: 'catalog', catalogType: 'crm.account' })
    expect(props.resolveTitle('crm.quote', 'Quote')).toBe('Quotes')
    expect(props.resolveStorageKey('crm.quote')).toBe('ngb:crm:document:crm.quote')
  })

  it('exposes metadata-driven create and edit routes with stable paths', async () => {
    const config = createCRMRouteFrameworkConfig()

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
    await expect((config.catalogRoutes[0]!.props as { editorComponent: () => Promise<unknown> }).editorComponent()).resolves.toEqual({ default: { name: 'CRMEntityEditorStub' } })
  })
})
