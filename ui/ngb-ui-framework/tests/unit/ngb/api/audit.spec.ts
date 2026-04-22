import { beforeEach, describe, expect, it, vi } from 'vitest'

const httpMocks = vi.hoisted(() => ({
  httpGet: vi.fn(),
}))

vi.mock('../../../../src/ngb/api/http', () => ({
  httpGet: httpMocks.httpGet,
}))

import { getEntityAuditLog } from '../../../../src/ngb/api/audit'

describe('audit api', () => {
  beforeEach(() => {
    httpMocks.httpGet.mockReset()
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
  })
})
