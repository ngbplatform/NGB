import { describe, expect, it, vi } from 'vitest'

describe('lookup config', () => {
  it('throws when the lookup framework has not been configured yet', async () => {
    vi.resetModules()
    const config = await import('../../../../src/ngb/lookup/config')

    expect(() => config.getConfiguredNgbLookup()).toThrow(
      'NGB lookup framework is not configured. Call configureNgbLookup(...) during app bootstrap.',
    )
    expect(config.maybeGetConfiguredNgbLookup()).toBeNull()
  })

  it('returns the configured lookup framework', async () => {
    vi.resetModules()
    const config = await import('../../../../src/ngb/lookup/config')

    const frameworkConfig = {
      loadCatalogItemsByIds: vi.fn(),
      searchCatalog: vi.fn(),
      loadCoaItem: vi.fn(),
      loadCoaItemsByIds: vi.fn(),
      searchCoa: vi.fn(),
      loadDocumentItem: vi.fn(),
      loadDocumentItemsByIds: vi.fn(),
      searchDocument: vi.fn(),
      searchDocumentsAcrossTypes: vi.fn(),
      buildCatalogUrl: vi.fn(),
      buildCoaUrl: vi.fn(),
      buildDocumentUrl: vi.fn(),
    }

    config.configureNgbLookup(frameworkConfig as never)

    expect(config.getConfiguredNgbLookup()).toBe(frameworkConfig)
    expect(config.maybeGetConfiguredNgbLookup()).toBe(frameworkConfig)
  })
})
