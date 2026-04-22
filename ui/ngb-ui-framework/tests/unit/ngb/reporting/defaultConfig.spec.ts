import { beforeEach, describe, expect, it, vi } from 'vitest'

const lookupMocks = vi.hoisted(() => ({
  buildLookupFieldTargetUrl: vi.fn(),
  useLookupStore: vi.fn(),
}))

vi.mock('../../../../src/ngb/lookup/navigation', () => ({
  buildLookupFieldTargetUrl: lookupMocks.buildLookupFieldTargetUrl,
}))

vi.mock('../../../../src/ngb/lookup/store', () => ({
  useLookupStore: lookupMocks.useLookupStore,
}))

import { createDefaultNgbReportingConfig } from '../../../../src/ngb/reporting/defaultConfig'

describe('reporting default config', () => {
  beforeEach(() => {
    lookupMocks.buildLookupFieldTargetUrl.mockReset()
    lookupMocks.useLookupStore.mockReset()
  })

  it('exposes the shared lookup store and resolves lookup targets through the lookup navigation helper', async () => {
    const store = {
      searchCatalog: vi.fn(),
      labelForCatalog: vi.fn(),
    }

    lookupMocks.useLookupStore.mockReturnValue(store)
    lookupMocks.buildLookupFieldTargetUrl.mockResolvedValueOnce('/catalogs/pm.property/riverfront')

    const config = createDefaultNgbReportingConfig()

    expect(config.useLookupStore()).toBe(store)
    await expect(config.resolveLookupTarget?.({
      hint: {
        kind: 'catalog',
        catalogType: 'pm.property',
      },
      value: 'riverfront',
      routeFullPath: '/reports/pm.occupancy.summary',
    })).resolves.toBe('/catalogs/pm.property/riverfront')

    expect(lookupMocks.buildLookupFieldTargetUrl).toHaveBeenCalledWith({
      hint: {
        kind: 'catalog',
        catalogType: 'pm.property',
      },
      value: 'riverfront',
      route: {
        fullPath: '/reports/pm.occupancy.summary',
      },
    })
  })
})
