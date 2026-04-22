import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const authMocks = vi.hoisted(() => ({
  forceRefreshAccessToken: vi.fn(),
  getAccessToken: vi.fn(),
}))

vi.mock('../../../../src/ngb/auth/keycloak', () => ({
  forceRefreshAccessToken: authMocks.forceRefreshAccessToken,
  getAccessToken: authMocks.getAccessToken,
}))

import { ApiError, httpGet, httpPostFile, httpRequest } from '../../../../src/ngb/api/http'

describe('api http', () => {
  const fetchMock = vi.fn()

  beforeEach(() => {
    authMocks.getAccessToken.mockReset()
    authMocks.forceRefreshAccessToken.mockReset()
    fetchMock.mockReset()

    vi.stubGlobal('fetch', fetchMock)
    vi.stubGlobal('window', {
      location: new URL('https://app.example/app/home?tab=dashboard'),
    })
  })

  afterEach(() => {
    vi.unstubAllEnvs()
    vi.unstubAllGlobals()
  })

  it('uses the configured api base url, appends normalized query params, and sends bearer auth headers', async () => {
    vi.stubEnv('VITE_API_BASE_URL', 'https://api.example')

    authMocks.getAccessToken.mockResolvedValueOnce('token-1')
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({ ok: true }), {
      status: 200,
      headers: {
        'content-type': 'application/json',
      },
    }))

    await expect(httpGet('/api/catalogs', {
      offset: 10,
      limit: 25,
      search: 'river',
      includeDeleted: false,
      blank: '',
      skipped: null,
      omitted: undefined,
    })).resolves.toEqual({ ok: true })

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock).toHaveBeenCalledWith(
      'https://api.example/api/catalogs?offset=10&limit=25&search=river&includeDeleted=false',
      expect.objectContaining({
        method: 'GET',
        credentials: 'omit',
        headers: {
          Accept: 'application/json',
          Authorization: 'Bearer token-1',
        },
      }),
    )
  })

  it('refreshes the access token once after a 401 and retries the request', async () => {
    authMocks.getAccessToken
      .mockResolvedValueOnce('expired-token')
      .mockResolvedValueOnce('fresh-token')
    authMocks.forceRefreshAccessToken.mockResolvedValueOnce('fresh-token')

    fetchMock
      .mockResolvedValueOnce(new Response(JSON.stringify({ detail: 'Unauthorized' }), {
        status: 401,
        headers: {
          'content-type': 'application/json',
        },
      }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ok: true }), {
        status: 200,
        headers: {
          'content-type': 'application/json',
        },
      }))

    await expect(httpRequest('POST', '/api/documents/test/post', { posted: true })).resolves.toEqual({ ok: true })

    expect(authMocks.forceRefreshAccessToken).toHaveBeenCalledTimes(1)
    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(fetchMock.mock.calls[1]?.[1]).toEqual(expect.objectContaining({
      method: 'POST',
      headers: expect.objectContaining({
        Authorization: 'Bearer fresh-token',
      }),
    }))
  })

  it('surfaces normalized api validation metadata through ApiError', async () => {
    authMocks.getAccessToken.mockResolvedValueOnce(null)
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({
      title: 'Validation failed',
      detail: 'One or more validation errors has occurred.',
      errorCode: 'NGB_VALIDATION',
      kind: 'validation',
      context: {
        entityType: 'pm.invoice',
      },
      errors: {
        'payload.customerId': ['Customer is required'],
        'parts.lines.rows[2].amount': ['Amount must be positive'],
      },
    }), {
      status: 400,
      headers: {
        'content-type': 'application/problem+json',
      },
    }))

    let error: ApiError | null = null

    try {
      await httpRequest('PUT', '/api/documents/pm.invoice/doc-1', { customerId: null })
    } catch (thrown) {
      error = thrown as ApiError
    }

    expect(error).toBeInstanceOf(ApiError)
    expect(error?.message).toBe('Customer is required')
    expect(error?.status).toBe(400)
    expect(error?.errorCode).toBe('NGB_VALIDATION')
    expect(error?.kind).toBe('validation')
    expect(error?.context).toEqual({ entityType: 'pm.invoice' })
    expect(error?.errors).toEqual({
      'payload.customerId': ['Customer is required'],
      'parts.lines.rows[2].amount': ['Amount must be positive'],
    })
    expect(error?.issues).toEqual([
      {
        path: 'customerId',
        message: 'Customer is required',
        scope: 'field',
        code: null,
      },
      {
        path: 'lines[2].amount',
        message: 'Amount must be positive',
        scope: 'field',
        code: null,
      },
    ])
  })

  it('throws a descriptive ApiError when a successful response is not json', async () => {
    authMocks.getAccessToken.mockResolvedValueOnce(null)
    fetchMock.mockResolvedValueOnce(new Response('plain text body', {
      status: 200,
      headers: {
        'content-type': 'text/plain',
      },
    }))

    let error: ApiError | null = null

    try {
      await httpRequest('GET', '/api/health')
    } catch (thrown) {
      error = thrown as ApiError
    }

    expect(error).toBeInstanceOf(ApiError)
    expect(error?.status).toBe(200)
    expect(error?.message).toContain("Expected JSON but got 'text/plain'")
    expect(error?.body).toBe('plain text body')
  })

  it('downloads files, parses utf8 filenames, and retries once after unauthorized responses', async () => {
    authMocks.getAccessToken
      .mockResolvedValueOnce('stale-token')
      .mockResolvedValueOnce('fresh-token')
    authMocks.forceRefreshAccessToken.mockResolvedValueOnce('fresh-token')

    fetchMock
      .mockResolvedValueOnce(new Response(JSON.stringify({ detail: 'Unauthorized' }), {
        status: 401,
        headers: {
          'content-type': 'application/json',
        },
      }))
      .mockResolvedValueOnce(new Response('xlsx-bytes', {
        status: 200,
        headers: {
          'content-type': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
          'content-disposition': "attachment; filename*=UTF-8''report%20April.xlsx",
        },
      }))

    const response = await httpPostFile('/api/reports/pm.occupancy/export/xlsx', { limit: 500 })

    expect(authMocks.forceRefreshAccessToken).toHaveBeenCalledTimes(1)
    expect(response.fileName).toBe('report April.xlsx')
    expect(response.contentType).toBe('application/vnd.openxmlformats-officedocument.spreadsheetml.sheet')
    await expect(response.blob.text()).resolves.toBe('xlsx-bytes')
  })
})
