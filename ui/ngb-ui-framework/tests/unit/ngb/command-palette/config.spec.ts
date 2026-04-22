import { describe, expect, it, vi } from 'vitest'

describe('command palette config', () => {
  it('throws when the command palette has not been configured yet', async () => {
    vi.resetModules()
    const config = await import('../../../../src/ngb/command-palette/config')

    expect(() => config.getConfiguredNgbCommandPalette()).toThrow(
      'NGB command palette is not configured. Call configureNgbCommandPalette(...) during app bootstrap.',
    )
  })

  it('returns the configured command palette store config', async () => {
    vi.resetModules()
    const config = await import('../../../../src/ngb/command-palette/config')

    const frameworkConfig = {
      router: { push: vi.fn(), replace: vi.fn() },
      recentStorageKey: 'ngb:test:command-palette',
      searchRemote: vi.fn(),
    }

    config.configureNgbCommandPalette(frameworkConfig as never)

    expect(config.getConfiguredNgbCommandPalette()).toBe(frameworkConfig)
  })
})
