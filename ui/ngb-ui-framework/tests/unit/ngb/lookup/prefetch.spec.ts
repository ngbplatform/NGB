import { beforeEach, describe, expect, it, vi } from 'vitest'

const filteringMocks = vi.hoisted(() => ({
  ensureResolvedLookupLabels: vi.fn(),
}))

vi.mock('../../../../src/ngb/metadata/filtering', () => ({
  ensureResolvedLookupLabels: filteringMocks.ensureResolvedLookupLabels,
}))

import { prefetchLookupsForPage } from '../../../../src/ngb/lookup/prefetch'

describe('lookup prefetch', () => {
  beforeEach(() => {
    filteringMocks.ensureResolvedLookupLabels.mockReset()
  })

  it('prefetches only columns that resolve to lookup hints and only forwards non-empty guid values', async () => {
    const resolveLookupHint = vi.fn((entityTypeCode: string, fieldKey: string) => {
      if (entityTypeCode === 'pm.invoice' && fieldKey === 'propertyId') {
        return {
          kind: 'catalog',
          catalogType: 'pm.property',
        }
      }

      return null
    })

    await prefetchLookupsForPage({
      entityTypeCode: 'pm.invoice',
      columns: [
        { key: 'propertyId', lookup: { kind: 'catalog', catalogType: 'pm.property' } },
        { key: 'memo' },
      ],
      items: [
        { payload: { fields: { propertyId: '11111111-1111-1111-1111-111111111111' } } },
        { payload: { fields: { propertyId: 'not-a-guid' } } },
        { payload: { fields: { propertyId: '22222222-2222-2222-2222-222222222222' } } },
      ],
      lookupStore: {} as never,
      resolveLookupHint,
    })

    expect(resolveLookupHint).toHaveBeenCalledTimes(2)
    expect(filteringMocks.ensureResolvedLookupLabels).toHaveBeenCalledWith(
      {},
      {
        kind: 'catalog',
        catalogType: 'pm.property',
      },
      [
        '11111111-1111-1111-1111-111111111111',
        '22222222-2222-2222-2222-222222222222',
      ],
    )
  })

  it('skips prefetch work when no lookup hint or guid values are available', async () => {
    await prefetchLookupsForPage({
      entityTypeCode: 'pm.invoice',
      columns: [{ key: 'memo' }],
      items: [{ payload: { fields: { memo: 'hello' } } }],
      lookupStore: {} as never,
      resolveLookupHint: () => null,
    })

    expect(filteringMocks.ensureResolvedLookupLabels).not.toHaveBeenCalled()
  })
})
