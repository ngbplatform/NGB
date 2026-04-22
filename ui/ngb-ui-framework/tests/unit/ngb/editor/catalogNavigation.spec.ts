import { describe, expect, it, vi } from 'vitest'

describe('catalog navigation', () => {
  it('delegates catalog urls to the configured editor routing', async () => {
    vi.resetModules()
    const config = await import('../../../../src/ngb/editor/config')
    const navigation = await import('../../../../src/ngb/editor/catalogNavigation')

    config.configureNgbEditor({
      loadDocumentById: vi.fn(),
      loadDocumentEffects: vi.fn(),
      loadDocumentGraph: vi.fn(),
      loadEntityAuditLog: vi.fn(),
      routing: {
        buildCatalogListUrl: (catalogType) => `/custom/catalogs/${catalogType}`,
        buildCatalogFullPageUrl: (catalogType, id) => `/custom/catalogs/${catalogType}/${id ?? 'new'}`,
        buildCatalogCompactPageUrl: (catalogType, id) =>
          `/custom/catalogs/${catalogType}?panel=${id ? 'edit' : 'new'}${id ? `&id=${id}` : ''}`,
      },
    } as never)

    expect(navigation.buildCatalogListUrl('pm.property')).toBe('/custom/catalogs/pm.property')
    expect(navigation.buildCatalogFullPageUrl('pm.property', 'cat-1')).toBe('/custom/catalogs/pm.property/cat-1')
    expect(navigation.buildCatalogCompactPageUrl('pm.property')).toBe('/custom/catalogs/pm.property?panel=new')
  })
})
