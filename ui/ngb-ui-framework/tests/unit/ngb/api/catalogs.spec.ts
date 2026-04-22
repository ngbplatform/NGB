import { beforeEach, describe, expect, it, vi } from 'vitest'

const httpMocks = vi.hoisted(() => ({
  httpDelete: vi.fn(),
  httpGet: vi.fn(),
  httpPost: vi.fn(),
  httpPut: vi.fn(),
}))

vi.mock('../../../../src/ngb/api/http', () => ({
  httpDelete: httpMocks.httpDelete,
  httpGet: httpMocks.httpGet,
  httpPost: httpMocks.httpPost,
  httpPut: httpMocks.httpPut,
}))

import {
  createCatalog,
  deleteCatalog,
  getCatalogById,
  getCatalogPage,
  getCatalogTypeMetadata,
  markCatalogForDeletion,
  unmarkCatalogForDeletion,
  updateCatalog,
} from '../../../../src/ngb/api/catalogs'

describe('catalogs api', () => {
  beforeEach(() => {
    httpMocks.httpDelete.mockReset()
    httpMocks.httpGet.mockReset()
    httpMocks.httpPost.mockReset()
    httpMocks.httpPut.mockReset()
  })

  it('loads metadata, list pages, and records with normalized page queries', async () => {
    httpMocks.httpGet
      .mockResolvedValueOnce({ code: 'pm.property' })
      .mockResolvedValueOnce({ items: [], offset: 25, limit: 50 })
      .mockResolvedValueOnce({ id: 'catalog-1' })

    await getCatalogTypeMetadata('pm/property')
    await getCatalogPage('pm/property', {
      offset: 25,
      limit: 50,
      search: 'river',
      filters: {
        deleted: 'all',
        status: 'active',
      },
    })
    await getCatalogById('pm/property', 'catalog/1')

    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(
      1,
      '/api/catalogs/pm%2Fproperty/metadata',
    )
    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(
      2,
      '/api/catalogs/pm%2Fproperty',
      {
        offset: 25,
        limit: 50,
        search: 'river',
        deleted: 'all',
        status: 'active',
      },
    )
    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(
      3,
      '/api/catalogs/pm%2Fproperty/catalog%2F1',
    )
  })

  it('routes create, update, delete, and deletion marks to the expected endpoints', async () => {
    const payload = {
      fields: {
        name: 'Riverfront Tower',
      },
    }

    httpMocks.httpPost.mockResolvedValue({})
    httpMocks.httpPut.mockResolvedValue({})
    httpMocks.httpDelete.mockResolvedValue(undefined)

    await createCatalog('pm/property', payload)
    await updateCatalog('pm/property', 'catalog/1', payload)
    await deleteCatalog('pm/property', 'catalog/1')
    await markCatalogForDeletion('pm/property', 'catalog/1')
    await unmarkCatalogForDeletion('pm/property', 'catalog/1')

    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(
      1,
      '/api/catalogs/pm%2Fproperty',
      payload,
    )
    expect(httpMocks.httpPut).toHaveBeenCalledWith(
      '/api/catalogs/pm%2Fproperty/catalog%2F1',
      payload,
    )
    expect(httpMocks.httpDelete).toHaveBeenCalledWith(
      '/api/catalogs/pm%2Fproperty/catalog%2F1',
    )
    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(
      2,
      '/api/catalogs/pm%2Fproperty/catalog%2F1/mark-for-deletion',
    )
    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(
      3,
      '/api/catalogs/pm%2Fproperty/catalog%2F1/unmark-for-deletion',
    )
  })
})
