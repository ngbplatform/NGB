import { beforeEach, describe, expect, it, vi } from 'vitest'

const httpMocks = vi.hoisted(() => ({
  httpGet: vi.fn(),
  httpPost: vi.fn(),
  httpPut: vi.fn(),
}))

vi.mock('../../../../src/ngb/api/http', () => ({
  httpGet: httpMocks.httpGet,
  httpPost: httpMocks.httpPost,
  httpPut: httpMocks.httpPut,
}))

import {
  createChartOfAccount,
  getChartOfAccountById,
  getChartOfAccountsByIds,
  getChartOfAccountsMetadata,
  getChartOfAccountsPage,
  markChartOfAccountForDeletion,
  setChartOfAccountActive,
  unmarkChartOfAccountForDeletion,
  updateChartOfAccount,
} from '../../../../src/ngb/accounting/api'

describe('chart of accounts api', () => {
  beforeEach(() => {
    httpMocks.httpGet.mockReset()
    httpMocks.httpPost.mockReset()
    httpMocks.httpPut.mockReset()
  })

  it('builds chart-of-accounts list and metadata requests with normalized query strings', async () => {
    httpMocks.httpGet
      .mockResolvedValueOnce({ items: [], offset: 0, limit: 20 })
      .mockResolvedValueOnce({ accountTypeOptions: [] })
      .mockResolvedValueOnce({ accountId: 'acc/1' })

    await getChartOfAccountsPage({
      offset: 5,
      limit: 10,
      search: 'cash',
      onlyActive: true,
      includeDeleted: false,
      onlyDeleted: false,
    })
    await getChartOfAccountsPage({})
    await getChartOfAccountsMetadata()
    await getChartOfAccountById('acc/1')

    const options = { signal: new AbortController().signal }
    await getChartOfAccountsPage({}, options)
    await getChartOfAccountsMetadata(options)
    await getChartOfAccountById('acc/1', options)

    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(
      1,
      '/api/chart-of-accounts?offset=5&limit=10&search=cash&includeDeleted=false&onlyActive=true&onlyDeleted=false',
    )
    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(2, '/api/chart-of-accounts?offset=0&limit=20')
    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(3, '/api/chart-of-accounts/metadata')
    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(4, '/api/chart-of-accounts/acc%2F1')
    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(5, '/api/chart-of-accounts?offset=0&limit=20', null, options)
    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(6, '/api/chart-of-accounts/metadata', undefined, options)
    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(7, '/api/chart-of-accounts/acc%2F1', undefined, options)
  })

  it('posts bulk chart-of-accounts lookups to the by-ids endpoint', async () => {
    httpMocks.httpPost.mockResolvedValueOnce([])

    await getChartOfAccountsByIds(['acc-1', 'acc-2'])

    expect(httpMocks.httpPost).toHaveBeenCalledWith(
      '/api/chart-of-accounts/by-ids',
      { ids: ['acc-1', 'acc-2'] },
    )
  })

  it('routes create, update, active state, and deletion actions to the expected endpoints', async () => {
    const request = {
      code: '1010',
      name: 'Cash',
      accountType: 'Asset',
      isActive: true,
      cashFlowRole: null,
      cashFlowLineCode: null,
    }

    httpMocks.httpPost.mockResolvedValue({})
    httpMocks.httpPut.mockResolvedValue({})

    await createChartOfAccount(request)
    await updateChartOfAccount('acc/1', request)
    await markChartOfAccountForDeletion('acc/1')
    await unmarkChartOfAccountForDeletion('acc/1')
    await setChartOfAccountActive('acc/1', false)

    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(1, '/api/chart-of-accounts', request)
    expect(httpMocks.httpPut).toHaveBeenCalledWith('/api/chart-of-accounts/acc%2F1', request)
    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(2, '/api/chart-of-accounts/acc%2F1/mark-for-deletion')
    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(3, '/api/chart-of-accounts/acc%2F1/unmark-for-deletion')
    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(
      4,
      '/api/chart-of-accounts/acc%2F1/set-active?isActive=false',
    )
  })
})
