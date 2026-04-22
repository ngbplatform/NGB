import { beforeEach, describe, expect, it, vi } from 'vitest'

const httpMocks = vi.hoisted(() => ({
  httpGet: vi.fn(),
  httpPost: vi.fn(),
}))

vi.mock('../../../../src/ngb/api/http', () => ({
  httpGet: httpMocks.httpGet,
  httpPost: httpMocks.httpPost,
}))

import {
  closeFiscalYear,
  closeMonth,
  getFiscalYearCloseStatus,
  getMonthCloseStatus,
  getPeriodClosingCalendar,
  reopenFiscalYear,
  reopenMonth,
  searchRetainedEarningsAccounts,
} from '../../../../src/ngb/accounting/periodClosingApi'

describe('period closing api', () => {
  beforeEach(() => {
    httpMocks.httpGet.mockReset()
    httpMocks.httpPost.mockReset()
  })

  it('builds read endpoints for month, fiscal year, calendar, and retained earnings lookup queries', async () => {
    httpMocks.httpGet.mockResolvedValue({})

    await getMonthCloseStatus('2026-04-01')
    await getPeriodClosingCalendar(2026)
    await getFiscalYearCloseStatus('2026-12-01')
    await searchRetainedEarningsAccounts({ query: '  equity  ', limit: 12 })
    await searchRetainedEarningsAccounts({ query: '   ' })

    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(
      1,
      '/api/accounting/period-closing/month',
      { period: '2026-04-01' },
    )
    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(
      2,
      '/api/accounting/period-closing/calendar',
      { year: 2026 },
    )
    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(
      3,
      '/api/accounting/period-closing/fiscal-year',
      { fiscalYearEndPeriod: '2026-12-01' },
    )
    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(
      4,
      '/api/accounting/period-closing/retained-earnings-accounts',
      { q: 'equity', limit: 12 },
    )
    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(
      5,
      '/api/accounting/period-closing/retained-earnings-accounts',
      { q: undefined, limit: 20 },
    )
  })

  it('posts month and fiscal-year mutations to the expected endpoints', async () => {
    const closeMonthRequest = { period: '2026-04-01' }
    const reopenMonthRequest = { period: '2026-04-01', reason: 'Reopen test' }
    const closeFiscalYearRequest = {
      fiscalYearEndPeriod: '2026-12-01',
      retainedEarningsAccountId: 'acc-1',
    }
    const reopenFiscalYearRequest = {
      fiscalYearEndPeriod: '2026-12-01',
      reason: 'Reopen year',
    }

    httpMocks.httpPost.mockResolvedValue({})

    await closeMonth(closeMonthRequest)
    await reopenMonth(reopenMonthRequest)
    await closeFiscalYear(closeFiscalYearRequest)
    await reopenFiscalYear(reopenFiscalYearRequest)

    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(
      1,
      '/api/accounting/period-closing/month/close',
      closeMonthRequest,
    )
    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(
      2,
      '/api/accounting/period-closing/month/reopen',
      reopenMonthRequest,
    )
    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(
      3,
      '/api/accounting/period-closing/fiscal-year/close',
      closeFiscalYearRequest,
    )
    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(
      4,
      '/api/accounting/period-closing/fiscal-year/reopen',
      reopenFiscalYearRequest,
    )
  })
})
