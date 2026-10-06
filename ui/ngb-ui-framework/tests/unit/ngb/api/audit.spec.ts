import { beforeEach, describe, expect, it, vi } from 'vitest'

const httpMocks = vi.hoisted(() => ({
  httpGet: vi.fn(),
  httpPost: vi.fn(),
}))

vi.mock('../../../../src/ngb/api/http', () => ({
  httpGet: httpMocks.httpGet,
  httpPost: httpMocks.httpPost,
}))

import { downloadAuditAttachment, getEntityAuditLog } from '../../../../src/ngb/api/audit'

describe('audit api', () => {
  beforeEach(() => {
    httpMocks.httpGet.mockReset()
  })

  it('requests retained downloads through the audit endpoint with cancellation', async () => {
    const signal = new AbortController().signal
    const result = { url: 'https://storage.test/retained', expiresAtUtc: '2026-10-02T12:00:00Z' }
    httpMocks.httpPost.mockResolvedValueOnce(result)

    expect(await downloadAuditAttachment('attachment/id', signal)).toEqual(result)
    expect(httpMocks.httpPost).toHaveBeenCalledWith(
      '/api/attachments/attachment%2Fid/audit-download', undefined, { signal },
    )
  })

  it('encodes entity identifiers and forwards paging options', async () => {
    httpMocks.httpGet.mockResolvedValueOnce({ items: [], offset: 0, limit: 50 })

    await getEntityAuditLog(2, 'doc/1', {
      afterOccurredAtUtc: '2026-04-08T12:00:00Z',
      afterAuditEventId: 'evt/1',
      limit: 25,
    })

    expect(httpMocks.httpGet).toHaveBeenCalledWith(
      '/api/audit/entities/2/doc%2F1',
      {
        afterOccurredAtUtc: '2026-04-08T12:00:00Z',
        afterAuditEventId: 'evt/1',
        limit: 25,
      },
    )

    const signal = new AbortController().signal
    await getEntityAuditLog(2, 'doc/1', { signal })
    expect(httpMocks.httpGet).toHaveBeenLastCalledWith(
      '/api/audit/entities/2/doc%2F1',
      { afterOccurredAtUtc: undefined, afterAuditEventId: undefined, limit: undefined },
      { signal },
    )
  })
})
