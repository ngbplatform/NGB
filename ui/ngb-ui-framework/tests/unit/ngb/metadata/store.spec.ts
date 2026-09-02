import { createPinia, setActivePinia } from 'pinia'
import { describe, expect, it, vi } from 'vitest'

describe('metadata store', () => {
  it('shares one in-flight metadata request across concurrent callers', async () => {
    vi.resetModules()
    const config = await import('../../../../src/ngb/metadata/config')
    const { useMetadataStore } = await import('../../../../src/ngb/metadata/store')
    let resolveRequest!: (value: Record<string, unknown>) => void
    const pending = new Promise<Record<string, unknown>>((resolve) => {
      resolveRequest = resolve
    })
    const loadCatalogTypeMetadata = vi.fn().mockReturnValue(pending)

    config.configureNgbMetadata({
      loadCatalogTypeMetadata,
      loadDocumentTypeMetadata: vi.fn(),
    })
    setActivePinia(createPinia())
    const store = useMetadataStore()

    const first = store.ensureCatalogType('pm.property')
    const second = store.ensureCatalogType('pm.property')
    expect(loadCatalogTypeMetadata).toHaveBeenCalledTimes(1)

    resolveRequest({
      catalogType: 'pm.property',
      displayName: 'Properties',
      kind: 1,
      list: null,
      form: null,
      parts: null,
    })

    expect(await second).toBe(await first)
    expect(store.catalogs['pm.property']).toBe(await first)
  })

  it('loads catalog and document metadata once, normalizes them, and caches the results', async () => {
    vi.resetModules()
    const config = await import('../../../../src/ngb/metadata/config')
    const { useMetadataStore } = await import('../../../../src/ngb/metadata/store')

    const loadCatalogTypeMetadata = vi.fn().mockResolvedValue({
      catalogType: 'pm.property',
      displayName: 'Properties',
      kind: 1,
      list: {
        columns: [
          { key: 'name', label: 'Name', dataType: 1, isSortable: true, align: 1 },
        ],
      },
      form: null,
      parts: null,
    })
    const loadDocumentTypeMetadata = vi.fn().mockResolvedValue({
      documentType: 'pm.invoice',
      displayName: 'Invoices',
      kind: 2,
      list: {
        columns: [
          { key: 'posted_at', label: 'Posted At', dataType: 6, isSortable: true, align: 1 },
        ],
      },
      form: null,
      parts: null,
    })

    config.configureNgbMetadata({
      loadCatalogTypeMetadata,
      loadDocumentTypeMetadata,
    })

    setActivePinia(createPinia())
    const store = useMetadataStore()

    const catalog = await store.ensureCatalogType('pm.property')
    const catalogAgain = await store.ensureCatalogType('pm.property')
    const document = await store.ensureDocumentType('pm.invoice')
    const documentAgain = await store.ensureDocumentType('pm.invoice')

    expect(loadCatalogTypeMetadata).toHaveBeenCalledTimes(1)
    expect(loadDocumentTypeMetadata).toHaveBeenCalledTimes(1)
    expect(catalogAgain).toBe(catalog)
    expect(documentAgain).toBe(document)
    expect(catalog.list?.columns[0]?.dataType).toBe('String')
    expect(document.list?.columns[0]?.dataType).toBe('DateTime')
    expect(store.catalogs['pm.property']).toBe(catalog)
    expect(store.documents['pm.invoice']).toBe(document)
  })

  it('clears cached metadata and reloads fresh entries after reset', async () => {
    vi.resetModules()
    const config = await import('../../../../src/ngb/metadata/config')
    const { useMetadataStore } = await import('../../../../src/ngb/metadata/store')

    const loadCatalogTypeMetadata = vi.fn()
      .mockResolvedValueOnce({
        catalogType: 'pm.property',
        displayName: 'Properties',
        kind: 1,
        list: null,
        form: null,
        parts: null,
      })
      .mockResolvedValueOnce({
        catalogType: 'pm.property',
        displayName: 'Properties v2',
        kind: 1,
        list: null,
        form: null,
        parts: null,
      })
    const loadDocumentTypeMetadata = vi.fn().mockResolvedValue({
      documentType: 'pm.invoice',
      displayName: 'Invoices',
      kind: 2,
      list: null,
      form: null,
      parts: null,
    })

    config.configureNgbMetadata({
      loadCatalogTypeMetadata,
      loadDocumentTypeMetadata,
    })

    setActivePinia(createPinia())
    const store = useMetadataStore()

    const first = await store.ensureCatalogType('pm.property')
    store.clear()
    const second = await store.ensureCatalogType('pm.property')

    expect(first.displayName).toBe('Properties')
    expect(second.displayName).toBe('Properties v2')
    expect(loadCatalogTypeMetadata).toHaveBeenCalledTimes(2)
    expect(Object.keys(store.documents)).toEqual([])
  })
})
