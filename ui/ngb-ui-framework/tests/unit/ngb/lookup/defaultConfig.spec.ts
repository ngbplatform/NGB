import { beforeEach, describe, expect, it, vi } from 'vitest'

const lookupMocks = vi.hoisted(() => ({
  buildCatalogFullPageUrl: vi.fn((catalogType: string, id: string) => `/catalogs/${catalogType}/${id}`),
  buildChartOfAccountsPath: vi.fn(({ id }: { id: string }) => `/admin/chart-of-accounts?panel=edit&id=${id}`),
  buildDocumentFullPageUrl: vi.fn((documentType: string, id: string) => `/documents/${documentType}/${id}`),
  getCatalogLookupByIds: vi.fn(),
  getCatalogPage: vi.fn(),
  getChartOfAccountById: vi.fn(),
  getChartOfAccountsByIds: vi.fn(),
  getChartOfAccountsPage: vi.fn(),
  getDocumentById: vi.fn(),
  getDocumentLookupByIds: vi.fn(),
  getDocumentPage: vi.fn(),
  lookupCatalog: vi.fn(),
  lookupDocumentsAcrossTypes: vi.fn(),
}))

vi.mock('../../../../src/ngb/api/catalogs', () => ({
  getCatalogPage: lookupMocks.getCatalogPage,
}))

vi.mock('../../../../src/ngb/api/documents', () => ({
  getDocumentById: lookupMocks.getDocumentById,
  getDocumentLookupByIds: lookupMocks.getDocumentLookupByIds,
  getDocumentPage: lookupMocks.getDocumentPage,
  lookupDocumentsAcrossTypes: lookupMocks.lookupDocumentsAcrossTypes,
}))

vi.mock('../../../../src/ngb/api/lookups', () => ({
  getCatalogLookupByIds: lookupMocks.getCatalogLookupByIds,
  lookupCatalog: lookupMocks.lookupCatalog,
}))

vi.mock('../../../../src/ngb/accounting/api', () => ({
  getChartOfAccountById: lookupMocks.getChartOfAccountById,
  getChartOfAccountsByIds: lookupMocks.getChartOfAccountsByIds,
  getChartOfAccountsPage: lookupMocks.getChartOfAccountsPage,
}))

vi.mock('../../../../src/ngb/accounting/navigation', () => ({
  buildChartOfAccountsPath: lookupMocks.buildChartOfAccountsPath,
}))

vi.mock('../../../../src/ngb/editor/catalogNavigation', () => ({
  buildCatalogFullPageUrl: lookupMocks.buildCatalogFullPageUrl,
}))

vi.mock('../../../../src/ngb/editor/documentNavigation', () => ({
  buildDocumentFullPageUrl: lookupMocks.buildDocumentFullPageUrl,
}))

import { createDefaultNgbLookupConfig } from '../../../../src/ngb/lookup/defaultConfig'

