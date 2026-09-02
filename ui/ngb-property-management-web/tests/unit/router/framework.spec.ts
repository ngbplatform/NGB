import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  catalogPage: vi.fn(async () => ({ items: [], total: 0 })),
  documentPage: vi.fn(async () => ({ items: [], total: 0 })),
  lookupHint: vi.fn(() => ({ kind: 'catalog' })),
  catalogTitle: vi.fn((type: string, display: string) => `catalog:${type}:${display}`),
  documentTitle: vi.fn((type: string, display: string) => `document:${type}:${display}`),
}))

vi.mock('vue', () => ({
  defineAsyncComponent: (loader: unknown) => loader,
  markRaw: (value: unknown) => value,
}))
vi.mock('@ngbplatform/ui', () => ({
  NgbMetadataCatalogEditPage: { name: 'CatalogEdit' },
  NgbMetadataCatalogListPage: { name: 'CatalogList' },
  NgbMetadataDocumentEditPage: { name: 'DocumentEdit' },
  NgbMetadataDocumentListPage: { name: 'DocumentList' },
  getCatalogPage: mocks.catalogPage,
  getDocumentPage: mocks.documentPage,
}))
vi.mock('../../../src/editor/pm/PmEntityEditor.vue', () => ({ default: { name: 'PmEditor' } }))
vi.mock('../../../src/lookup/hints', () => ({ getLookupHint: mocks.lookupHint }))
vi.mock('../../../src/utils/entityCollectionTitles', () => ({
  catalogCollectionTitle: mocks.catalogTitle,
  documentCollectionTitle: mocks.documentTitle,
}))

import { createPmRouteFrameworkConfig } from '../../../src/router/framework'

describe('property-management route framework', () => {
  beforeEach(() => vi.clearAllMocks())

  it('loads catalog pages and handles every bulk-create action state', async () => {
    const config = createPmRouteFrameworkConfig()
    const props = config.catalogRoutes[0]!.props as Record<string, (...args: never[]) => unknown>
    await props.loadPage({ catalogType: 'pm.property', offset: 10, limit: 20, search: 'main', trashMode: 'deleted' })
    expect(mocks.catalogPage).toHaveBeenCalledWith('pm.property', {
      offset: 10, limit: 20, search: 'main', filters: { deleted: 'deleted' },
    })
    expect(props.resolveTitle('pm.property', 'Property')).toBe('catalog:pm.property:Property')
    expect(props.resolveStorageKey('pm.property')).toBe('pm:catalog:pm.property')

    expect(props.resolveDrawerExtraActions({ editorFlags: {} })).toEqual([])
    expect(props.resolveDrawerExtraActions({ editorFlags: { extras: { bulkCreateUnits: false } } })).toEqual([])
    expect(props.resolveDrawerExtraActions({ editorFlags: { extras: { bulkCreateUnits: true }, loading: false, saving: false } }))
      .toEqual([{ key: 'bulkCreateUnits', title: 'Bulk create units', icon: 'grid', disabled: false }])
    expect(props.resolveDrawerExtraActions({ editorFlags: { extras: { bulkCreateUnits: true }, loading: true, saving: false } })[0].disabled).toBe(true)
    expect(props.resolveDrawerExtraActions({ editorFlags: { extras: { bulkCreateUnits: true }, loading: false, saving: true } })[0].disabled).toBe(true)

    const open = vi.fn()
    expect(props.handleDrawerExtraAction({ action: 'other', editor: { openBulkCreateUnitsWizard: open } })).toBe(false)
    expect(props.handleDrawerExtraAction({ action: 'bulkCreateUnits', editor: null })).toBe(true)
    expect(props.handleDrawerExtraAction({ action: 'bulkCreateUnits', editor: { openBulkCreateUnitsWizard: 'no' } })).toBe(true)
    expect(props.handleDrawerExtraAction({ action: 'bulkCreateUnits', editor: { openBulkCreateUnitsWizard: open } })).toBe(true)
    expect(open).toHaveBeenCalledOnce()
  })

  it('loads document pages with optional periods and resolves list behavior', async () => {
    const props = createPmRouteFrameworkConfig().documentRoutes[0]!.props as Record<string, (...args: never[]) => unknown>
    await props.loadPage({
      documentType: 'pm.lease', offset: 0, limit: 50, search: 'lease', trashMode: 'active',
      periodFrom: '2026-01-01', periodTo: '2026-12-31', listFilters: { party_id: 'party-1' },
    })
    await props.loadPage({
      documentType: 'pm.lease', offset: 50, limit: 50, search: '', trashMode: 'all',
      periodFrom: null, periodTo: null, listFilters: {},
    })
    expect(mocks.documentPage).toHaveBeenNthCalledWith(1, 'pm.lease', {
      offset: 0, limit: 50, search: 'lease',
      filters: { deleted: 'active', periodFrom: '2026-01-01', periodTo: '2026-12-31', party_id: 'party-1' },
    })
    expect(mocks.documentPage).toHaveBeenNthCalledWith(2, 'pm.lease', {
      offset: 50, limit: 50, search: '', filters: { deleted: 'all' },
    })
    expect(props.resolveLookupHint({ entityTypeCode: 'pm.lease', fieldKey: 'party_id', lookup: null })).toEqual({ kind: 'catalog' })
    expect(props.resolveTitle('pm.lease', 'Lease')).toBe('document:pm.lease:Lease')
    expect(props.resolveStorageKey('pm.lease')).toBe('pm:document:pm.lease')
  })

  it('covers apply warnings, disabled states, and create overrides', async () => {
    const props = createPmRouteFrameworkConfig().documentRoutes[0]!.props as Record<string, (...args: never[]) => unknown>
    expect(props.resolveWarning('pm.payable_apply')).toContain('payables Apply flow')
    expect(props.resolveWarning('pm.receivable_apply')).toContain('receivables Apply flow')
    expect(props.resolveWarning('pm.lease')).toBeNull()
    expect(props.isCreateDisabled('pm.receivable_apply')).toBe(true)
    expect(props.isCreateDisabled('pm.payable_apply')).toBe(true)
    expect(props.isCreateDisabled('pm.lease')).toBe(false)

    const router = { push: vi.fn().mockResolvedValue(undefined) }
    await expect(props.handleCreateOverride({ documentType: 'pm.receivable_apply', router })).resolves.toBe(true)
    await expect(props.handleCreateOverride({ documentType: 'pm.payable_apply', router })).resolves.toBe(true)
    await expect(props.handleCreateOverride({ documentType: 'pm.lease', router })).resolves.toBe(false)
    expect(router.push.mock.calls).toEqual([['/receivables/open-items'], ['/payables/open-items']])
  })

  it('exposes stable catalog and document route surfaces', () => {
    const config = createPmRouteFrameworkConfig()
    expect(config.catalogRoutes.map((route) => route.path)).toEqual([
      '/catalogs/:catalogType', '/catalogs/:catalogType/new', '/catalogs/:catalogType/:id',
    ])
    expect(config.documentRoutes.map((route) => route.path)).toEqual([
      '/documents/:documentType', '/documents/pm.receivable_apply/new', '/documents/pm.payable_apply/new',
      '/documents/:documentType/new', '/documents/:documentType/:id',
    ])
  })
})
