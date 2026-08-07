import { describe, expect, it, vi } from 'vitest'

import type { NgbWorkCenterConfig } from '../../../../src/ngb/work-center/config'

describe('Work Center configuration', () => {
  it('fails fast before bootstrap and returns the exact configured application gateway', async () => {
    vi.resetModules()
    const {
      configureNgbWorkCenter,
      getConfiguredNgbWorkCenter,
    } = await import('../../../../src/ngb/work-center/config')

    expect(() => getConfiguredNgbWorkCenter()).toThrow(
      'NGB Work Center is not configured. Call configureNgbWorkCenter(...) during app bootstrap.',
    )

    const config = {
      gateway: {},
      session: {},
      createRealtimeClient: vi.fn(),
    } as unknown as NgbWorkCenterConfig
    configureNgbWorkCenter(config)

    expect(getConfiguredNgbWorkCenter()).toBe(config)
  })
})
