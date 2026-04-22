import { beforeEach, describe, expect, it, vi } from 'vitest'

const apiMocks = vi.hoisted(() => ({
  httpPost: vi.fn(),
}))

vi.mock('../../../../src/ngb/api/http', () => ({
  httpPost: apiMocks.httpPost,
}))

import { searchCommandPalette } from '../../../../src/ngb/command-palette/api'

describe('command palette api', () => {
  beforeEach(() => {
    apiMocks.httpPost.mockReset()
  })

  it('posts remote search requests to the command palette endpoint', async () => {
    const signal = new AbortController().signal
    const request = {
      query: 'invoice',
      scope: 'documents' as const,
      limit: 8,
      currentRoute: '/documents/pm.invoice',
      context: {
        entityType: 'document',
        documentType: 'pm.invoice',
        entityId: 'doc-1',
      },
    }
    const response = {
      groups: [
        {
          code: 'documents' as const,
          label: 'Documents',
          items: [
            {
              key: 'pm.invoice:doc-1',
              kind: 'document' as const,
              title: 'Invoice INV-001',
              openInNewTabSupported: true,
              score: 42,
            },
          ],
        },
      ],
    }

    apiMocks.httpPost.mockResolvedValueOnce(response)

    await expect(searchCommandPalette(request, signal)).resolves.toEqual(response)
    expect(apiMocks.httpPost).toHaveBeenCalledWith('/api/search/command-palette', request, { signal })
  })
})