describe('default lookup config', () => {
  beforeEach(() => {
    Object.values(lookupMocks).forEach((mock) => {
      if ('mockReset' in mock) mock.mockReset()
    })

    lookupMocks.buildCatalogFullPageUrl.mockImplementation((catalogType: string, id: string) => `/catalogs/${catalogType}/${id}`)
    lookupMocks.buildChartOfAccountsPath.mockImplementation(({ id }: { id: string }) => `/admin/chart-of-accounts?panel=edit&id=${id}`)
    lookupMocks.buildDocumentFullPageUrl.mockImplementation((documentType: string, id: string) => `/documents/${documentType}/${id}`)
  })

  it('maps lookup dto payloads and chooses the lightweight catalog lookup endpoint when no filters are present', async () => {
    lookupMocks.getCatalogLookupByIds.mockResolvedValueOnce([
      {
        id: 'cat-1',
        label: 'Riverfront Tower',
        meta: {
          city: 'Boston',
          units: '20',
          manager: 'Alex',
          status: 'Open',
          ignored: 'extra',
        },
      },
    ])
    lookupMocks.lookupCatalog.mockResolvedValueOnce([
      {
        id: 'cat-2',
        label: 'North Square',
        meta: 'Portfolio',
      },
    ])

    const config = createDefaultNgbLookupConfig()

    await expect(config.loadCatalogItemsByIds('pm.property', ['cat-1'])).resolves.toEqual([
      {
        id: 'cat-1',
        label: 'Riverfront Tower',
        meta: 'city: Boston · units: 20 · manager: Alex · status: Open',
      },
    ])
    await expect(config.searchCatalog('pm.property', 'north')).resolves.toEqual([
      {
        id: 'cat-2',
        label: 'North Square',
        meta: 'Portfolio',
      },
    ])

    expect(lookupMocks.getCatalogLookupByIds).toHaveBeenCalledWith('pm.property', ['cat-1'])
    expect(lookupMocks.lookupCatalog).toHaveBeenCalledWith('pm.property', 'north', 25)
    expect(lookupMocks.getCatalogPage).not.toHaveBeenCalled()
  })

  it('uses bulk coa and document lookup endpoints while keeping legacy single-item helpers and urls available', async () => {
    lookupMocks.getChartOfAccountsByIds.mockResolvedValueOnce([
      { id: 'acc-1', label: '1010 — Cash' },
    ])
    lookupMocks.lookupDocumentsAcrossTypes.mockResolvedValueOnce([
      { id: 'doc-1', display: 'Invoice 1', documentType: 'pm.invoice', status: 1, isMarkedForDeletion: false, number: 'INV-001' },
      { id: 'doc-2', display: 'Credit Memo 2', documentType: 'pm.credit_note', status: 2, isMarkedForDeletion: false, number: 'CM-002' },
    ])
    lookupMocks.getDocumentLookupByIds.mockResolvedValueOnce([
      { id: 'doc-1', display: 'Invoice 1', documentType: 'pm.invoice', status: 1, isMarkedForDeletion: false, number: 'INV-001' },
    ])
    lookupMocks.getChartOfAccountById.mockResolvedValueOnce({
      accountId: 'acc-1',
      code: '1010',
      name: 'Cash',
    })
    lookupMocks.getDocumentById.mockResolvedValueOnce({
      id: 'doc-legacy',
      display: null,
    })

    const config = createDefaultNgbLookupConfig()

    await expect(config.loadCoaItemsByIds(['acc-1'])).resolves.toEqual([
      {
        id: 'acc-1',
        label: '1010 — Cash',
        meta: undefined,
      },
    ])
    await expect(config.searchDocumentsAcrossTypes(['pm.invoice', 'pm.credit_note'], 'invoice')).resolves.toEqual([
      {
        id: 'doc-1',
        label: 'Invoice 1',
        meta: undefined,
        documentType: 'pm.invoice',
      },
      {
        id: 'doc-2',
        label: 'Credit Memo 2',
        meta: undefined,
        documentType: 'pm.credit_note',
      },
    ])
    await expect(config.loadDocumentItemsByIds(['pm.invoice', 'pm.credit_note'], ['doc-1'])).resolves.toEqual([
      {
        id: 'doc-1',
        label: 'Invoice 1',
        meta: undefined,
        documentType: 'pm.invoice',
      },
    ])
    await expect(config.loadCoaItem('acc-1')).resolves.toEqual({
      id: 'acc-1',
      label: '1010 — Cash',
      meta: undefined,
    })
    await expect(config.loadDocumentItem('pm.invoice', 'doc-legacy')).resolves.toEqual({
      id: 'doc-legacy',
      label: 'doc-legacy',
      meta: undefined,
    })

    expect(config.buildCatalogUrl('pm.property', 'cat-1')).toBe('/catalogs/pm.property/cat-1')
    expect(config.buildCoaUrl('acc-1')).toBe('/admin/chart-of-accounts?panel=edit&id=acc-1')
    expect(config.buildDocumentUrl('pm.invoice', 'doc-1')).toBe('/documents/pm.invoice/doc-1')

    expect(lookupMocks.getChartOfAccountsByIds).toHaveBeenCalledWith(['acc-1'])
    expect(lookupMocks.lookupDocumentsAcrossTypes).toHaveBeenCalledWith({
      documentTypes: ['pm.invoice', 'pm.credit_note'],
      query: 'invoice',
      perTypeLimit: 25,
      activeOnly: true,
    })
    expect(lookupMocks.getDocumentLookupByIds).toHaveBeenCalledWith({
      documentTypes: ['pm.invoice', 'pm.credit_note'],
      ids: ['doc-1'],
    })
  })

  it('falls back to paged catalog search when filters exist and keeps single-type search helpers intact', async () => {
    lookupMocks.getCatalogPage.mockResolvedValueOnce({
      items: [
        { id: '11111111-1111-1111-1111-111111111111', display: null },
      ],
      offset: 0,
      limit: 25,
    })
    lookupMocks.getChartOfAccountsPage.mockResolvedValueOnce({
      items: [
        { accountId: 'acc-2', code: '2010', name: 'Payables' },
      ],
      offset: 0,
      limit: 25,
    })
    lookupMocks.getDocumentPage.mockResolvedValueOnce({
      items: [
        { id: 'doc-2', display: 'Invoice 2' },
      ],
      offset: 0,
      limit: 25,
    })

    const config = createDefaultNgbLookupConfig()

    await expect(config.searchCatalog('pm.property', 'river', { filters: { status: 'active' } })).resolves.toEqual([
      {
        id: '11111111-1111-1111-1111-111111111111',
        label: '11111111…1111',
      },
    ])
    await expect(config.searchCoa('pay')).resolves.toEqual([
      {
        id: 'acc-2',
        label: '2010 — Payables',
        meta: undefined,
      },
    ])
    await expect(config.searchDocument('pm.invoice', 'invoice')).resolves.toEqual([
      {
        id: 'doc-2',
        label: 'Invoice 2',
        meta: undefined,
      },
    ])

    expect(lookupMocks.getCatalogPage).toHaveBeenCalledWith('pm.property', {
      offset: 0,
      limit: 25,
      search: 'river',
      filters: {
        deleted: 'active',
        status: 'active',
      },
    })
    expect(lookupMocks.getChartOfAccountsPage).toHaveBeenCalledWith({
      search: 'pay',
      limit: 25,
      onlyActive: true,
      includeDeleted: false,
    })
    expect(lookupMocks.getDocumentPage).toHaveBeenCalledWith('pm.invoice', {
      offset: 0,
      limit: 25,
      search: 'invoice',
    })
  })
})
