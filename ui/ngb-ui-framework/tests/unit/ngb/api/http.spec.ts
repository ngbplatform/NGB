import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const authMocks = vi.hoisted(() => ({
  forceRefreshAccessToken: vi.fn(),
  getAccessToken: vi.fn(),
}))

vi.mock('../../../../src/ngb/auth/keycloak', () => ({
  forceRefreshAccessToken: authMocks.forceRefreshAccessToken,
  getAccessToken: authMocks.getAccessToken,
}))

import {
  ApiError,
  httpDelete,
  httpGet,
  httpPost,
  httpPostFile,
  httpPut,
  httpRequest,
} from '../../../../src/ngb/api/http'

describe('api http', () => {
  const fetchMock = vi.fn()

  function jsonResponse(body: unknown, status = 400): Response {
    return new Response(JSON.stringify(body), {
      status,
      headers: { 'content-type': 'application/problem+json' },
    })
  }

  async function capturedApiError(request: Promise<unknown>): Promise<ApiError> {
    try {
      await request
    } catch (error) {
      expect(error).toBeInstanceOf(ApiError)
      return error as ApiError
    }
    throw new Error('Expected request to throw ApiError')
  }

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

  it('normalizes PascalCase nested envelopes, explicit issues, paths, scopes, and mixed validation values', () => {
    const error = new ApiError({
      message: 'invalid',
      status: 422,
      url: 'https://api.example/test',
      body: {
        error: {
          Code: 'VALIDATION_PASCAL',
          Kind: 'Validation',
          Context: { operation: 'save' },
          Errors: {
            scalar: ' Scalar message ',
            array: [null, '', ' Array message '],
            empty: null,
            blank: ' ',
          },
          Issues: [
            null,
            { Message: ' ' },
            { Message: 42 },
            { Message: 'Amount required', Path: '$request.payload.fields.parts.lines.rows[2].amount', Scope: ' row ', Code: 'AMOUNT' },
            { message: 'Collection invalid', path: 'items.rows[]' },
            { message: 'Row invalid', path: 'items.rows[3]' },
            { message: 'Form invalid', path: 'payload' },
            { message: 'Explicit scope', path: '_form', scope: 'summary', code: 'FORM' },
          ],
        },
      },
    })

    expect(error.errorCode).toBe('VALIDATION_PASCAL')
    expect(error.kind).toBe('Validation')
    expect(error.context).toEqual({ operation: 'save' })
    expect(error.errors).toEqual({
      scalar: ['Scalar message'],
      array: ['Array message'],
    })
    expect(error.issues).toEqual(expect.arrayContaining([
      { path: 'lines[2].amount', message: 'Amount required', scope: 'row', code: 'AMOUNT' },
      { path: 'items[]', message: 'Collection invalid', scope: 'collection', code: null },
      { path: 'items[3]', message: 'Row invalid', scope: 'row', code: null },
      { path: '_form', message: 'Form invalid', scope: 'form', code: null },
      { path: '_form', message: 'Explicit scope', scope: 'summary', code: 'FORM' },
    ]))
  })

  it('normalizes lowercase nested and flat envelope fallbacks without inventing validation data', () => {
    const nested = new ApiError({
      message: 'nested',
      status: 409,
      url: '/nested',
      body: {
        error: {
          code: 'nested.code',
          kind: 'conflict',
          context: { source: 'nested' },
          errors: {},
          issues: [],
        },
      },
    })
    const flat = new ApiError({
      message: 'flat',
      status: 400,
      url: '/flat',
      body: {
        issues: [
          { message: 'Leading dots', path: '...field...' },
          { message: 'Missing path', path: 42 },
        ],
        context: { source: 'flat' },
      },
    })
    const primitive = new ApiError({ message: 'primitive', status: 500, url: '/primitive', body: [] })

    expect(nested).toMatchObject({
      errorCode: 'nested.code',
      kind: 'conflict',
      context: { source: 'nested' },
      errors: null,
      issues: null,
    })
    expect(flat.issues).toEqual([
      { path: 'field', message: 'Leading dots', scope: 'field', code: null },
      { path: '_form', message: 'Missing path', scope: 'form', code: null },
    ])
    expect(primitive.problem).toBeNull()
    expect(primitive.errorCode).toBeNull()
  })

  it('selects the most useful API error message across every supported response shape', async () => {
    authMocks.getAccessToken.mockResolvedValue(null)
    fetchMock
      .mockResolvedValueOnce(jsonResponse({ detail: 'Specific detail' }, 400))
      .mockResolvedValueOnce(jsonResponse({ issues: [{ message: 'Issue message', path: 'field' }] }, 400))
      .mockResolvedValueOnce(jsonResponse({ errors: { field: ['', 'Error message'] } }, 400))
      .mockResolvedValueOnce(jsonResponse({ message: ' Envelope message ' }, 400))
      .mockResolvedValueOnce(jsonResponse({ title: 'Problem title' }, 400))
      .mockResolvedValueOnce(jsonResponse({ errorCode: 'problem.code' }, 400))
      .mockResolvedValueOnce(jsonResponse({}, 418))
      .mockResolvedValueOnce(new Response('', { status: 503, headers: { 'content-type': 'text/plain' } }))

    const messages: string[] = []
    for (let index = 0; index < 8; index += 1)
      messages.push((await capturedApiError(httpRequest('GET', `absolute-${index}`))).message)

    expect(messages).toEqual([
      'Specific detail',
      'Issue message',
      'Error message',
      'Envelope message',
      'Problem title',
      'problem.code (HTTP 400)',
      'HTTP 418',
      'HTTP 503',
    ])
  })

  it('handles auth refresh failures, retry opt-out, empty responses, custom headers, absolute URLs, and all JSON wrappers', async () => {
    const signal = new AbortController().signal
    authMocks.getAccessToken.mockResolvedValue(null)
    authMocks.forceRefreshAccessToken.mockRejectedValueOnce(new Error('session offline'))
    fetchMock
      .mockResolvedValueOnce(jsonResponse({ detail: 'Unauthorized after refresh failure' }, 401))
      .mockResolvedValueOnce(jsonResponse({ detail: 'Retry disabled' }, 401))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(jsonResponse({ method: 'post' }, 200))
      .mockResolvedValueOnce(jsonResponse({ method: 'put' }, 200))
      .mockResolvedValueOnce(jsonResponse({ method: 'delete' }, 200))
      .mockResolvedValueOnce(jsonResponse({ absolute: true }, 200))
      .mockResolvedValueOnce(jsonResponse({ query: true }, 200))
      .mockResolvedValueOnce(jsonResponse({ emptyQuery: true }, 200))

    await expect(httpRequest('GET', '/unauthorized')).rejects.toMatchObject({ message: 'Unauthorized after refresh failure' })
    await expect(httpRequest('GET', '/no-retry', undefined, { retryOnUnauthorized: false })).rejects.toMatchObject({ message: 'Retry disabled' })
    await expect(httpRequest('GET', '/empty', undefined, { headers: { 'X-Test': 'yes' }, signal })).resolves.toBeUndefined()
    await expect(httpPost('/post', { value: 1 })).resolves.toEqual({ method: 'post' })
    await expect(httpPut('/put', { value: 2 })).resolves.toEqual({ method: 'put' })
    await expect(httpDelete('/delete', { value: 3 })).resolves.toEqual({ method: 'delete' })
    await expect(httpGet('https://other.example/absolute', null)).resolves.toEqual({ absolute: true })
    await expect(httpGet('/with-existing?first=1', { second: 2 })).resolves.toEqual({ query: true })
    await expect(httpGet('/empty-query', { blank: '', skipped: null })).resolves.toEqual({ emptyQuery: true })

    expect(authMocks.forceRefreshAccessToken).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls[2]?.[1]).toEqual(expect.objectContaining({
      headers: { Accept: 'application/json', 'X-Test': 'yes' },
      signal,
    }))
    expect(fetchMock.mock.calls[6]?.[0]).toBe('https://other.example/absolute')
    expect(fetchMock.mock.calls[7]?.[0]).toContain('?first=1&second=2')
    expect(fetchMock.mock.calls[8]?.[0]).toBe('https://app.example/empty-query')
  })

  it('handles malformed JSON and unreadable text bodies without masking protocol diagnostics', async () => {
    authMocks.getAccessToken.mockResolvedValue(null)
    fetchMock
      .mockResolvedValueOnce(new Response('{broken', {
        status: 500,
        headers: { 'content-type': 'application/json' },
      }))
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        headers: new Headers(),
        text: vi.fn().mockRejectedValue(new Error('stream failed')),
      } as unknown as Response)

    await expect(httpRequest('GET', '/broken-json')).rejects.toMatchObject({ message: 'HTTP 500', body: undefined })
    await expect(httpRequest('GET', '/broken-text')).rejects.toMatchObject({
      message: expect.stringContaining("Expected JSON but got 'unknown'"),
      body: undefined,
    })
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

  it('handles file auth failures and every supported content-disposition filename form', async () => {
    authMocks.getAccessToken.mockResolvedValue(null)
    authMocks.forceRefreshAccessToken.mockRejectedValueOnce(new Error('refresh failed'))
    fetchMock
      .mockResolvedValueOnce(jsonResponse({ detail: 'File unauthorized' }, 401))
      .mockResolvedValueOnce(new Response('a', { status: 200, headers: { 'content-disposition': 'attachment; filename="basic.csv"' } }))
      .mockResolvedValueOnce(new Response('b', { status: 200, headers: { 'content-disposition': 'attachment; filename=plain.csv' } }))
      .mockResolvedValueOnce(new Response('c', { status: 200, headers: { 'content-disposition': "attachment; filename*=UTF-8''bad%ZZ.csv" } }))
      .mockResolvedValueOnce(new Response('no-name', { status: 200, headers: { 'content-disposition': 'attachment' } }))
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        headers: new Headers(),
        blob: vi.fn().mockResolvedValue(new Blob(['d'])),
      } as unknown as Response)

    await expect(httpPostFile('/file-auth')).rejects.toMatchObject({ message: 'File unauthorized' })
    await expect(httpPostFile('/basic', undefined)).resolves.toMatchObject({ fileName: 'basic.csv' })
    await expect(httpPostFile('/plain', null)).resolves.toMatchObject({ fileName: 'plain.csv' })
    await expect(httpPostFile('/invalid-utf8')).resolves.toMatchObject({ fileName: 'bad%ZZ.csv' })
    await expect(httpPostFile('/missing-filename')).resolves.toMatchObject({ fileName: null })
    await expect(httpPostFile('/unnamed')).resolves.toMatchObject({ fileName: null, contentType: null })
  })
})
