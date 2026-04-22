import { describe, expect, it, vi } from 'vitest'

describe('metadata config', () => {
  it('throws when the metadata framework has not been configured yet', async () => {
    vi.resetModules()
    const config = await import('../../../../src/ngb/metadata/config')

    expect(() => config.getConfiguredNgbMetadata()).toThrow(
      'NGB metadata framework is not configured. Call configureNgbMetadata(...) during app bootstrap.',
    )
  })

  it('returns the configured metadata loaders and merges form behavior overrides', async () => {
    vi.resetModules()
    const config = await import('../../../../src/ngb/metadata/config')

    const frameworkConfig = {
      loadCatalogTypeMetadata: vi.fn(),
      loadDocumentTypeMetadata: vi.fn(),
      formBehavior: {
        findDisplayField: vi.fn(() => null),
        isFieldReadonly: vi.fn(() => true),
      },
    }

    config.configureNgbMetadata(frameworkConfig)

    expect(config.getConfiguredNgbMetadata()).toBe(frameworkConfig)

    const resolved = config.resolveNgbMetadataFormBehavior({
      isFieldHidden: vi.fn(() => true),
      isFieldReadonly: vi.fn(() => false),
    })

    expect(resolved.findDisplayField).toBe(frameworkConfig.formBehavior.findDisplayField)
    expect(resolved.isFieldHidden).toBeTypeOf('function')
    expect(resolved.isFieldReadonly).not.toBe(frameworkConfig.formBehavior.isFieldReadonly)
  })
})
