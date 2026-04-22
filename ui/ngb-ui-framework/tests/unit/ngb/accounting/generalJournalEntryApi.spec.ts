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
  approveGeneralJournalEntry,
  createGeneralJournalEntryDraft,
  getGeneralJournalEntry,
  getGeneralJournalEntryAccountContext,
  getGeneralJournalEntryPage,
  markGeneralJournalEntryForDeletion,
  postGeneralJournalEntry,
  rejectGeneralJournalEntry,
  replaceGeneralJournalEntryLines,
  reverseGeneralJournalEntry,
  submitGeneralJournalEntry,
  unmarkGeneralJournalEntryForDeletion,
  updateGeneralJournalEntryHeader,
} from '../../../../src/ngb/accounting/generalJournalEntryApi'

describe('general journal entry api', () => {
  beforeEach(() => {
    httpMocks.httpGet.mockReset()
    httpMocks.httpPost.mockReset()
    httpMocks.httpPut.mockReset()
  })

  it('builds list and read endpoints with normalized filters and encoded identifiers', async () => {
    httpMocks.httpGet
      .mockResolvedValueOnce({ items: [], offset: 0, limit: 50 })
      .mockResolvedValueOnce({ document: { id: 'entry/1' } })
      .mockResolvedValueOnce({ accountId: 'acc/1' })

    await getGeneralJournalEntryPage({
      offset: 10,
      limit: 25,
      search: 'closing',
      dateFrom: '2026-01-01',
      dateTo: '2026-01-31',
      trash: 'all',
    })
    await getGeneralJournalEntry('entry/1')
    await getGeneralJournalEntryAccountContext('acc/1')

    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(
      1,
      '/api/accounting/general-journal-entries?offset=10&limit=25&search=closing&dateFrom=2026-01-01&dateTo=2026-01-31&trash=all',
    )
    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(2, '/api/accounting/general-journal-entries/entry%2F1')
    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(
      3,
      '/api/accounting/general-journal-entries/accounts/acc%2F1',
    )
  })

  it('routes create, update, workflow, reverse, and deletion endpoints to the expected paths', async () => {
    const createRequest = { dateUtc: '2026-04-08' }
    const headerRequest = { updatedBy: 'tester', memo: 'Updated' }
    const linesRequest = { updatedBy: 'tester', lines: [] }
    const rejectRequest = { rejectReason: 'Missing support' }
    const reverseRequest = { reversalDateUtc: '2026-04-09', postImmediately: true }

    httpMocks.httpPost.mockResolvedValue({})
    httpMocks.httpPut.mockResolvedValue({})

    await createGeneralJournalEntryDraft(createRequest)
    await updateGeneralJournalEntryHeader('entry/1', headerRequest)
    await replaceGeneralJournalEntryLines('entry/1', linesRequest)
    await submitGeneralJournalEntry('entry/1', {})
    await approveGeneralJournalEntry('entry/1', {})
    await rejectGeneralJournalEntry('entry/1', rejectRequest)
    await postGeneralJournalEntry('entry/1', {})
    await reverseGeneralJournalEntry('entry/1', reverseRequest)
    await markGeneralJournalEntryForDeletion('entry/1')
    await unmarkGeneralJournalEntryForDeletion('entry/1')

    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(1, '/api/accounting/general-journal-entries', createRequest)
    expect(httpMocks.httpPut).toHaveBeenNthCalledWith(
      1,
      '/api/accounting/general-journal-entries/entry%2F1/header',
      headerRequest,
    )
    expect(httpMocks.httpPut).toHaveBeenNthCalledWith(
      2,
      '/api/accounting/general-journal-entries/entry%2F1/lines',
      linesRequest,
    )
    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(
      2,
      '/api/accounting/general-journal-entries/entry%2F1/submit',
      {},
    )
    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(
      3,
      '/api/accounting/general-journal-entries/entry%2F1/approve',
      {},
    )
    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(
      4,
      '/api/accounting/general-journal-entries/entry%2F1/reject',
      rejectRequest,
    )
    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(
      5,
      '/api/accounting/general-journal-entries/entry%2F1/post',
      {},
    )
    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(
      6,
      '/api/accounting/general-journal-entries/entry%2F1/reverse',
      reverseRequest,
    )
    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(
      7,
      '/api/accounting/general-journal-entries/entry%2F1/mark-for-deletion',
    )
    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(
      8,
      '/api/accounting/general-journal-entries/entry%2F1/unmark-for-deletion',
    )
  })
})
