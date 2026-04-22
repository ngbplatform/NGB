import { beforeEach, describe, expect, it, vi } from 'vitest'

const httpMocks = vi.hoisted(() => ({
  httpGet: vi.fn(),
  httpPost: vi.fn(),
}))

vi.mock('../../../../src/ngb/api/http', () => ({
  httpGet: httpMocks.httpGet,
  httpPost: httpMocks.httpPost,
}))

import { getCatalogLookupByIds, lookupCatalog } from '../../../../src/ngb/api/lookups'

describe('lookups api', () => {
  beforeEach(() => {
    httpMocks.httpGet.mockReset()
    httpMocks.httpPost.mockReset()
  })

  it('omits blank search text from lookup queries and forwards explicit limits', async () => {
    httpMocks.httpGet.mockResolvedValue([])

    await lookupCatalog('pm/property', '  ', 15)
    await lookupCatalog('pm/property', 'river', 25)

    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(
      1,
      '/api/catalogs/pm%2Fproperty/lookup',
      { limit: 15 },
    )
    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(
      2,
      '/api/catalogs/pm%2Fproperty/lookup',
      { q: 'river', limit: 25 },
    )
  })

  it('posts lookup by-id batches to the typed endpoint', async () => {
    httpMocks.httpPost.mockResolvedValue([])

    await getCatalogLookupByIds('pm/property', ['id-1', 'id-2'])

    expect(httpMocks.httpPost).toHaveBeenCalledWith(
      '/api/catalogs/pm%2Fproperty/by-ids',
      { ids: ['id-1', 'id-2'] },
    )
  })
})
